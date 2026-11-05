// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Operations;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Tests;

/// <summary>Coupure pendant « cp » ou « mv » faits par le serveur : rien ne doit repartir de zéro par le PC.</summary>
public class ServerCommandCutTests
{
    private static readonly DateTime Original = new(2026, 1, 1, 12, 0, 0);

    private static ServerCommandState State() => new() { Settle = TimeSpan.FromMilliseconds(40), CheckEvery = TimeSpan.FromMilliseconds(5) };

    private static MemoryFileSystem Server()
    {
        var fs = new MemoryFileSystem { HasServerMove = true };
        fs.AddDir("/a");
        fs.AddDir("/b");
        fs.Nodes["/a/film.mkv"] = new byte[1000];
        fs.Dates["/a/film.mkv"] = Original;
        return fs;
    }

    private static async Task<IReadOnlyList<RemoteItem>> Copy(MemoryFileSystem fs, ServerCommandState state, CancellationToken ct = default)
    {
        IReadOnlyList<RemoteItem> rest = [];
        var items = await fs.ListAsync("/a", default);
        await new TransferResume { MaxRetries = 3, RetryDelay = _ => TimeSpan.Zero }.RunAsync(_ => Task.FromResult<IRemoteFileSystem>(fs),
            async f => rest = await RemoteTransfer.CopyOnServerAsync(f, items, "/b", null, ct, TimeSpan.FromHours(1), state), null, ct);
        return rest;
    }

    private static async Task<List<RemoteItem>> Move(MemoryFileSystem fs, ServerCommandState state)
    {
        var copyInstead = new List<RemoteItem>();
        var items = await fs.ListAsync("/a", default);
        await new TransferResume { MaxRetries = 3, RetryDelay = _ => TimeSpan.Zero }.RunAsync(_ => Task.FromResult<IRemoteFileSystem>(fs), f =>
        {
            copyInstead.Clear();
            return RemoteTransfer.RenameIntoAsync(f, items, "/b", ConflictPolicy.Overwrite, null, copyInstead, default, TimeSpan.FromHours(1), state);
        }, null, default);
        return copyInstead;
    }

    [Fact]
    public async Task Copy_the_server_finished_alone_is_not_started_again()
    {
        var fs = Server();
        fs.CutServerCommand = () =>
        {
            fs.Nodes["/b/film.mkv"] = new byte[1000];
            fs.Dates["/b/film.mkv"] = Original; // « cp -a » pose la date d'origine tout à la fin
        };

        var rest = await Copy(fs, State());

        Assert.Empty(rest); // rien ne passe par le PC
        Assert.Equal(0, fs.ServerCopies);
        Assert.Equal(1000, fs.Nodes["/b/film.mkv"]!.Length);
    }

    [Fact]
    public async Task Copy_still_running_on_the_server_is_waited_for()
    {
        var fs = Server();
        int size = 100;
        fs.CutServerCommand = () => fs.Nodes["/b/film.mkv"] = new byte[size];
        fs.OnStat = () =>
        {
            if (!fs.Nodes.ContainsKey("/b/film.mkv") || size >= 1000) return;
            size += 100;
            fs.Nodes["/b/film.mkv"] = new byte[size];
            if (size >= 1000) fs.Dates["/b/film.mkv"] = Original;
        };

        var rest = await Copy(fs, State());

        Assert.Empty(rest);
        Assert.Equal(0, fs.ServerCopies);
        Assert.Equal(1000, fs.Nodes["/b/film.mkv"]!.Length);
    }

    [Fact]
    public async Task Copy_stopped_by_the_cut_starts_again_on_the_server()
    {
        var fs = Server();
        fs.CutServerCommand = () =>
        {
            fs.Nodes["/b/film.mkv"] = new byte[300]; // « cp » arrêté à 300 octets
            fs.Dates["/b/film.mkv"] = DateTime.Now;
        };

        var rest = await Copy(fs, State());

        Assert.Empty(rest);
        Assert.Equal(1, fs.ServerCopies);
        Assert.Equal(1000, fs.Nodes["/b/film.mkv"]!.Length);
    }

