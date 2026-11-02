// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Net.Sockets;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Tests;

public class TransferResumeTests
{
    private const int Size = 300_000;

    private static byte[] Data(byte seed = 1, int size = Size)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static MemoryFileSystem ServerWith(string path, byte[] data)
    {
        var server = new MemoryFileSystem();
        server.Nodes[path] = data;
        server.Dates[path] = new DateTime(2026, 1, 1, 12, 0, 0);
        return server;
    }

    private static TransferResume Resume(int retries = 3) => new() { MaxRetries = retries, RetryDelay = _ => TimeSpan.Zero, MinBytes = 1000 };

    private static Task Download(MemoryFileSystem server, TransferResume resume, IReadOnlyList<RemoteItem> items, string dest,
        ConflictPolicy policy = ConflictPolicy.Overwrite, CancellationToken ct = default, bool deleteSource = false) =>
        resume.RunAsync(_ => Task.FromResult<IRemoteFileSystem>(server),
            fs => RemoteTransfer.DownloadAsync(fs, items, dest, policy, null, ct, deleteSource: deleteSource, resume: resume), null, ct);

    private static Task Upload(MemoryFileSystem server, TransferResume resume, IReadOnlyList<string> paths, string dir,
        ConflictPolicy policy = ConflictPolicy.Overwrite, CancellationToken ct = default) =>
        resume.RunAsync(_ => Task.FromResult<IRemoteFileSystem>(server),
            fs => RemoteTransfer.UploadAsync(fs, paths, dir, policy, null, ct, resume: resume), null, ct);

    private static string Part(string path) => path + RemoteTransfer.PartSuffix;

    [Fact]
    public async Task Cut_download_continues_where_it_stopped()
    {
        using var dir = new TempDir();
        var data = Data();
        var server = ServerWith("/film.mkv", data);
        server.CutAfter.Enqueue(200_000);

        await Download(server, Resume(), await server.ListAsync("/", default), dir.Path);

        var target = Path.Combine(dir.Path, "film.mkv");
        Assert.Equal(data, File.ReadAllBytes(target));
        Assert.False(File.Exists(Part(target)));
        // 200 000 + le reste, plus les 64 Ko relus pour comparer : pas de second envoi complet.
        Assert.Equal(Size + TransferResume.TailBytes, server.BytesMoved);
    }

    [Fact]
    public async Task Cut_upload_continues_where_it_stopped()
    {
        using var dir = new TempDir();
        var data = Data();
        var local = Path.Combine(dir.Path, "film.mkv");
        File.WriteAllBytes(local, data);
        var server = new MemoryFileSystem();
        server.CutAfter.Enqueue(200_000);

        await Upload(server, Resume(), [local], "/");

        Assert.Equal(data, server.Nodes["/film.mkv"]);
        Assert.False(server.Nodes.ContainsKey(Part("/film.mkv")));
        // 200 000 + le reste, plus les 64 Ko relus pour comparer : pas de second envoi complet.
        Assert.Equal(Size + TransferResume.TailBytes, server.BytesMoved);
    }

    [Fact]
    public async Task File_changed_on_the_server_starts_again()
    {
        using var dir = new TempDir();
        var server = ServerWith("/a.bin", Data(1));
        var items = await server.ListAsync("/", default);
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() => Download(server, resume, items, dir.Path));

        var changed = Data(2);
        server.Nodes["/a.bin"] = changed;
        server.Dates["/a.bin"] = new DateTime(2026, 2, 1);
        await Download(server, resume, items, dir.Path);

