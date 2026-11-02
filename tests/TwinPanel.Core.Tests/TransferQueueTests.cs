// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Reflection;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Tests;

/// <summary>Transferts gardés d'une session à l'autre : ce qui est relu doit permettre de continuer sans rien refaire.</summary>
public class TransferQueueTests
{
    private const int Size = 300_000;
    private static readonly DateTime Original = new(2026, 1, 1, 12, 0, 0);

    private static string QueueFile(TempDir dir) => Path.Combine(dir.Path, "config", "transfers.json");

    private static TransferQueueStore Store(TempDir dir)
    {
        var store = new TransferQueueStore(QueueFile(dir), TimeSpan.FromHours(1));
        store.Load();
        return store;
    }

    /// <summary>Comme une fermeture : on enregistre, puis tout est relu par un TwinPanel neuf.</summary>
    private static SavedTransfer Restart(TempDir dir, TransferQueueStore store)
    {
        store.Dispose();
        using var next = Store(dir);
        return Assert.Single(next.Pending);
    }

    private static byte[] Data(int size = Size)
    {
        var data = new byte[size];
        new Random(5).NextBytes(data);
        return data;
    }

    private static TransferResume Resume(TransferPlan? plan = null, int retries = 0) =>
        new() { MaxRetries = retries, RetryDelay = _ => TimeSpan.Zero, MinBytes = 1000, Plan = plan };

    private static async Task Cut(Func<Task> transfer) => await Assert.ThrowsAnyAsync<IOException>(transfer);

    private static Task Download(MemoryFileSystem server, TransferResume resume, IReadOnlyList<RemoteItem> items, string dest, bool deleteSource = false) =>
        resume.RunAsync(_ => Task.FromResult<IRemoteFileSystem>(server),
            fs => RemoteTransfer.DownloadAsync(fs, items, dest, ConflictPolicy.Overwrite, null, default, deleteSource: deleteSource, resume: resume), null, default);

    private static Task Upload(MemoryFileSystem server, TransferResume resume, IReadOnlyList<string> paths) =>
        resume.RunAsync(_ => Task.FromResult<IRemoteFileSystem>(server),
            fs => RemoteTransfer.UploadAsync(fs, paths, "/", ConflictPolicy.Overwrite, null, default, resume: resume), null, default);

    [Fact]
    public void Everything_needed_to_continue_comes_back()
    {
        using var dir = new TempDir();
        var modified = new DateTime(2026, 7, 1, 2, 30, 15, DateTimeKind.Local).AddTicks(1234567);
        var store = Store(dir);
        var source = Guid.NewGuid();
        store.Add(new SavedTransfer
        {
            Title = "Copie",
            SourceProfile = source,
            Destination = @"D:\Films",
            Policy = ConflictPolicy.RenameNew,
            Move = true,
            LocalPaths = [@"C:\a.txt"],
            Items = [new RemoteItem("a.txt", "/a.txt", false, 5, modified, Permissions: "rw-r--r--", Mode: 420, Owner: "root", Uid: 0)],
            Plan = new TransferPlan
            {
                IsUpload = true,
                Files = [new TransferPlanFile { Source = "/a.txt", Target = @"D:\Films\a.txt", Size = 5, Modified = modified, Top = 1, Resolved = true, Started = true, StampSize = 5, StampModified = modified }],
                Dirs = [new TransferPlanDir { Path = "/d", Exists = true, Done = true }],
                Broken = [2],
                Deleted = [3],
                Skipped = ["x"],
            },
            Commands = new ServerCommandState { Started = ["/b/a"], Finished = new Dictionary<string, long> { ["/b/c"] = 9 } },
        });

        var back = Restart(dir, store);

        Assert.Equal("Copie", back.Title);
        Assert.Equal(source, back.SourceProfile);
        Assert.Null(back.TargetProfile);
        Assert.Equal(ConflictPolicy.RenameNew, back.Policy);
        Assert.True(back.Move);
        Assert.Equal(@"C:\a.txt", Assert.Single(back.LocalPaths));
        var item = Assert.Single(back.Items);
        Assert.Equal(("rw-r--r--", 420, "root", 0L), (item.Permissions, item.Mode, item.Owner, item.Uid));
        var file = Assert.Single(back.Plan!.Files);
        Assert.Equal(modified.Ticks, file.Modified.Ticks); // comparée à la date du fichier sur le disque avant de reprendre
        Assert.Equal(modified.Ticks, item.Modified.Ticks);
        Assert.Equal(1, file.Top);
        Assert.True(file.Resolved && file.Started && !file.Done);
        Assert.Equal(2, Assert.Single(back.Plan.Broken));
        Assert.Equal(3, Assert.Single(back.Plan.Deleted));
        Assert.True(back.Plan.IsUpload);
        Assert.Contains("/b/a", back.Commands!.Started);
        Assert.Equal(9, back.Commands.Finished["/b/c"]);
    }

