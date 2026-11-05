// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Archives;
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Tests;

/// <summary>Une archive d'un serveur ouverte dans un panneau : copie temporaire, remplacement contrôlé.</summary>
[Collection("Copies d'archives")]
public sealed class ServerArchiveTests : IDisposable
{
    private const string Folder = "sftp://demo/data";
    private const string OnServer = "/data/backup.zip";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tp-server-archive-" + Guid.NewGuid().ToString("N"));
    private readonly MemoryFileSystem _server = new();
    private readonly List<ServerArchive> _opened = [];

    public ServerArchiveTests()
    {
        Directory.CreateDirectory(_dir);
        _server.AddDir("/data");
    }

    public void Dispose()
    {
        foreach (var copy in _opened) ServerArchives.Close(copy);
        Directory.Delete(_dir, recursive: true);
    }

    private string Local(string name, string content = "bonjour")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<ServerArchive> OpenAsync()
    {
        var zip = Path.Combine(_dir, "source.zip");
        await ArchiveService.CreateAsync([Local("dedans.txt")], zip, ArchiveKind.Zip);
        _server.Nodes[OnServer] = await File.ReadAllBytesAsync(zip);
        _server.Dates[OnServer] = new DateTime(2024, 1, 1, 12, 0, 0);
        var copy = await ServerArchives.OpenAsync(_ => Task.FromResult<IRemoteFileSystem>(_server), OnServer, Folder, null, CancellationToken.None);
        _opened.Add(copy);
        return copy;
    }

    private async Task<IReadOnlyList<string>> ServerNamesAsync()
    {
        var zip = Path.Combine(_dir, "lecture.zip");
        await File.WriteAllBytesAsync(zip, _server.Nodes[OnServer]!);
        return ArchiveIndex.Load(zip).ChildrenOf("").Select(i => i.Key).ToList();
    }

    private static ArchiveEdits Adding(string file, bool move = false) =>
        new() { Add = [new ArchiveAddition(file, string.Empty)], MoveSources = move };

    [Fact]
    public async Task Inside_the_copy_the_panel_goes_back_to_the_server_folder()
    {
        var copy = await OpenAsync();
        var inside = ArchivePath.Combine(copy.LocalPath, "");

        Assert.Same(copy, ServerArchives.Find(inside));
        Assert.Equal(Folder, new ArchiveFileSource().GetParent(inside));
        Assert.True(copy.IsOn("demo"));
        Assert.Equal(["demo", "data", "backup.zip"], Breadcrumb.Split(inside).Select(s => s.Name));
    }

    [Fact]
    public async Task An_edit_reaches_the_server()
    {
        var copy = await OpenAsync();

        await ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(Local("ajout.txt")));

        Assert.Contains("ajout.txt", await ServerNamesAsync());
        Assert.Contains("ajout.txt", ArchiveIndex.Load(copy.LocalPath).ChildrenOf("").Select(i => i.Key));
        Assert.DoesNotContain(_server.Nodes.Keys, k => k.EndsWith(RemoteTransfer.PartSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Our_own_edit_does_not_block_the_next_one()
    {
        var copy = await OpenAsync();

        await ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(Local("un.txt")));
        await ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(Local("deux.txt")));

        Assert.Contains("deux.txt", await ServerNamesAsync());
    }

    [Fact]
    public async Task An_archive_changed_on_the_server_is_never_replaced()
    {
        var copy = await OpenAsync();
        var before = await File.ReadAllBytesAsync(copy.LocalPath);
        _server.AddFile(OnServer, "version de quelqu'un d'autre", new DateTime(2025, 6, 1, 8, 0, 0));
        var theirs = _server.Nodes[OnServer];

        await Assert.ThrowsAsync<FileOperationException>(() => ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(Local("ajout.txt"))));

