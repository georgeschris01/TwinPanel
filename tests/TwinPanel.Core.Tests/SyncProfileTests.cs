// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Compare;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

public class SyncProfileTests
{
    private static string FileIn(TempDir dir) => Path.Combine(dir.Path, "config", "sync-profiles.json");

    private static SyncProfileStore Reload(TempDir dir)
    {
        var store = new SyncProfileStore(FileIn(dir));
        store.Load();
        return store;
    }

    [Fact]
    public void Every_setting_comes_back()
    {
        using var dir = new TempDir();
        var server = SavedRemoteTab.Encode(Guid.NewGuid(), "/srv/photos");
        var profile = new SyncProfile
        {
            Name = "Sauvegarde photos",
            Left = @"D:\Photos",
            Right = server,
            NameMask = "*.jpg",
            Recursive = false,
            CompareContent = true,
            IgnoreDate = true,
            Direction = SyncDirection.ToRight,
        };

        new SyncProfileStore(FileIn(dir)).Put(profile);

        Assert.Equal(profile, Assert.Single(Reload(dir).Items));
    }

    [Fact]
    public void A_server_folder_keeps_only_the_connection_number()
    {
        using var dir = new TempDir();
        var id = Guid.NewGuid();

        new SyncProfileStore(FileIn(dir)).Put(new SyncProfile { Name = "x", Left = SavedRemoteTab.Encode(id, "/a") });

        Assert.True(SavedRemoteTab.TryDecode(Reload(dir).Items[0].Left, out var back, out var path));
        Assert.Equal((id, "/a"), (back, path));
    }

    [Fact]
    public void The_same_name_replaces_the_profile()
    {
        using var dir = new TempDir();
        var store = new SyncProfileStore(FileIn(dir));
        store.Put(new SyncProfile { Name = "Photos", Left = "a" });

        store.Put(new SyncProfile { Name = "PHOTOS", Left = "b" });

        Assert.Equal("b", Assert.Single(Reload(dir).Items).Left);
    }

    [Fact]
    public void Profiles_are_sorted_by_name_and_found_without_case()
    {
        using var dir = new TempDir();
        var store = new SyncProfileStore(FileIn(dir));
        store.Put(new SyncProfile { Name = "Zèbre" });
        store.Put(new SyncProfile { Name = "abricot" });

        Assert.Equal(["abricot", "Zèbre"], Reload(dir).Items.Select(p => p.Name));
        Assert.NotNull(store.Find("ZÈBRE"));
        Assert.Null(store.Find("autre"));
    }

    [Fact]
    public void The_file_goes_away_with_the_last_profile()
    {
        using var dir = new TempDir();
        var store = new SyncProfileStore(FileIn(dir));
        store.Put(new SyncProfile { Name = "a" });
        store.Put(new SyncProfile { Name = "b" });

        store.Remove("A");
        Assert.Equal("b", Assert.Single(Reload(dir).Items).Name);

        store.Remove("b");
        Assert.False(File.Exists(FileIn(dir)));
    }

    [Fact]
    public void A_damaged_file_gives_an_empty_list()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.GetDirectoryName(FileIn(dir))!);
        File.WriteAllText(FileIn(dir), "[ pas du json");

        Assert.Empty(Reload(dir).Items);
    }

    [Fact]
    public void Nothing_kept_can_hold_a_secret()
    {
        var forbidden = new[] { "password", "passphrase", "secret", "credential", "user", "login", "token", "key" };

        var names = typeof(SyncProfile).GetProperties().Select(p => p.Name.ToLowerInvariant());

        Assert.DoesNotContain(names, n => forbidden.Any(n.Contains));
    }
}