    [Theory]
    [InlineData(0)] // 2 h 30 en heure d'été
    [InlineData(1)] // 2 h 30 une heure plus tard, en heure d'hiver
    public void Date_in_the_repeated_hour_of_autumn_stays_exact(int hour)
    {
        using var dir = new TempDir();
        var modified = new DateTime(2026, 10, 25, hour, 30, 0, DateTimeKind.Utc).ToLocalTime();
        var store = Store(dir);
        store.Add(new SavedTransfer { Plan = new TransferPlan { Files = [new TransferPlanFile { Modified = modified }] } });

        var back = Restart(dir, store);

        Assert.Equal(modified.ToUniversalTime(), Assert.Single(back.Plan!.Files).Modified.ToUniversalTime());
        Assert.Equal(modified, back.Plan.Files[0].Modified);
    }

    [Fact]
    public async Task Download_continues_after_a_restart_without_fetching_again()
    {
        using var dir = new TempDir();
        var server = new MemoryFileSystem();
        var (first, second) = (Data(), Data());
        server.Nodes["/1.bin"] = first;
        server.Nodes["/2.bin"] = second;
        server.Dates["/1.bin"] = server.Dates["/2.bin"] = Original;
        var items = await server.ListAsync("/", default);
        server.CutAfter.Enqueue(Size); // le premier fichier passe entier
        server.CutAfter.Enqueue(200_000); // le second est coupé

        var store = Store(dir);
        var saved = new SavedTransfer { Live = Resume(), Items = items.ToList() };
        store.Add(saved);
        await Cut(() => Download(server, saved.Live, items, dir.Path));
        var back = Restart(dir, store);
        long before = server.BytesMoved;

        await Download(server, Resume(back.Plan), back.Items, dir.Path);

        Assert.Equal(second, File.ReadAllBytes(Path.Combine(dir.Path, "2.bin")));
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(dir.Path, "1.bin")));
        Assert.False(File.Exists(Path.Combine(dir.Path, "2.bin" + RemoteTransfer.PartSuffix)));
        Assert.Equal(Size - 200_000 + TransferResume.TailBytes, server.BytesMoved - before); // le reste, et la fin vérifiée
    }

    [Fact]
    public async Task Upload_continues_after_a_restart_without_sending_again()
    {
        using var dir = new TempDir();
        var data = Data();
        var path = Path.Combine(dir.Path, "film.mkv");
        File.WriteAllBytes(path, data);
        var server = new MemoryFileSystem();
        server.CutAfter.Enqueue(200_000);

        var store = Store(dir);
        var saved = new SavedTransfer { Live = Resume(), LocalPaths = [path] };
        store.Add(saved);
        await Cut(() => Upload(server, saved.Live, [path]));
        var back = Restart(dir, store);
        long before = server.BytesMoved;

        await Upload(server, Resume(back.Plan), back.LocalPaths);

        Assert.Equal(data, server.Nodes["/film.mkv"]);
        Assert.False(server.Nodes.ContainsKey("/film.mkv" + RemoteTransfer.PartSuffix));
        // Reste à envoyer 100 000 octets, plus la vérification de la fin du morceau : renvoyer tout serait visible.
        Assert.True(server.BytesMoved - before < 200_000, $"{server.BytesMoved - before} octets transférés");
    }

    [Fact]
    public async Task Waiting_transfer_starts_normally_after_a_restart()
    {
        using var dir = new TempDir();
        var data = Data(5000);
        var path = Path.Combine(dir.Path, "a.bin");
        File.WriteAllBytes(path, data);
        var store = Store(dir);
        store.Add(new SavedTransfer { LocalPaths = [path] });
        var back = Restart(dir, store);

        var server = new MemoryFileSystem();
        await Upload(server, Resume(back.Plan), back.LocalPaths);

        Assert.Null(back.Plan);
        Assert.Equal(data, server.Nodes["/a.bin"]);
    }

    [Fact]
    public async Task Server_copy_finished_while_twinpanel_was_closed_is_not_started_again()
    {
        using var dir = new TempDir();
        var fs = new MemoryFileSystem { HasServerMove = true };
        fs.AddDir("/a");
        fs.AddDir("/b");
        fs.Nodes["/a/film.mkv"] = new byte[1000];
        fs.Dates["/a/film.mkv"] = Original;
        var items = await fs.ListAsync("/a", default);

        var store = Store(dir);
        var saved = new SavedTransfer { Commands = new ServerCommandState(), Items = items.ToList() };
        saved.Commands.Started.Add("/b/film.mkv"); // « cp » lancé, puis TwinPanel fermé
        store.Add(saved);
        var back = Restart(dir, store);
        fs.Nodes["/b/film.mkv"] = new byte[1000]; // pendant ce temps, le serveur finit seul
        fs.Dates["/b/film.mkv"] = Original;

        var rest = await RemoteTransfer.CopyOnServerAsync(fs, back.Items, "/b", null, default, TimeSpan.FromHours(1), back.Commands);

        Assert.Empty(rest);
        Assert.Equal(0, fs.ServerCopies);
    }

    [Fact]
    public void Damaged_file_gives_an_empty_list()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.GetDirectoryName(QueueFile(dir))!);
        File.WriteAllText(QueueFile(dir), "{ pas du json");

        using var store = Store(dir);

        Assert.Empty(store.Pending);
    }

    [Fact]
    public void File_goes_away_with_the_last_transfer()
    {
        using var dir = new TempDir();
        using var store = Store(dir);
        var saved = new SavedTransfer();
        store.Add(saved);
        store.Save();
        Assert.True(File.Exists(QueueFile(dir)));

        store.Remove(saved);
        store.Save();

        Assert.False(File.Exists(QueueFile(dir)));
    }

    [Fact]
    public void Transfers_with_a_job_are_not_offered_again()
    {
        using var dir = new TempDir();
        using var store = Store(dir);
        store.Add(new SavedTransfer { Title = "lancé", HasJob = true });
        store.Add(new SavedTransfer { Title = "de côté" });

        Assert.Equal("de côté", Assert.Single(store.Pending).Title);
        Assert.Equal(2, store.Items.Count);
    }

    [Fact]
    public void A_second_window_neither_offers_nor_writes_anything()
    {
        using var dir = new TempDir();
        using var first = Store(dir);
        first.Add(new SavedTransfer { Title = "en cours" });
        first.Save();
        var written = File.ReadAllText(QueueFile(dir));

        using var second = Store(dir);
        second.Add(new SavedTransfer { Title = "autre" });
        second.Save();

        Assert.False(second.IsOwner);
        Assert.Empty(second.Items);
        Assert.Equal(written, File.ReadAllText(QueueFile(dir)));
    }

    [Fact]
    public async Task Saving_while_the_lists_change_loses_nothing_and_never_fails()
    {
        using var dir = new TempDir();
        var plan = new TransferPlan();
        var commands = new ServerCommandState();
        var store = Store(dir);
        store.Add(new SavedTransfer { Plan = plan, Commands = commands });
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var writer = Task.Run(() =>
        {
            for (int i = 0; !stop.IsCancellationRequested; i++)
            {
                plan.Broken.Add(i);
                commands.Finished[$"/f{i}"] = i;
                commands.Started.Add($"/f{i}");
                plan.Skipped.Add("x");
            }
        });
        while (!stop.IsCancellationRequested) store.Save();
        await writer;

        var back = Restart(dir, store);
        Assert.Equal(plan.Broken.Count, back.Plan!.Broken.Count);
        Assert.Equal(commands.Finished.Count, back.Commands!.Finished.Count);
    }

    [Fact]
    public void Nothing_kept_can_hold_a_secret()
    {
        var forbidden = new[] { "password", "passphrase", "secret", "credential", "user", "login", "token", "key" };
        var names = new[] { typeof(SavedTransfer), typeof(TransferPlan), typeof(TransferPlanFile), typeof(TransferPlanDir), typeof(ServerCommandState), typeof(RemoteItem) }
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)).Select(p => p.Name.ToLowerInvariant());

        Assert.DoesNotContain(names, n => forbidden.Any(n.Contains));
    }

    [Fact]
    public async Task Forgetting_removes_the_pieces_on_the_pc_and_on_a_connected_server()
    {
        using var dir = new TempDir();
        var part = Path.Combine(dir.Path, "a.bin" + RemoteTransfer.PartSuffix);
        File.WriteAllBytes(part, [1, 2, 3]);
        var download = new TransferPlan { Files = [new TransferPlanFile { Target = Path.Combine(dir.Path, "a.bin"), Started = true }] };
        await new TransferResume { Plan = download }.DiscardAsync();
        Assert.False(File.Exists(part));

        var server = new MemoryFileSystem();
        var remotePart = "/a.bin" + RemoteTransfer.PartSuffix;
        server.Nodes[remotePart] = [1, 2, 3];
        var upload = new TransferPlan { IsUpload = true, Files = [new TransferPlanFile { Target = "/a.bin", Started = true }] };

        await new TransferResume { Plan = upload }.DiscardAsync(); // pas de connexion : rien à faire, et pas d'erreur
        Assert.True(server.Nodes.ContainsKey(remotePart));

        await new TransferResume { Plan = upload }.DiscardAsync(_ => Task.FromResult<IRemoteFileSystem>(server));
        Assert.False(server.Nodes.ContainsKey(remotePart));
    }
}