        Assert.Equal(theirs, _server.Nodes[OnServer]);
        Assert.Equal(before, await File.ReadAllBytesAsync(copy.LocalPath));
    }

    [Fact]
    public async Task A_refused_upload_changes_nothing_on_either_side()
    {
        var copy = await OpenAsync();
        var before = await File.ReadAllBytesAsync(copy.LocalPath);
        var onServer = _server.Nodes[OnServer];
        _server.RefuseRename = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(Local("ajout.txt"))));

        Assert.Equal(onServer, _server.Nodes[OnServer]);
        Assert.Equal(before, await File.ReadAllBytesAsync(copy.LocalPath));
        Assert.DoesNotContain(_server.Nodes.Keys, k => k.EndsWith(RemoteTransfer.PartSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cut_during_the_replacement_keeps_the_only_complete_copy()
    {
        var copy = await OpenAsync();
        _server.CutDuringReplace = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(Local("ajout.txt"))));

        Assert.True(_server.Nodes.ContainsKey(OnServer + RemoteTransfer.PartSuffix));
    }

    [Fact]
    public async Task A_move_keeps_the_file_on_the_pc_when_the_server_refuses()
    {
        var copy = await OpenAsync();
        var file = Local("ajout.txt");
        _server.RefuseRename = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(file, move: true)));

        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task A_private_archive_is_protected_before_its_content_is_written()
    {
        _server.SupportsPermissions = true;
        _server.Modes[OnServer] = 0x180; // 600
        var copy = await OpenAsync();
        var part = OnServer + RemoteTransfer.PartSuffix;
        _server.RefuseRename = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ArchiveEditor.ApplyAsync(copy.LocalPath, Adding(Local("ajout.txt"))));

        Assert.Equal(0x180, _server.Modes[part]);
    }

    [Fact]
    public async Task Closing_removes_the_copy()
    {
        var copy = await OpenAsync();
        var folder = Path.GetDirectoryName(copy.LocalPath)!;

        ServerArchives.Close(copy);

        Assert.False(Directory.Exists(folder));
        Assert.Null(ServerArchives.Find(copy.LocalPath));
    }

    [Fact]
    public async Task A_missing_archive_leaves_no_copy()
    {
        var before = ServerArchives.All.Count;

        await Assert.ThrowsAsync<RemoteException>(() =>
            ServerArchives.OpenAsync(_ => Task.FromResult<IRemoteFileSystem>(_server), "/data/absente.zip", Folder, null, CancellationToken.None));

        Assert.Equal(before, ServerArchives.All.Count);
    }

    [Fact]
    public async Task Copying_out_of_an_archive_reaches_the_server()
    {
        var zip = Path.Combine(_dir, "local.zip");
        await ArchiveService.CreateAsync([Local("a.txt")], zip, ArchiveKind.Zip);

        await RemoteArchives.ExtractToServerAsync(_server, zip, ["a.txt"], "", "/data", ConflictPolicy.Overwrite, move: false, null, CancellationToken.None);

        Assert.Equal("bonjour", System.Text.Encoding.UTF8.GetString(_server.Nodes["/data/a.txt"]!));
        Assert.Contains("a.txt", ArchiveIndex.Load(zip).ChildrenOf("").Select(i => i.Key));
    }

    [Fact]
    public async Task Moving_out_of_an_archive_removes_it_only_when_it_was_sent()
    {
        var zip = Path.Combine(_dir, "local.zip");
        await ArchiveService.CreateAsync([Local("a.txt"), Local("b.txt")], zip, ArchiveKind.Zip);
        _server.AddFile("/data/a.txt", "déjà là");

        await RemoteArchives.ExtractToServerAsync(_server, zip, ["a.txt", "b.txt"], "", "/data", ConflictPolicy.Skip, move: true, null, CancellationToken.None);

        Assert.Equal("déjà là", System.Text.Encoding.UTF8.GetString(_server.Nodes["/data/a.txt"]!));
        Assert.Equal(["a.txt"], ArchiveIndex.Load(zip).ChildrenOf("").Select(i => i.Key));
    }
}