    [Fact]
    public async Task Same_size_with_another_date_is_not_taken_for_finished()
    {
        var fs = Server();
        fs.CutServerCommand = () =>
        {
            fs.Nodes["/b/film.mkv"] = new byte[1000];
            fs.Dates["/b/film.mkv"] = DateTime.Now; // tout est écrit, sauf la date : « cp » n'a pas fini
        };

        await Copy(fs, State());

        Assert.Equal(1, fs.ServerCopies);
        Assert.Equal(Original, fs.Dates["/b/film.mkv"]);
    }

    [Fact]
    public async Task File_already_at_the_destination_is_never_removed()
    {
        var fs = Server();
        fs.Nodes["/b/film.mkv"] = [1, 2, 3];
        fs.Dates["/b/film.mkv"] = DateTime.Now;
        fs.CutServerCommand = () => throw new InvalidOperationException("aucune commande ne doit partir");

        var rest = await Copy(fs, State());

        Assert.Single(rest); // conflit : il suit la conduite choisie
        Assert.Equal([1, 2, 3], fs.Nodes["/b/film.mkv"]);
    }

    [Fact]
    public async Task Cancelling_while_waiting_removes_our_target()
    {
        var fs = Server();
        using var cts = new CancellationTokenSource();
        fs.CutServerCommand = () => fs.Nodes["/b/film.mkv"] = new byte[300];
        int stats = 0;
        fs.OnStat = () => { if (fs.Nodes.ContainsKey("/b/film.mkv") && ++stats == 2) cts.Cancel(); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Copy(fs, State(), cts.Token));

        Assert.False(fs.Nodes.ContainsKey("/b/film.mkv"));
        Assert.True(fs.Nodes.ContainsKey("/a/film.mkv"));
    }

    [Fact]
    public async Task Move_the_server_finished_alone_is_kept()
    {
        var fs = Server();
        fs.RefuseRename = true;
        fs.CutServerCommand = () =>
        {
            fs.Nodes["/b/film.mkv"] = fs.Nodes["/a/film.mkv"];
            fs.Nodes.Remove("/a/film.mkv");
        };

        var copyInstead = await Move(fs, State());

        Assert.Empty(copyInstead);
        Assert.Equal(1000, fs.Nodes["/b/film.mkv"]!.Length);
        Assert.False(fs.Nodes.ContainsKey("/a/film.mkv"));
    }

    [Fact]
    public async Task Move_stopped_by_the_cut_keeps_the_original_and_starts_again()
    {
        var fs = Server();
        fs.RefuseRename = true;
        fs.CutServerCommand = () => fs.Nodes["/b/film.mkv"] = new byte[300];

        var copyInstead = await Move(fs, State());

        Assert.Empty(copyInstead);
        Assert.Equal(1000, fs.Nodes["/b/film.mkv"]!.Length);
        Assert.False(fs.Nodes.ContainsKey("/a/film.mkv"));
    }

    [Fact]
    public async Task Cut_folder_move_touches_nothing()
    {
        var fs = new MemoryFileSystem { HasServerMove = true, RefuseRename = true };
        fs.AddDir("/a");
        fs.AddDir("/a/photos");
        fs.AddDir("/b");
        fs.Nodes["/a/photos/1.jpg"] = new byte[10];
        fs.CutServerCommand = () => { fs.AddDir("/b/photos"); fs.Nodes["/b/photos/1.jpg"] = new byte[5]; };

        var error = await Assert.ThrowsAsync<RemoteException>(() => Move(fs, State()));

        Assert.Contains("Rien n'a été retiré", error.Message);
        Assert.True(fs.Nodes.ContainsKey("/a/photos/1.jpg"));
        Assert.True(fs.Nodes.ContainsKey("/b/photos/1.jpg"));
    }

    [Fact]
    public void A_cut_command_counts_as_a_lost_connection()
    {
        var failure = ConnectionFailure.DuringCommand("la copie", new InvalidOperationException("x"));
        Assert.True(ConnectionFailure.IsLost(failure, new MemoryFileSystem()));
        Assert.Contains("vérifie la destination", failure.Message);
    }
}