        Assert.Equal(changed, File.ReadAllBytes(Path.Combine(dir.Path, "a.bin")));
    }

    [Fact]
    public async Task Different_tail_is_never_joined()
    {
        using var dir = new TempDir();
        var data = Data();
        var server = ServerWith("/a.bin", data);
        var items = await server.ListAsync("/", default);
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() => Download(server, resume, items, dir.Path));

        // Même taille, même date, mais le morceau gardé est abîmé.
        var part = Part(Path.Combine(dir.Path, "a.bin"));
        var broken = File.ReadAllBytes(part);
        broken[^1] ^= 0xFF;
        File.WriteAllBytes(part, broken);

        await Download(server, resume, items, dir.Path);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dir.Path, "a.bin")));
    }

    [Fact]
    public async Task Different_tail_on_the_server_is_never_joined()
    {
        using var dir = new TempDir();
        var data = Data();
        var local = Path.Combine(dir.Path, "a.bin");
        File.WriteAllBytes(local, data);
        var server = new MemoryFileSystem();
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() => Upload(server, resume, [local], "/"));

        server.Nodes[Part("/a.bin")]![^1] ^= 0xFF;
        await Upload(server, resume, [local], "/");
        Assert.Equal(data, server.Nodes["/a.bin"]);
    }

    [Fact]
    public async Task Protocol_without_resume_starts_the_file_again()
    {
        using var dir = new TempDir();
        var data = Data();
        var server = ServerWith("/a.bin", data);
        server.ResumeSupported = false;
        server.CutAfter.Enqueue(100_000);

        await Download(server, Resume(), await server.ListAsync("/", default), dir.Path);

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dir.Path, "a.bin")));
        Assert.Equal(100_000 + Size, server.BytesMoved);
    }

    [Fact]
    public async Task Server_refusing_to_resume_a_download_starts_the_file_again()
    {
        using var dir = new TempDir();
        var data = Data();
        var server = ServerWith("/a.bin", data);
        var items = await server.ListAsync("/", default);
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() => Download(server, resume, items, dir.Path));

        server.RefuseResume = true;
        await Download(server, resume, items, dir.Path);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dir.Path, "a.bin")));
    }

    [Fact]
    public async Task Server_refusing_to_resume_an_upload_starts_the_file_again()
    {
        using var dir = new TempDir();
        var data = Data();
        var local = Path.Combine(dir.Path, "a.bin");
        File.WriteAllBytes(local, data);
        var server = new MemoryFileSystem();
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() => Upload(server, resume, [local], "/"));

        server.RefuseResume = true;
        await Upload(server, resume, [local], "/");
        Assert.Equal(data, server.Nodes["/a.bin"]);
        Assert.Equal(2, server.Uploads.Count);
    }

    [Fact]
    public async Task Small_file_leaves_no_part_behind_after_a_cut()
    {
        using var dir = new TempDir();
        var server = ServerWith("/a.bin", Data(size: 500));
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100);
        await Assert.ThrowsAsync<RemoteException>(() => Download(server, resume, server.ListAsync("/", default).Result, dir.Path));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task Refused_login_is_not_tried_again()
    {
        int connections = 0;
        var resume = Resume();
        await Assert.ThrowsAsync<RemoteException>(() => resume.RunAsync(_ =>
        {
            connections++;
            throw new RemoteException("refusé", new Renci.SshNet.Common.SshAuthenticationException("refusé"));
        }, _ => Task.CompletedTask, null, default));
        Assert.Equal(1, connections);
    }

    [Fact]
    public async Task Server_down_stops_after_the_chosen_number_of_tries()
    {
        int connections = 0;
        var resume = Resume(retries: 3);
        await Assert.ThrowsAsync<SocketException>(() => resume.RunAsync(_ =>
        {
            connections++;
            throw new SocketException((int)SocketError.ConnectionRefused);
        }, _ => Task.CompletedTask, null, default));
        Assert.Equal(4, connections);
    }

    [Fact]
    public async Task Connection_lost_is_reported_without_a_cut_file_name()
    {
        var notices = new List<string>();
        var progress = new Direct(p => { if (p.Notice is { } n) notices.Add(n); });
        var resume = Resume(retries: 1);
        await Assert.ThrowsAsync<SocketException>(() => resume.RunAsync(_ => throw new SocketException((int)SocketError.TimedOut), _ => Task.CompletedTask, progress, default));
        Assert.Contains("tentative 1 sur 1", Assert.Single(notices));
    }

    private sealed class Direct(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }

    [Fact]
    public async Task Resume_button_finishes_the_work_after_the_tries_ran_out()
    {
        using var dir = new TempDir();
        var data = Data();
        var server = ServerWith("/a.bin", data);
        var items = await server.ListAsync("/", default);
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(150_000);

        await Assert.ThrowsAsync<RemoteException>(() => Download(server, resume, items, dir.Path));
        var target = Path.Combine(dir.Path, "a.bin");
        Assert.Equal(150_000, new FileInfo(Part(target)).Length); // gardé pour la reprise

        await Download(server, resume, items, dir.Path);
        Assert.Equal(data, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task Finished_files_are_not_sent_twice()
    {
        using var dir = new TempDir();
        foreach (var name in new[] { "1.bin", "2.bin", "3.bin" }) File.WriteAllBytes(Path.Combine(dir.Path, name), Data());
        var server = new MemoryFileSystem();
        var paths = new[] { "1.bin", "2.bin", "3.bin" }.Select(n => Path.Combine(dir.Path, n)).ToList();
        server.CutAfter.Enqueue(long.MaxValue); // le 1er passe
        server.CutAfter.Enqueue(100_000);       // le 2e est coupé

        await Upload(server, Resume(), paths, "/");

        Assert.Equal(1, server.Uploads.Count(u => u == Part("/1.bin")));
        Assert.All(new[] { "/1.bin", "/2.bin", "/3.bin" }, p => Assert.True(server.Nodes.ContainsKey(p)));
    }

    [Fact]
    public async Task A_kept_copy_keeps_its_name_after_a_cut()
    {
        using var dir = new TempDir();
        var data = Data();
        var server = ServerWith("/a.bin", data);
        var items = await server.ListAsync("/", default);
        File.WriteAllText(Path.Combine(dir.Path, "a.bin"), "déjà là");
        server.CutAfter.Enqueue(100_000);

        await Download(server, Resume(), items, dir.Path, ConflictPolicy.RenameNew);

        Assert.Equal("déjà là", File.ReadAllText(Path.Combine(dir.Path, "a.bin")));
        var copies = Directory.GetFiles(dir.Path).Where(f => !f.EndsWith("a.bin", StringComparison.Ordinal)).ToList();
        Assert.Equal(data, File.ReadAllBytes(Assert.Single(copies)));
    }

    [Fact]
    public async Task Move_deletes_the_source_only_once_everything_arrived()
    {
        using var dir = new TempDir();
        var data = Data();
        var server = ServerWith("/a.bin", data);
        server.Nodes["/b.bin"] = Data(2);
        var items = await server.ListAsync("/", default);
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(long.MaxValue);
        server.CutAfter.Enqueue(100_000);

        await Assert.ThrowsAsync<RemoteException>(() => Download(server, resume, items, dir.Path, deleteSource: true));
        Assert.True(server.Nodes.ContainsKey("/a.bin"));

        await Download(server, resume, items, dir.Path, deleteSource: true);
        Assert.False(server.Nodes.ContainsKey("/a.bin"));
        Assert.False(server.Nodes.ContainsKey("/b.bin"));
    }

    [Fact]
    public async Task Cancelling_removes_the_part()
    {
        using var dir = new TempDir();
        var server = ServerWith("/a.bin", Data());
        var items = await server.ListAsync("/", default);
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() => Download(server, resume, items, dir.Path));
        var part = Part(Path.Combine(dir.Path, "a.bin"));
        Assert.True(File.Exists(part));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(server, resume, items, dir.Path, ct: cts.Token));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public async Task Giving_up_removes_the_part_on_the_server()
    {
        using var dir = new TempDir();
        var local = Path.Combine(dir.Path, "a.bin");
        File.WriteAllBytes(local, Data());
        var server = new MemoryFileSystem();
        var resume = Resume(retries: 0);
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() => Upload(server, resume, [local], "/"));
        Assert.True(server.Nodes.ContainsKey(Part("/a.bin")));

        await resume.DiscardAsync();
        Assert.False(server.Nodes.ContainsKey(Part("/a.bin")));
    }

    [Fact]
    public async Task Without_resume_the_part_is_removed_as_before()
    {
        using var dir = new TempDir();
        var server = ServerWith("/a.bin", Data());
        server.CutAfter.Enqueue(100_000);
        await Assert.ThrowsAsync<RemoteException>(() =>
            RemoteTransfer.DownloadAsync(server, server.ListAsync("/", default).Result, dir.Path, ConflictPolicy.Overwrite, null, default));
        Assert.False(File.Exists(Part(Path.Combine(dir.Path, "a.bin"))));
    }

    [Fact]
    public void A_full_disk_or_a_refusal_is_not_a_lost_connection()
    {
        var connected = new MemoryFileSystem();
        Assert.False(ConnectionFailure.IsLost(new IOException("Espace insuffisant sur le disque."), connected));
        Assert.False(ConnectionFailure.IsLost(new RemoteException("droits", new Renci.SshNet.Common.SftpPermissionDeniedException("non")), connected));
        Assert.False(ConnectionFailure.IsLost(new RemoteException("FTP", new FluentFTP.Exceptions.FtpAuthenticationException(new FluentFTP.FtpReply { Code = "530", Message = "non" })), connected));
        Assert.True(ConnectionFailure.IsLost(new RemoteException("coupé", new Renci.SshNet.Common.SshConnectionException("x")), connected));
        Assert.True(ConnectionFailure.IsLost(new IOException("lecture", new SocketException((int)SocketError.ConnectionReset)), connected));
        Assert.True(ConnectionFailure.IsLost(new HttpRequestException("réseau"), connected));
        Assert.False(ConnectionFailure.IsLost(new HttpRequestException("interdit", null, System.Net.HttpStatusCode.Forbidden), connected));
    }

    [Fact]
    public void Retries_wait_longer_each_time()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), TransferResume.DefaultDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(15), TransferResume.DefaultDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(30), TransferResume.DefaultDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(30), TransferResume.DefaultDelay(9));
    }
}
