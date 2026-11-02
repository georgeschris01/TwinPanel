// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using TwinPanel.Core.Rights;

namespace TwinPanel.Core.Tests;

public class UnixModeTests
{
    [Theory]
    [InlineData("755", 0x1ED, "rwxr-xr-x")]
    [InlineData("644", 0x1A4, "rw-r--r--")]
    [InlineData("4755", 0x9ED, "rwsr-xr-x")]
    [InlineData("2750", 0x5E8, "rwxr-s---")]
    [InlineData("1777", 0x3FF, "rwxrwxrwt")]
    [InlineData("1666", 0x3B6, "rw-rw-rwT")]
    [InlineData("4644", 0x9A4, "rwSr--r--")]
    public void Octal_and_symbolic_round_trip(string octal, int mode, string symbolic)
    {
        Assert.True(UnixMode.TryParseOctal(octal, out var m));
        Assert.Equal(mode, m);
        Assert.Equal(symbolic, UnixMode.ToSymbolic(mode));
        Assert.True(UnixMode.TryParseSymbolic(symbolic, out var back));
        Assert.Equal(mode, back);
        Assert.Equal(octal, UnixMode.ToOctal(mode));
        Assert.Equal(int.Parse(octal), UnixMode.ToDigits(mode));
        Assert.Equal(mode, UnixMode.FromDigits(int.Parse(octal)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("75a")]
    [InlineData("77777")]
    public void Bad_octal_is_refused(string text) => Assert.False(UnixMode.TryParseOctal(text, out _));

    [Fact]
    public void Ls_line_with_type_is_read() =>
        Assert.True(UnixMode.TryParseSymbolic("drwxr-x---", out var m) && m == 0x1E8);

    [Fact]
    public void Bit_positions()
    {
        Assert.Equal(0x100, UnixMode.Bit(0, 0)); // propriétaire lire
        Assert.Equal(0x010, UnixMode.Bit(1, 1)); // groupe écrire
        Assert.Equal(0x001, UnixMode.Bit(2, 2)); // autres exécuter
    }

    [Fact]
    public void Partial_change_keeps_other_bits()
    {
        // « + exécuter pour le groupe, - écrire pour les autres » sur 0646
        var change = new ModeChange(UnixMode.Bit(1, 2), UnixMode.Bit(2, 1));
        Assert.Equal(0x1AC, change.Apply(0x1A6)); // 0654
        Assert.False(change.IsAbsolute);
        Assert.True(ModeChange.Absolute(0x1ED).IsAbsolute);
        Assert.Equal(0x1ED, ModeChange.Absolute(0x1ED).Apply(0xFFF));
    }

    [Fact]
    public void Common_bit_of_several_items()
    {
        Assert.True(RightsApplier.Common([0x1ED, 0x1A4], 0x100));
        Assert.False(RightsApplier.Common([0x1A4, 0x124], 0x001));
        Assert.Null(RightsApplier.Common([0x1ED, 0x1A4], 0x040));
    }
}

public class UnixAccountsTests
{
    private const string Passwd = "root:x:0:0:root:/root:/bin/bash\n# commentaire\n\nchris:x:1000:1000:Utilisateur:/home/chris:/bin/bash\nwww-data:x:33:33::/var/www:/usr/sbin/nologin\nabimé\n";
    private const string Group = "root:x:0:\nwww-data:x:33:chris\nchris:x:1000:\n";

    [Fact]
    public void Passwd_and_group_are_read()
    {
        var a = UnixAccounts.Parse(Passwd, Group);
        Assert.Equal(new[] { "chris", "root", "www-data" }, a.Users.Select(u => u.Name));
        Assert.Equal("chris", a.UserName(1000));
        Assert.Equal("www-data", a.GroupName(33));
        Assert.Null(a.UserName(4242));
    }

    [Theory]
    [InlineData("chris", 1000)]
    [InlineData("chris (1000)", 1000)]
    [InlineData("33", 33)]
    [InlineData("4242", 4242)]
    public void Typed_user_is_found(string text, int id) => Assert.Equal(id, UnixAccounts.Parse(Passwd, Group).FindUser(text)?.Id);

    [Fact]
    public void Unknown_name_is_not_invented() => Assert.Null(UnixAccounts.Parse(Passwd, Group).FindUser("personne"));
}

public class UnixCommandsTests
{
    [Fact]
    public void Paths_are_quoted_and_ids_used()
    {
        Assert.Equal("chmod 755 -- '/tmp/l'\\''arbre'", UnixCommands.ChmodCommand("/tmp/l'arbre", 0x1ED));
        Assert.Equal("chmod 4755 -- '/a b'", UnixCommands.ChmodCommand("/a b", 0x9ED));
        Assert.Equal("chown 1000:33 -- '/x'", UnixCommands.ChownCommand("/x", new UnixAccount(1000, "chris"), new UnixAccount(33, "www-data")));
        Assert.Equal("chown 0 -- '/x'", UnixCommands.ChownCommand("/x", new UnixAccount(0, "root"), null));
        Assert.Equal("chgrp 33 -- '/x'", UnixCommands.ChownCommand("/x", null, new UnixAccount(33, "www-data")));
        Assert.Null(UnixCommands.ChownCommand("/x", null, null));
    }

    [Fact]
    public void Find_output_is_read()
    {
        var output = "f\t1234\t1700000000.5\t1700000100.0\t4755\t1000\t33\tchris\twww-data\t\n";
        var d = UnixCommands.ParseDetails("/srv/app", output)!;
        Assert.False(d.IsDirectory);
        Assert.Equal(1234, d.Size);
        Assert.Equal(0x9ED, d.Mode);
        Assert.Equal(1000, d.UserId);
        Assert.Equal("www-data", d.Group);
        Assert.Equal("chris (1000)", d.OwnerDisplay);
        Assert.Null(d.LinkTarget);
    }

    [Fact]
    public void Link_shows_its_target_and_target_info()
    {
        var output = "l\t7\t1700000000\t1700000000\t777\t0\t0\troot\troot\t/var/www\n" +
                     "d\t4096\t1700000000\t1700000000\t755\t33\t33\twww-data\twww-data\t\n";
        var d = UnixCommands.ParseDetails("/www", output)!;
        Assert.True(d.IsLink);
        Assert.True(d.IsDirectory);
        Assert.Equal("/var/www", d.LinkTarget);
        Assert.Equal(0x1ED, d.Mode);
        Assert.Equal("www-data", d.Owner);
    }

    [Fact]
    public void Refusal_is_explained()
    {
        Assert.Contains("root", UnixCommands.ExplainRefusal("chown: changing ownership of 'x': Operation not permitted", owner: true));
        Assert.Contains("propriétaire", UnixCommands.ExplainRefusal("chmod: Permission denied", owner: false));
        Assert.Equal("autre", UnixCommands.ExplainRefusal("autre", owner: true));
    }
}

public class RightsApplierTests
{
    private static MemoryFileSystem Tree()
    {
        var fs = new MemoryFileSystem();
        fs.AddDir("/www");
        fs.AddDir("/www/img");
        fs.AddFile("/www/index.php", "x");
        fs.AddFile("/www/img/a.png", "yy");
        fs.Modes["/www/index.php"] = 0x180; // 600
        return fs;
    }

    [Fact]
    public async Task Separate_modes_for_files_and_folders_recursively()
    {
        var fs = Tree();
        fs.Modes["/www/img/a.png"] = 0x1FF; // 777
        var top = (await fs.GetDetailsAsync("/www", default))!.ToItem();
        var r = await RightsApplier.ApplyAsync(fs, [top],
            new RightsRequest(ModeChange.Absolute(0x1A4), ModeChange.Absolute(0x1ED), null, null, Recursive: true), null, default);
        Assert.Equal(0x1A4, fs.Modes["/www/index.php"]);
        Assert.Equal(0x1A4, fs.Modes["/www/img/a.png"]);
        Assert.Equal(0x1ED, fs.Modes.GetValueOrDefault("/www/img", 0x1ED));
        Assert.Empty(r.Errors);
        Assert.Equal(4, r.Visited);
    }

    [Fact]
    public async Task Owner_only_for_content_and_errors_do_not_stop()
    {
        var fs = Tree();
        fs.Refused.Add("/www/img");
        var top = (await fs.GetDetailsAsync("/www", default))!.ToItem();
        var change = new ModeChange(UnixMode.Bit(1, 1), 0); // + écrire pour le groupe
        var r = await RightsApplier.ApplyAsync(fs, [top],
            new RightsRequest(change, change, new UnixAccount(33, "www-data"), null, Recursive: true, ModeForContent: false), null, default);
        Assert.Equal(0x1FD, fs.Modes["/www"]);                      // 0775 pour l'élément choisi
        Assert.Equal(0x180, fs.Modes["/www/index.php"]);             // contenu : droits inchangés
        Assert.Equal(33, fs.Owners["/www/img/a.png"].Uid);           // … mais propriétaire changé
        Assert.Empty(r.Errors);                                      // le dossier refusé n'a pas reçu de chmod

        var r2 = await RightsApplier.ApplyAsync(fs, [(await fs.GetDetailsAsync("/www/img", default))!.ToItem()],
            new RightsRequest(change, change, null, null, false), null, default);
        Assert.Single(r2.Errors);
    }

    [Fact]
    public async Task Measure_counts_files_and_folders()
    {
        var fs = Tree();
        var (bytes, files, folders) = await RightsApplier.MeasureAsync(fs, [(await fs.GetDetailsAsync("/www", default))!.ToItem()], null, default);
        Assert.Equal(3, bytes);
        Assert.Equal(2, files);
        Assert.Equal(2, folders);
    }
}

[Collection("Formats globaux")]
public class OwnerColumnTests
{
    [Fact]
    public void Scp_listing_carries_owner_and_group()
    {
        var item = TwinPanel.Core.Remote.ScpFileSystem.ParseFindLine("/srv",
            "f\tf\t12\t1700000000\t4755\t1000\t33\tchris\twww-data\tappli")!;
        Assert.Equal("/srv/appli", item.FullPath);
        Assert.Equal(0x9ED, item.Mode);
        Assert.Equal(1000, item.Uid);
        Assert.Equal(33, item.Gid);
        Assert.Equal("chris", item.Owner);
        Assert.Equal("www-data", item.Group);
    }

    [Fact]
    public void Owner_shown_as_name_or_number()
    {
        var e = new FileEntry("a", "/a", false, 1, DateTime.Now, FileAttributes.Normal) { Mode = 0x1A4, UserId = 1000, GroupId = 33, Owner = "chris", Group = "" };
        var before = FileEntry.OwnerAsNumber;
        try
        {
            FileEntry.OwnerAsNumber = false;
            Assert.Equal("644", e.DisplayMode);
            Assert.Equal("chris", e.DisplayOwner);
            Assert.Equal("33", e.DisplayGroup); // pas de nom connu : le numéro
            FileEntry.OwnerAsNumber = true;
            Assert.Equal("1000", e.DisplayOwner);
            var windows = e with { UserId = -1, Owner = "Utilisateur" };
            Assert.Equal("Utilisateur", windows.DisplayOwner); // pas de numéro (Windows) : le nom
        }
        finally { FileEntry.OwnerAsNumber = before; }
    }

    [Fact]
    public void Sort_by_owner_and_mode()
    {
        FileEntry F(string n, int mode, string owner) => new(n, "/" + n, false, 1, DateTime.Now, FileAttributes.Normal) { Mode = mode, Owner = owner };
        var list = new[] { F("b", 0x1ED, "zoe"), F("a", 0x1A4, "root"), F("c", 0x180, "anne") };
        Assert.Equal(new[] { "c", "a", "b" }, TwinPanel.Core.Navigation.EntrySorter.Sort(list, TwinPanel.Core.Navigation.SortColumn.Owner, false).Select(x => x.Name));
        Assert.Equal(new[] { "c", "a", "b" }, TwinPanel.Core.Navigation.EntrySorter.Sort(list, TwinPanel.Core.Navigation.SortColumn.Mode, false).Select(x => x.Name));
    }
}

public class SyncDirectionTests
{
    private static TwinPanel.Core.Compare.CompareItem Item(TwinPanel.Core.Compare.CompareState s) => new("a.txt", false, null, null, s);

    [Theory]
    [InlineData(TwinPanel.Core.Compare.CompareState.LeftOnly, TwinPanel.Core.Compare.SyncDirection.ToLeft, TwinPanel.Core.Compare.SyncAction.None)]
    [InlineData(TwinPanel.Core.Compare.CompareState.LeftNewer, TwinPanel.Core.Compare.SyncDirection.ToRight, TwinPanel.Core.Compare.SyncAction.CopyToRight)]
    [InlineData(TwinPanel.Core.Compare.CompareState.RightNewer, TwinPanel.Core.Compare.SyncDirection.ToRight, TwinPanel.Core.Compare.SyncAction.None)]
    [InlineData(TwinPanel.Core.Compare.CompareState.RightOnly, TwinPanel.Core.Compare.SyncDirection.ToLeft, TwinPanel.Core.Compare.SyncAction.CopyToLeft)]
    [InlineData(TwinPanel.Core.Compare.CompareState.RightNewer, TwinPanel.Core.Compare.SyncDirection.Both, TwinPanel.Core.Compare.SyncAction.CopyToLeft)]
    [InlineData(TwinPanel.Core.Compare.CompareState.Different, TwinPanel.Core.Compare.SyncDirection.Both, TwinPanel.Core.Compare.SyncAction.None)]
    public void Default_action_follows_direction(TwinPanel.Core.Compare.CompareState state, TwinPanel.Core.Compare.SyncDirection direction, TwinPanel.Core.Compare.SyncAction expected) =>
        Assert.Equal(expected, Item(state).DefaultAction(direction));
}

public class RemoteHelpersTests
{
    [Fact]
    public void Hashing_stream_gives_the_same_sha256_as_a_file()
    {
        var data = System.Text.Encoding.UTF8.GetBytes("bonjour");
        using var hash = TwinPanel.Core.Compare.Checksums.NewHash(TwinPanel.Core.Compare.HashKind.Sha256);
        var s = new TwinPanel.Core.Compare.HashingStream(hash);
        s.Write(data, 0, 3);
        s.Write(data, 3, data.Length - 3);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant(), s.HexHash());
    }

    [Fact]
    public void Checksum_lines_round_trip()
    {
        var line = TwinPanel.Core.Compare.Checksums.FormatLine("abc123", @"photos\a.jpg");
        Assert.Equal("abc123 *photos/a.jpg", line);
        var parsed = TwinPanel.Core.Compare.Checksums.ParseLines(["# commentaire", "", line]).Single();
        Assert.Equal(("abc123", "photos/a.jpg"), parsed);
        Assert.Equal("sous/x.txt", TwinPanel.Core.Remote.RemoteChecksums.Relative("/srv", "/srv/sous/x.txt"));
    }

    [Fact]
    public async Task Multi_rename_on_server_paths_with_rollback()
    {
        var names = new HashSet<string> { "sftp://s/d/a.txt", "sftp://s/d/b.txt" };
        Task Move(string from, string to, CancellationToken ct)
        {
            if (to.EndsWith("/boom.txt", StringComparison.Ordinal)) throw new TwinPanel.Core.Remote.RemoteException("refusé");
            if (!names.Remove(from) || !names.Add(to)) throw new TwinPanel.Core.Remote.RemoteException("conflit");
            return Task.CompletedTask;
        }
        FileEntry E(string n) => new(n, "sftp://s/d/" + n, false, 1, DateTime.Now, FileAttributes.Normal);
        // Échange a ↔ b : possible grâce aux noms temporaires.
        var log = await TwinPanel.Core.Operations.MultiRename.ExecuteAsync(
            [new TwinPanel.Core.Operations.RenamePreview(E("a.txt"), "b.txt", null), new TwinPanel.Core.Operations.RenamePreview(E("b.txt"), "a.txt", null)], Move);
        Assert.Equal(2, log.Items.Count);
        Assert.Equal(new HashSet<string> { "sftp://s/d/a.txt", "sftp://s/d/b.txt" }, names);
        // Erreur : tout est remis en place.
        await Assert.ThrowsAsync<FileOperationException>(() => TwinPanel.Core.Operations.MultiRename.ExecuteAsync(
            [new TwinPanel.Core.Operations.RenamePreview(E("a.txt"), "c.txt", null), new TwinPanel.Core.Operations.RenamePreview(E("b.txt"), "boom.txt", null)], Move));
        Assert.Equal(new HashSet<string> { "sftp://s/d/a.txt", "sftp://s/d/b.txt" }, names);
    }
}
