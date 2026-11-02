// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Tests;

/// <summary>Doublons sur un serveur : le serveur calcule si possible, et rien n'est lu par le réseau sans accord.</summary>
public class RemoteDuplicatesTests
{
    private const string Empty = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static FileEntry File(string path, long size, FileAttributes attributes = FileAttributes.Normal) =>
        new(RemotePath.FileName(path), $"sftp://serveur{path}", false, size, new DateTime(2026, 1, 1), attributes);

    private static MemoryFileSystem Server(bool serverHash = true)
    {
        var fs = new MemoryFileSystem { HasServerHash = serverHash };
        fs.Nodes["/a.bin"] = [1, 2, 3, 4];
        fs.Nodes["/copie.bin"] = [1, 2, 3, 4];
        fs.Nodes["/autre.bin"] = [9, 9, 9, 9];
        fs.Nodes["/seul.bin"] = [1, 2, 3, 4, 5];
        return fs;
    }

    private static FileEntry[] Files() => [File("/a.bin", 4), File("/copie.bin", 4), File("/autre.bin", 4), File("/seul.bin", 5)];

    private static Func<int, long, Task<bool>> NeverAsked => (_, _) => throw new InvalidOperationException("rien ne doit être demandé");

    private static string[] Names(RemoteDuplicateResult result) => result.Groups.Single().Files.Select(f => f.Name).ToArray();

    [Fact]
    public async Task The_server_computes_and_nothing_crosses_the_network()
    {
        var fs = Server();

        var result = await RemoteDuplicates.FindAsync(fs, Files(), NeverAsked);

        Assert.Equal(["a.bin", "copie.bin"], Names(result));
        Assert.Equal(4, result.Groups[0].Wasted);
        Assert.Equal(0, fs.BytesMoved);
        Assert.Equal(3, fs.ServerHashes); // « seul.bin » n'a aucune taille commune : pas d'empreinte
    }

    [Fact]
    public async Task Without_commands_the_volume_is_announced_before_reading()
    {
        var fs = Server(serverHash: false);
        (int Files, long Bytes) asked = default;

        var result = await RemoteDuplicates.FindAsync(fs, Files(), (files, bytes) => { asked = (files, bytes); return Task.FromResult(true); });

        Assert.Equal((3, 12L), asked);
        Assert.Equal(12, fs.BytesMoved);
        Assert.Equal(["a.bin", "copie.bin"], Names(result));
    }

    [Fact]
    public async Task Refusing_the_network_read_reads_nothing()
    {
        var fs = Server(serverHash: false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => RemoteDuplicates.FindAsync(fs, Files(), (_, _) => Task.FromResult(false)));

        Assert.Equal(0, fs.BytesMoved);
    }

    [Fact]
    public async Task Nothing_is_asked_when_no_sizes_match()
    {
        var result = await RemoteDuplicates.FindAsync(Server(serverHash: false), [File("/a.bin", 4), File("/seul.bin", 5)], NeverAsked);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task A_folder_given_twice_does_not_make_a_file_its_own_duplicate()
    {
        var result = await RemoteDuplicates.FindAsync(Server(), [File("/seul.bin", 5), File("/seul.bin", 5)], NeverAsked);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task A_link_is_never_a_duplicate_of_its_target()
    {
        var fs = Server();
        fs.Nodes["/lien.bin"] = [1, 2, 3, 4];

        var result = await RemoteDuplicates.FindAsync(fs, [File("/a.bin", 4), File("/lien.bin", 4, FileAttributes.ReparsePoint)], NeverAsked);

        Assert.Empty(result.Groups);
        Assert.Equal(0, fs.ServerHashes);
    }

    [Fact]
    public async Task Empty_files_are_ignored()
    {
        var fs = Server();
        fs.Nodes["/v1"] = [];
        fs.Nodes["/v2"] = [];

        var result = await RemoteDuplicates.FindAsync(fs, [File("/v1", 0), File("/v2", 0)], NeverAsked);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Unreadable_files_are_counted_not_taken_for_different()
    {
        var fs = Server();
        fs.Unreadable.Add("/autre.bin");

        var result = await RemoteDuplicates.FindAsync(fs, Files(), NeverAsked);

        Assert.Equal(1, result.Unreadable);
        Assert.Equal(["a.bin", "copie.bin"], Names(result));
    }

    [Theory]
    [InlineData(Empty + "  -\n", Empty)]
    [InlineData("\\" + Empty + "  /a\\nb\n", Empty)] // nom avec un caractère spécial
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855  x", Empty)]
    [InlineData("sha256sum: introuvable", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Command_output_is_read_strictly(string? output, string? expected) => Assert.Equal(expected, ServerHashResult.ParseHash(output));

    [Fact]
    public void The_command_counts_only_if_it_really_hashes()
    {
        Assert.True(ServerHashResult.IsProbeOk(0, Empty + "  -\n"));
        Assert.False(ServerHashResult.IsProbeOk(127, string.Empty));
        Assert.False(ServerHashResult.IsProbeOk(0, "bienvenue sur ce serveur\n")); // shell restreint qui répond autre chose
    }
}
