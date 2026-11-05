// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

/// <summary>Une configuration qui passe d'une machine à une autre : deux coffres, verrouillés chacun à sa façon.</summary>
public sealed class ConfigSyncTests : IDisposable
{
    private const string Phrase = "une longue phrase secrète";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tp-sync-" + Guid.NewGuid().ToString("N"));
    private readonly string _folder;
    private readonly ConnectionStore _windows;
    private readonly ConnectionStore _linux;

    public ConfigSyncTests()
    {
        _folder = Path.Combine(_dir, "partage");
        Directory.CreateDirectory(_folder);
        _windows = new ConnectionStore(Path.Combine(_dir, "windows"), new Mask(0x5A));
        _linux = new ConnectionStore(Path.Combine(_dir, "linux"), new Mask(0xA5));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Protection propre à une machine : ce qu'un coffre écrit, l'autre ne sait pas le lire.</summary>
    private sealed class Mask(byte value) : IUserDataProtector
    {
        public bool IsAvailable => true;
        public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ value)).ToArray();
        public byte[] Unprotect(byte[] data) => Protect(data);
    }

    private static ConnectionProfile Server(string name = "maison", string host = "192.0.2.10") =>
        new() { Name = name, Host = host, UserName = "demo", SavePassword = true };

    private ConnectionProfile SavedServer()
    {
        var p = Server();
        p.LocalDirectory = @"C:\Films";
        _windows.SaveProfile(p);
        _windows.Vault.Set(SecretIds.Password(p.Id), "mot-de-passe-du-serveur");
        return p;
    }

    private SyncPackage Send(ConnectionStore from, AppSettings? settings = null)
    {
        var package = ConfigSync.Build(settings ?? new AppSettings(), from);
        ConfigSync.Write(_folder, Phrase, package);
        return package;
    }

    [Fact]
    public void A_secret_terminal_command_travels_with_the_settings_only_inside_the_encrypted_file()
    {
        var settings = new AppSettings();
        var command = new SavedTerminalCommand { Name = "mot de passe" };
        TwinPanel.Core.Terminal.TerminalCommandSecrets.Store(command, "texte-secret-de-test", secret: true, _windows.Vault);
        settings.Terminal.SavedCommands.Add(command);

        Send(_windows, settings);
        Assert.DoesNotContain("texte-secret-de-test", File.ReadAllText(ConfigSync.FilePath(_folder)));
        var received = ConfigSync.Read(_folder, Phrase);
        var here = new AppSettings();
        received.Settings.ApplyTo(here);
        Assert.Equal(1, ConfigSync.ApplyCommandSecrets(received, _linux));

        var arrived = Assert.Single(here.Terminal.SavedCommands);
        Assert.True(arrived.Secret);
        Assert.Equal(string.Empty, arrived.Command);
        Assert.Equal("texte-secret-de-test", TwinPanel.Core.Terminal.TerminalCommandSecrets.Text(arrived, _linux.Vault));
    }

    [Fact]
    public void A_secret_command_needs_the_vault_to_be_sent()
    {
        var settings = new AppSettings();
        Assert.False(ConfigSync.NeedsVault(settings, TransferParts.Settings));
        settings.Terminal.SavedCommands.Add(new SavedTerminalCommand { Name = "x", Secret = true });
        Assert.True(ConfigSync.NeedsVault(settings, TransferParts.Settings));
        Assert.False(ConfigSync.NeedsVault(settings, TransferParts.Tabs));
        Assert.True(ConfigSync.NeedsVault(new AppSettings(), TransferParts.Connections));
    }

    [Fact]
    public void A_password_saved_on_one_machine_is_readable_on_the_other()
    {
        var p = SavedServer();
        Send(_windows);

        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.Equal("mot-de-passe-du-serveur", _linux.Vault.Get(SecretIds.Password(p.Id)));
        Assert.Equal("maison", _linux.FindProfile(p.Id)?.Name);
    }

    [Fact]
    public void A_private_key_travels_and_its_connection_keeps_it()
    {
        var key = new KeychainKey { Name = "cle", KeyType = "ssh-ed25519", Fingerprint = "SHA256:demo", ImportedFrom = @"C:\Users\demo\id" };
        _windows.SaveKey(key);
        _windows.Vault.Set(SecretIds.KeyMaterial(key.Id), "contenu de la clé");
        var p = Server();
        p.Auth = AuthMode.Key;
        p.KeyId = key.Id;
        _windows.SaveProfile(p);
        Send(_windows);

        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.Equal(key.Id, _linux.FindProfile(p.Id)?.KeyId);
        Assert.Equal("contenu de la clé", _linux.Vault.Get(SecretIds.KeyMaterial(key.Id)));
        Assert.Empty(_linux.FindKey(key.Id)!.ImportedFrom);
    }

    [Fact]
    public void Nothing_is_readable_in_the_file()
    {
        SavedServer();
        Send(_windows);

        var text = File.ReadAllText(ConfigSync.FilePath(_folder));

        Assert.DoesNotContain("mot-de-passe", text);
        Assert.DoesNotContain("192.0.2.10", text);
        Assert.DoesNotContain("maison", text);
    }

    [Fact]
    public void A_wrong_passphrase_is_refused()
    {
        SavedServer();
        Send(_windows);

        Assert.Throws<SyncPassphraseException>(() => ConfigSync.Read(_folder, "une autre phrase secrète"));
    }

    [Fact]
    public void A_damaged_file_is_refused()
    {
        Send(_windows);
        var file = ConfigSync.FilePath(_folder);
        // Un octet changé dans les données décodées : changer un caractère du texte pouvait tomber dans « + »
        // (le « + » de la base64 échappé en JSON) et casser le JSON au lieu des données (échec du test, une fois sur 20 environ).
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        var data = Convert.FromBase64String((string)json["Data"]!);
        data[12] ^= 0x01;
        json["Data"] = Convert.ToBase64String(data);
        File.WriteAllText(file, json.ToJsonString());

        Assert.Throws<SyncPassphraseException>(() => ConfigSync.Read(_folder, Phrase));
    }

    [Fact]
    public void Another_file_is_not_taken_for_a_sync_file()
    {
        File.WriteAllText(ConfigSync.FilePath(_folder), "{\"Format\":\"autre\"}");

        Assert.Throws<InvalidDataException>(() => ConfigSync.Read(_folder, Phrase));
    }

    [Fact]
    public void A_file_from_a_newer_version_is_refused()
    {
        var package = ConfigSync.Build(new AppSettings(), _windows);
        package.Version = SyncPackage.CurrentVersion + 1;
        ConfigSync.Write(_folder, Phrase, package);

        Assert.Throws<InvalidDataException>(() => ConfigSync.Read(_folder, Phrase));
    }

    [Fact]
    public void Local_paths_stay_on_their_machine()
    {
        var p = SavedServer();
        var package = Send(_windows);
        Assert.Empty(package.Connections.Profiles.Single().LocalDirectory);

        var linuxSide = _linux.FindProfile(p.Id) ?? p.Clone();
        linuxSide.LocalDirectory = "/home/demo/films";
        _linux.SaveProfile(linuxSide);
        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.Equal("/home/demo/films", _linux.FindProfile(p.Id)?.LocalDirectory);
        Assert.Equal(@"C:\Films", _windows.FindProfile(p.Id)?.LocalDirectory);
    }

    [Fact]
    public void Connections_found_only_here_are_kept()
    {
        SavedServer();
        Send(_windows);
        var here = Server("seulement ici", "192.0.2.20");
        _linux.SaveProfile(here);

        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.NotNull(_linux.FindProfile(here.Id));
        Assert.Equal(2, _linux.Profiles.Count);
    }

    [Fact]
    public void The_same_server_created_on_both_sides_does_not_appear_twice()
    {
        var p = SavedServer();
        var jump = Server("autre", "192.0.2.30");
        var twin = Server();
        _linux.SaveProfile(twin);
        jump.JumpHostId = twin.Id;
        _linux.SaveProfile(jump);
        Send(_windows);

        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.Single(_linux.Profiles, x => x.Name == "maison");
        Assert.Equal(p.Id, _linux.FindProfile(jump.Id)?.JumpHostId);
    }

    [Fact]
    public void A_received_name_never_collides_with_another_server()
    {
        SavedServer();
        Send(_windows);
        _linux.SaveProfile(Server("maison", "192.0.2.40"));

        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.Equal(["maison", "maison (2)"], _linux.Profiles.Select(p => p.Name).Order());
    }

    [Fact]
    public void A_password_no_longer_saved_leaves_the_other_vault_too()
    {
        var p = SavedServer();
        Send(_windows);
        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);
        var changed = _windows.FindProfile(p.Id)!.Clone();
        changed.SavePassword = false;
        _windows.SaveProfile(changed);
        Send(_windows);

        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.Null(_linux.Vault.Get(SecretIds.Password(p.Id)));
    }

    [Fact]
    public void A_received_fingerprint_never_replaces_the_one_known_here()
    {
        byte[] mine = [1, 2, 3], theirs = [4, 5, 6];
        _windows.KnownHosts.Trust("192.0.2.10", 22, "ssh-ed25519", theirs);
        _windows.KnownHosts.Trust("192.0.2.11", 22, "ssh-ed25519", theirs);
        _linux.KnownHosts.Trust("192.0.2.10", 22, "ssh-ed25519", mine);
        Send(_windows);

        ConfigSync.ApplyConnections(ConfigSync.Read(_folder, Phrase), _linux);

        Assert.Equal(HostKeyStatus.Trusted, _linux.KnownHosts.Check("192.0.2.10", 22, "ssh-ed25519", mine));
        Assert.Equal(HostKeyStatus.Changed, _linux.KnownHosts.Check("192.0.2.10", 22, "ssh-ed25519", theirs));
        Assert.Equal(HostKeyStatus.Trusted, _linux.KnownHosts.Check("192.0.2.11", 22, "ssh-ed25519", theirs));
    }

    [Fact]
    public void A_locked_vault_refuses_to_send()
    {
        var p = SavedServer();
        _windows.Vault.SetMasterPassword("mot de passe maître");
        _windows.Vault.Lock();

        Assert.Throws<VaultLockedException>(() => ConfigSync.Build(new AppSettings(), _windows));
        Assert.False(ConfigSync.Exists(_folder));
    }

    [Fact]
    public void Shared_settings_travel_and_local_ones_stay()
    {
        var sent = new AppSettings { Theme = "Dark", Language = "en", EditorPath = @"C:\Editeur\edit.exe" };
        sent.Shortcuts["Copy"] = "Ctrl+K";
        sent.Appearance.CustomDriveColors = true;
        sent.Appearance.DriveColors["C:"] = "#112233";
        sent.Startup.LeftPath = @"C:\Films";
        sent.Terminal.SavedCommands.Add(new SavedTerminalCommand { Name = "voir", Command = "ls" });
        Send(_windows, sent);
        var here = new AppSettings { EditorPath = "/usr/bin/kate" };
        here.Startup.LeftPath = "/home/demo";
        here.Terminal.IntegratedShellPath = "/bin/zsh";
        here.Terminal.SavedCommands.Add(new SavedTerminalCommand { Name = "ici", Command = "df" });

        ConfigSync.Read(_folder, Phrase).Settings.ApplyTo(here);

        Assert.Equal(("Dark", "en", "Ctrl+K"), (here.Theme, here.Language, here.Shortcuts["Copy"]));
        Assert.True(here.Appearance.CustomDriveColors);
        Assert.Empty(here.Appearance.DriveColors);
        Assert.Equal("/usr/bin/kate", here.EditorPath);
        Assert.Equal("/home/demo", here.Startup.LeftPath);
        Assert.Equal("/bin/zsh", here.Terminal.IntegratedShellPath);
        Assert.Equal(["voir", "ici"], here.Terminal.SavedCommands.Select(c => c.Name));
    }

    [Fact]
    public void The_local_state_holds_no_passphrase_and_survives_a_restart()
    {
        var directory = Path.Combine(_dir, "etat");
        var revision = Guid.NewGuid();

        new SyncState { Folder = _folder, LastRevision = revision }.Save(directory);

        var state = SyncState.Load(directory);
        Assert.Equal((_folder, revision), (state.Folder, state.LastRevision));
        Assert.Empty(SyncState.Load(Path.Combine(_dir, "absent")).Folder);
    }

    [Fact]
    public void Only_the_chosen_parts_travel()
    {
        var p = SavedServer();
        var settings = new AppSettings { Theme = "Dark", Left = new PanelState { Tabs = [SavedRemoteTab.Encode(p.Id, "/srv")] } };
        var package = ConfigSync.Build(settings, _windows, TransferParts.Tabs);
        ConfigSync.Write(_folder, Phrase, package);

        var back = ConfigSync.Read(_folder, Phrase);
        Assert.Equal(TransferParts.Tabs, back.Parts);
        Assert.Empty(back.Connections.Profiles);
        Assert.Empty(back.Secrets);
        Assert.Equal(1, back.Tabs!.Count);
        Assert.Equal("maison", Assert.Single(back.Tabs.Servers).Name);
        Assert.Equal(0, ConfigSync.ApplyConnections(back, _linux));
        Assert.Empty(_linux.Profiles);
    }

    [Fact]
    public void A_part_unticked_on_arrival_is_not_applied()
    {
        SavedServer();
        Send(_windows);
        var back = ConfigSync.Read(_folder, Phrase);
        Assert.Equal(TransferParts.Settings | TransferParts.Connections | TransferParts.KnownHosts, back.Parts); // aucun onglet ouvert

        Assert.Equal(0, ConfigSync.ApplyConnections(back, _linux, TransferParts.KnownHosts));
        Assert.Empty(_linux.Profiles);
        Assert.Equal(1, ConfigSync.ApplyConnections(back, _linux, TransferParts.Connections));
    }

    [Fact]
    public void A_file_sent_before_the_choice_contains_everything()
    {
        var package = System.Text.Json.JsonSerializer.Deserialize<SyncPackage>("{\"Version\":1}")!;
        Assert.Equal(TransferParts.Settings | TransferParts.Connections | TransferParts.KnownHosts, package.Parts);
        Assert.Null(package.Tabs);
    }
}
