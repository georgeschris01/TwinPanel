// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

public class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tp-paths-" + Guid.NewGuid().ToString("N"));
    private string Program => Path.Combine(_root, "programme");
    private string Installed => Path.Combine(_root, "appdata", "TwinPanel");

    public AppPathsTests() => Directory.CreateDirectory(Program);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Without_the_marker_the_installed_folder_is_used()
    {
        var layout = AppPaths.Resolve(Program, Installed);
        Assert.False(layout.Portable);
        Assert.Equal(Installed, layout.Data);
        Assert.False(Directory.Exists(Path.Combine(Program, "Data")));
    }

    [Fact]
    public void With_the_marker_settings_go_to_Data_next_to_the_program()
    {
        File.WriteAllText(Path.Combine(Program, AppPaths.PortableMarker), "portable");
        var layout = AppPaths.Resolve(Program, Installed);
        Assert.True(layout.Portable);
        Assert.False(layout.ReadOnlyCopy);
        Assert.Equal(Path.Combine(Program, "Data"), layout.Data);
        Assert.True(Directory.Exists(layout.Data));
        Assert.Empty(Directory.EnumerateFileSystemEntries(layout.Data)); // le test d'écriture ne laisse rien
    }

    [Fact]
    public void Installed_settings_are_offered_once_and_copied_without_the_vault()
    {
        File.WriteAllText(Path.Combine(Program, AppPaths.PortableMarker), "portable");
        Directory.CreateDirectory(Installed);
        foreach (var f in new[] { "settings.json", "history.json", "connections.json", "known_hosts.json", "secrets.dat", "crash.log", "transfers.json" })
            File.WriteAllText(Path.Combine(Installed, f), f);
        var layout = AppPaths.Resolve(Program, Installed);
        Assert.True(AppPaths.CanImportFromInstalled(layout, Installed));

        Assert.Equal(4, AppPaths.ImportFromInstalled(Installed, layout.Data));
        Assert.True(File.Exists(Path.Combine(layout.Data, "settings.json")));
        Assert.True(File.Exists(Path.Combine(layout.Data, "connections.json")));
        Assert.False(File.Exists(Path.Combine(layout.Data, "secrets.dat")));
        Assert.False(File.Exists(Path.Combine(layout.Data, "crash.log")));
        Assert.False(AppPaths.CanImportFromInstalled(layout, Installed)); // plus de question ensuite
    }

    [Fact]
    public void Nothing_is_offered_without_an_installed_version_or_when_not_portable()
    {
        Assert.False(AppPaths.CanImportFromInstalled(AppPaths.Resolve(Program, Installed), Installed));
        File.WriteAllText(Path.Combine(Program, AppPaths.PortableMarker), "portable");
        Assert.False(AppPaths.CanImportFromInstalled(AppPaths.Resolve(Program, Installed), Installed));
    }

    [Fact]
    public void A_read_only_folder_works_on_a_temporary_copy()
    {
        File.WriteAllText(Path.Combine(Program, AppPaths.PortableMarker), "portable");
        // Un fichier à la place du dossier Data : on ne peut pas y écrire, comme sur une clé verrouillée.
        File.WriteAllText(Path.Combine(Program, "Data"), "pas un dossier");
        var temp = Path.Combine(_root, "temp");
        var layout = AppPaths.Resolve(Program, Installed, temp);
        Assert.True(layout.Portable);
        Assert.True(layout.ReadOnlyCopy);
        Assert.StartsWith(temp, layout.Data);
        Assert.True(Directory.Exists(layout.Data));
        Assert.False(AppPaths.CanImportFromInstalled(layout, Installed));
    }

    [Fact]
    public void The_portable_vault_needs_a_master_password()
    {
        var vault = new SecretVault(Path.Combine(_root, "secrets.dat"), new PortableDataProtector());
        Assert.False(vault.CanWorkWithoutMaster);
        Assert.ThrowsAny<InvalidOperationException>(() => vault.Set("x", "secret"));
    }
}
