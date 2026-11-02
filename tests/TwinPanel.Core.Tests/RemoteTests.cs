// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Tests;

/// <summary>Protection de test : remplace DPAPI (inverse les octets, réversible, sans dépendre de Windows).</summary>
internal sealed class FakeProtector(bool available = true) : IUserDataProtector
{
    public bool IsAvailable => available;
    public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).Reverse().ToArray();
    public byte[] Unprotect(byte[] data) => data.Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray();
}

public class SecretVaultTests
{
    [Fact]
    public void Secrets_survive_reload_and_never_appear_in_clear()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "secrets.dat");
        var vault = new SecretVault(path, new FakeProtector());
        vault.Set("pwd:1", "Tr3s-s3cret!");
        Assert.Equal("Tr3s-s3cret!", vault.Get("pwd:1"));
        Assert.DoesNotContain("Tr3s-s3cret!", File.ReadAllText(path));

        var again = new SecretVault(path, new FakeProtector());
        Assert.Equal("Tr3s-s3cret!", again.Get("pwd:1"));
        again.Set("pwd:1", null);
        Assert.Null(new SecretVault(path, new FakeProtector()).Get("pwd:1"));
    }

    [Fact]
    public void Master_password_locks_unlocks_changes_and_removes()
    {
        using var t = new TempDir();
        var path = Path.Combine(t.Path, "secrets.dat");
        var vault = new SecretVault(path, new FakeProtector());
        vault.Set("a", "alpha");
        vault.SetMasterPassword("maître");
        Assert.True(vault.HasMasterPassword);
        Assert.Equal("alpha", vault.Get("a")); // toujours déverrouillé juste après

        var reopened = new SecretVault(path, new FakeProtector());
        Assert.False(reopened.IsUnlocked);
        Assert.Throws<VaultLockedException>(() => reopened.Get("a"));
        Assert.False(reopened.Unlock("mauvais"));
        Assert.True(reopened.Unlock("maître"));
        Assert.Equal("alpha", reopened.Get("a"));

        reopened.SetMasterPassword("nouveau");
        reopened.Lock();
        Assert.False(reopened.Unlock("maître"));
        Assert.True(reopened.Unlock("nouveau"));

        reopened.SetMasterPassword(null);
        var plain = new SecretVault(path, new FakeProtector());
        Assert.True(plain.IsUnlocked);
        Assert.Equal("alpha", plain.Get("a"));
    }

    [Fact]
    public void Without_dpapi_a_master_password_is_required()
    {
        using var t = new TempDir();
        var vault = new SecretVault(Path.Combine(t.Path, "s.dat"), new FakeProtector(available: false));
        Assert.Throws<InvalidOperationException>(() => vault.Set("x", "y"));
        vault.SetMasterPassword("m");
        vault.Set("x", "y");
        Assert.Equal("y", vault.Get("x"));
        Assert.Throws<InvalidOperationException>(() => vault.SetMasterPassword(null));
    }

    [Fact]
    public void Removing_an_owner_removes_all_its_secrets()
    {
        using var t = new TempDir();
        var vault = new SecretVault(Path.Combine(t.Path, "s.dat"), new FakeProtector());
        var id = Guid.NewGuid();
        vault.Set(SecretIds.Password(id), "p");
        vault.Set(SecretIds.ProxyPassword(id), "q");
        vault.Set(SecretIds.Password(Guid.NewGuid()), "autre");
        vault.RemoveAllFor(id);
        Assert.False(vault.Contains(SecretIds.Password(id)));
        Assert.False(vault.Contains(SecretIds.ProxyPassword(id)));
    }
}

public class KnownHostsTests
{
    private static byte[] Blob(string type, byte fill)
    {
        var name = Encoding.ASCII.GetBytes(type);
        var key = Enumerable.Repeat(fill, 32).ToArray();
        var ms = new MemoryStream();
        void Str(byte[] b) { ms.Write([(byte)(b.Length >> 24), (byte)(b.Length >> 16), (byte)(b.Length >> 8), (byte)b.Length]); ms.Write(b); }
        Str(name);
        Str(key);
        return ms.ToArray();
    }

    [Fact]
    public void Unknown_then_trusted_then_changed()
    {
        using var t = new TempDir();
        var kh = new KnownHosts(Path.Combine(t.Path, "kh.json"));
        var key = Blob("ssh-ed25519", 1);
        Assert.Equal(HostKeyStatus.Unknown, kh.Check("srv", 22, "ssh-ed25519", key));
        kh.Trust("srv", 22, "ssh-ed25519", key);
        var reloaded = new KnownHosts(Path.Combine(t.Path, "kh.json"));
        Assert.Equal(HostKeyStatus.Trusted, reloaded.Check("SRV", 22, "ssh-ed25519", key));
        Assert.Equal(HostKeyStatus.Changed, reloaded.Check("srv", 22, "ssh-ed25519", Blob("ssh-ed25519", 2)));
        Assert.Equal(HostKeyStatus.Unknown, reloaded.Check("srv", 2222, "ssh-ed25519", key));
    }

    [Fact]
    public void Fingerprint_uses_openssh_format()
    {
        var key = Blob("ssh-ed25519", 7);
        var expected = "SHA256:" + Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=');
        Assert.Equal(expected, KnownHosts.Sha256Fingerprint(key));
    }

    [Fact]
    public void Reads_openssh_known_hosts_plain_port_and_hashed()
    {
        var key = Blob("ssh-ed25519", 3);
        var b64 = Convert.ToBase64String(key);
        var salt = RandomNumberGenerator.GetBytes(20);
        var hash = HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes("hashed.example"));
        string[] lines =
        [
            "# commentaire",
            $"plain.example,10.0.0.1 ssh-ed25519 {b64}",
            $"[port.example]:2222 ssh-ed25519 {b64}",
            $"|1|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)} ssh-ed25519 {b64}",
            $"other.example ssh-ed25519 {Convert.ToBase64String(Blob("ssh-ed25519", 9))}",
        ];
        Assert.Equal(HostKeyStatus.Trusted, KnownHosts.CheckOpenSshLines(lines, "10.0.0.1", 22, "ssh-ed25519", key));
        Assert.Equal(HostKeyStatus.Trusted, KnownHosts.CheckOpenSshLines(lines, "port.example", 2222, "ssh-ed25519", key));
        Assert.Null(KnownHosts.CheckOpenSshLines(lines, "port.example", 22, "ssh-ed25519", key));
        Assert.Equal(HostKeyStatus.Trusted, KnownHosts.CheckOpenSshLines(lines, "hashed.example", 22, "ssh-ed25519", key));
        Assert.Equal(HostKeyStatus.Changed, KnownHosts.CheckOpenSshLines(lines, "other.example", 22, "ssh-ed25519", key));
        Assert.Equal(HostKeyStatus.Changed, KnownHosts.CheckOpenSshLines([$"@revoked plain.example ssh-ed25519 {b64}"], "plain.example", 22, "ssh-ed25519", key));
    }

    [Fact]
    public void Certificates_are_pinned_per_host()
    {
        using var t = new TempDir();
        var kh = new KnownHosts(Path.Combine(t.Path, "kh.json"));
        kh.TrustCertificate("nas", 443, "abcd", "CN=nas", DateTime.UtcNow.AddYears(1));
        Assert.True(kh.IsCertificateTrusted("NAS", 443, "ABCD"));
        Assert.False(kh.IsCertificateTrusted("nas", 443, "ffff"));
        Assert.False(kh.IsCertificateTrusted("nas", 8443, "abcd"));
    }
}

public class SshKeyTests
{
    [Fact]
    public void Generated_ed25519_key_is_readable_by_ssh_net()
    {
        var text = SshKeys.GenerateEd25519("utilisateur@twinpanel");
        Assert.StartsWith("-----BEGIN OPENSSH PRIVATE KEY-----", text);
        var info = SshKeys.Inspect(text, null, "utilisateur@twinpanel");
        Assert.Equal("ssh-ed25519", info.KeyType);
        Assert.StartsWith("SHA256:", info.Fingerprint);
        Assert.StartsWith("ssh-ed25519 AAAAC3NzaC1lZDI1NTE5", info.PublicKeyLine);
        Assert.EndsWith(" utilisateur@twinpanel", info.PublicKeyLine);
        Assert.False(SshKeys.NeedsPassphrase(text));
        Assert.NotEqual(SshKeys.GenerateEd25519("x"), SshKeys.GenerateEd25519("x"));
    }

    [Fact]
    public void Garbage_is_not_a_key()
    {
        Assert.Throws<InvalidDataException>(() => SshKeys.Inspect("ceci n'est pas une clé", null));
    }
}

public class ImportTests
{
    [Fact]
    public void Ssh_config_hosts_with_keys_ports_and_jump()
    {
        var text = """
            Host *
              ServerAliveInterval 30
            Host chrypox nas
              HostName 192.168.0.10
              User utilisateur
              Port 2222
              IdentityFile ~/.ssh/id_ed25519
            Host interne
              HostName 10.0.0.5
              ProxyJump chrypox
            """;
        var list = ConnectionImport.ParseSshConfig(text);
        Assert.Equal(3, list.Count);
        var c = list[0];
        Assert.Equal("chrypox", c.Profile.Name);
        Assert.Equal("192.168.0.10", c.Profile.Host);
        Assert.Equal(2222, c.Profile.Port);
        Assert.Equal("utilisateur", c.Profile.UserName);
        Assert.Equal(AuthMode.Key, c.Profile.Auth);
        Assert.EndsWith("id_ed25519", c.KeyFile);
        Assert.Equal("nas", list[1].Profile.Name);
        Assert.Equal("chrypox", list[2].JumpHostName);
        Assert.Equal(22, list[2].Profile.Port);
    }

    [Fact]
    public void FileZilla_sites_folders_passwords_and_paths()
    {
        var pass = Convert.ToBase64String(Encoding.UTF8.GetBytes("motdepasse"));
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <FileZilla3><Servers>
              <Folder expanded="1">Maison
                <Server><Host>ftp.example.be</Host><Port>21</Port><Protocol>4</Protocol><Type>0</Type>
                  <User>chris</User><Pass encoding="base64">{pass}</Pass><Logontype>1</Logontype>
                  <Name>Site perso</Name><RemoteDir>1 0 4 home 3 www</RemoteDir></Server>
              </Folder>
              <Server><Host>sftp.example.be</Host><Port>22</Port><Protocol>1</Protocol><User>root</User>
                <Logontype>5</Logontype><Keyfile>C:\cles\id.ppk</Keyfile><Name>Serveur</Name></Server>
            </Servers></FileZilla3>
            """;
        var list = ConnectionImport.ParseFileZilla(xml);
        Assert.Equal(2, list.Count);
        Assert.Equal("Maison", list[0].Profile.Group);
        Assert.Equal(RemoteProtocol.FtpsExplicit, list[0].Profile.Protocol);
        Assert.Equal("motdepasse", list[0].Password);
        Assert.Equal("/home/www", list[0].Profile.RemoteDirectory);
        Assert.Equal(RemoteProtocol.Sftp, list[1].Profile.Protocol);
        Assert.Equal(AuthMode.Key, list[1].Profile.Auth);
        Assert.Equal(@"C:\cles\id.ppk", list[1].KeyFile);
        Assert.Null(list[1].Password);
    }

    [Theory]
    [InlineData("1 0 4 home 8 christop", "/home/christop")]
    [InlineData("1 0 4 mes  9 documents", "/mes /documents")]
    [InlineData("", "")]
    public void FileZilla_path_decoding(string encoded, string expected) =>
        Assert.Equal(expected, ConnectionImport.DecodeFileZillaPath(encoded));

    [Fact]
    public void Total_commander_ftp_and_sftp_files()
    {
        var ftp = """
            [connections]
            1=Hébergeur
            [Hébergeur]
            host=ftp.hebergeur.be:2121
            username=chris
            password=4B3A0C1F
            directory=/www
            pasvmode=1
            """;
        var list = ConnectionImport.ParseTotalCommander(ftp, "wcx_ftp.ini");
        var c = Assert.Single(list);
        Assert.Equal("ftp.hebergeur.be", c.Profile.Host);
        Assert.Equal(2121, c.Profile.Port);
        Assert.Equal(RemoteProtocol.Ftp, c.Profile.Protocol);
        Assert.Equal("/www", c.Profile.RemoteDirectory);
        Assert.Null(c.Password); // jamais le mot de passe chiffré de Total Commander

        var sftp = """
            [chrypox]
            server=192.168.0.20
            user=root
            privkeyfile=C:\cles\root.pem
            """;
        var s = Assert.Single(ConnectionImport.ParseTotalCommander(sftp, "sftpplug.ini"));
        Assert.Equal(RemoteProtocol.Sftp, s.Profile.Protocol);
        Assert.Equal(22, s.Profile.Port);
        Assert.Equal("root", s.Profile.UserName);
        Assert.Equal(AuthMode.Key, s.Profile.Auth);
    }

    [Fact]
    public void WinScp_ini_sessions()
    {
        var ini = """
            [Sessions\Travail/serveur%20web]
            HostName=web.example.be
            UserName=deploy
            FSProtocol=5
            Ftps=3
            RemoteDirectory=/var/www
            [Sessions\Default%20Settings]
            HostName=
            """;
        var c = Assert.Single(ConnectionImport.ParseWinScpIni(ini));
        Assert.Equal("serveur web", c.Profile.Name);
        Assert.Equal("WinSCP/Travail", c.Profile.Group);
        Assert.Equal(RemoteProtocol.FtpsExplicit, c.Profile.Protocol);
        Assert.Equal(21, c.Profile.Port);
        Assert.Equal("/var/www", c.Profile.RemoteDirectory);
    }

    [Theory]
    [InlineData("hote", 21, "hote", 21)]
    [InlineData("hote:2121", 21, "hote", 2121)]
    [InlineData("ftp://user@hote:990/dossier", 21, "hote", 990)]
    [InlineData("[fe80::1]:2222", 22, "fe80::1", 2222)]
    [InlineData("fe80::1", 22, "fe80::1", 22)]
    public void Host_and_port_splitting(string raw, int defaultPort, string host, int port) =>
        Assert.Equal((host, port), ConnectionImport.SplitHostPort(raw, defaultPort));
}

public class RemotePathTests
{
    [Theory]
    [InlineData("sftp://chrypox/home/utilisateur", "sftp", "chrypox", "/home/utilisateur")]
    [InlineData("ftp://site", "ftp", "site", "/")]
    [InlineData("davs://nc/Documents/../Photos/", "davs", "nc", "/Photos")]
    public void Panel_paths_parse(string text, string scheme, string session, string path)
    {
        Assert.True(PanelRemotePath.TryParse(text, out var p));
        Assert.Equal((scheme, session, path), (p.Scheme, p.Session, p.Path));
        Assert.True(PanelRemotePath.TryParse(p.ToString(), out var again));
        Assert.Equal(p, again);
    }

    [Theory]
    [InlineData(@"C:\Users")]
    [InlineData("http://exemple")]
    [InlineData("sftp://")]
    [InlineData("")]
    public void Not_remote_paths(string text) => Assert.False(PanelRemotePath.TryParse(text, out _));

    [Fact]
    public void Parent_and_names()
    {
        Assert.Null(RemotePath.Parent("/"));
        Assert.Equal("/", RemotePath.Parent("/home"));
        Assert.Equal("/home", RemotePath.Parent("/home/x/"));
        Assert.Equal("x", RemotePath.FileName("/home/x"));
        Assert.Equal("/a/b", RemotePath.Combine("/a", "b"));
        Assert.Equal("/b", RemotePath.Combine("/", "b"));
    }

    [Theory]
    [InlineData("rapport.pdf", true)]
    [InlineData("con.txt", false)]
    [InlineData("a:b", false)]
    [InlineData("fin.", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    public void Windows_names(string name, bool ok) => Assert.Equal(ok, RemotePath.IsValidWindowsName(name));

    [Fact]
    public void Shell_quoting_neutralises_special_characters()
    {
        Assert.Equal("'simple'", ScpFileSystem.Quote("simple"));
        Assert.Equal("'l'\\''arbre; rm -rf /'", ScpFileSystem.Quote("l'arbre; rm -rf /"));
        Assert.Equal("'$(whoami)'", ScpFileSystem.Quote("$(whoami)"));
    }

    [Fact]
    public void Scp_find_output_parsing()
    {
        var file = ScpFileSystem.ParseFindLine("/home", "f\tf\t1234\t1700000000.5\t644\trapport final.txt");
        Assert.NotNull(file);
        Assert.Equal("/home/rapport final.txt", file!.FullPath);
        Assert.Equal(1234, file.Size);
        Assert.Equal("rw-r--r--", file.Permissions);
        Assert.False(file.IsDirectory);
        var link = ScpFileSystem.ParseFindLine("/", "l\td\t7\t1700000000\t777\twww");
        Assert.True(link!.IsDirectory);
        Assert.True(link.IsLink);
        Assert.Null(ScpFileSystem.ParseFindLine("/", "f\tf\t1\t1\t644\t.."));
    }

    [Fact]
    public void WebDav_multistatus_parsing_with_nextcloud_root()
    {
        var xml = """
            <?xml version="1.0"?>
            <d:multistatus xmlns:d="DAV:">
              <d:response><d:href>/remote.php/dav/files/chris/Photos/</d:href>
                <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
              <d:response><d:href>/remote.php/dav/files/chris/Photos/%C3%A9t%C3%A9%202024.jpg</d:href>
                <d:propstat><d:prop><d:resourcetype/><d:getcontentlength>2048</d:getcontentlength>
                <d:getlastmodified>Tue, 01 Oct 2024 10:00:00 GMT</d:getlastmodified></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
              <d:response><d:href>https://nc.example/remote.php/dav/files/chris/Photos/Vacances/</d:href>
                <d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
              <d:response><d:href>/ailleurs/fichier</d:href>
                <d:propstat><d:prop><d:resourcetype/></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            </d:multistatus>
            """;
        var items = WebDavFileSystem.ParseForTests(xml, "/remote.php/dav/files/chris");
        Assert.Equal(3, items.Count);
        Assert.Equal("/Photos", items[0].FullPath);
        Assert.True(items[0].IsDirectory);
        Assert.Equal("été 2024.jpg", items[1].Name);
        Assert.Equal(2048, items[1].Size);
        Assert.Equal(new DateTime(2024, 10, 1, 10, 0, 0, DateTimeKind.Utc).ToLocalTime(), items[1].Modified);
        Assert.Equal("/Photos/Vacances", items[2].FullPath);
    }
}

public class ConnectionStoreTests
{
    private static ConnectionProfile Sample(string name = "chrypox") => new()
    {
        Name = name, Host = "192.168.0.10", Port = 22, UserName = "utilisateur", Protocol = RemoteProtocol.Sftp, Group = "Maison",
    };

    [Fact]
    public void Profiles_round_trip_and_export_contains_no_secret()
    {
        using var t = new TempDir();
        var store = new ConnectionStore(t.Path, new FakeProtector());
        var p = Sample();
        p.SavePassword = true;
        store.SaveProfile(p);
        store.Vault.Set(SecretIds.Password(p.Id), "SuperSecret42");

        var reloaded = new ConnectionStore(t.Path, new FakeProtector());
        var r = Assert.Single(reloaded.Profiles);
        Assert.Equal("chrypox", r.Name);
        Assert.Equal(RemoteProtocol.Sftp, r.Protocol);
        Assert.Equal("SuperSecret42", reloaded.Vault.Get(SecretIds.Password(r.Id)));
        Assert.DoesNotContain("SuperSecret42", File.ReadAllText(Path.Combine(t.Path, "connections.json")));

        var export = Path.Combine(t.Path, "export.json");
        reloaded.Export(reloaded.Profiles, export);
        Assert.DoesNotContain("SuperSecret42", File.ReadAllText(export));

        // Import dans un autre gestionnaire : nouvelle identité, nom unique, pas de mot de passe enregistré.
        using var t2 = new TempDir();
        var other = new ConnectionStore(t2.Path, new FakeProtector());
        other.SaveProfile(Sample());
        Assert.Equal(1, other.AddImported(ConnectionStore.ReadExport(export).Profiles));
        Assert.Contains(other.Profiles, x => x.Name == "chrypox (2)" && !x.SavePassword && x.Id != r.Id);
    }

    private static (ConnectionStore Store, ConnectionProfile[] Line) JumpLine(TempDir t, int count)
    {
        var store = new ConnectionStore(t.Path, new FakeProtector());
        var line = Enumerable.Range(0, count).Select(i => Sample("serveur" + i)).ToArray();
        foreach (var p in line) store.SaveProfile(p);
        for (int i = 0; i + 1 < count; i++) line[i].JumpHostId = line[i + 1].Id;
        return (store, line);
    }

    [Fact]
    public void Jump_chain_follows_the_jumps_in_order()
    {
        using var t = new TempDir();
        var (store, line) = JumpLine(t, 3);
        Assert.Equal([line[1].Id, line[2].Id], store.JumpChain(line[0]).Select(p => p.Id));
        Assert.Empty(store.JumpChain(line[2]));
    }

    [Fact]
    public void Jump_chain_refuses_a_loop()
    {
        using var t = new TempDir();
        var (store, line) = JumpLine(t, 3);
        line[2].JumpHostId = line[0].Id;
        Assert.Throws<RemoteException>(() => store.JumpChain(line[0]));
    }

    [Fact]
    public void Jump_chain_refuses_a_missing_or_endless_line()
    {
        using var t = new TempDir();
        var (store, line) = JumpLine(t, ConnectionStore.MaxJumps + 2);
        Assert.Throws<RemoteException>(() => store.JumpChain(line[0]));
        Assert.Equal(ConnectionStore.MaxJumps, store.JumpChain(line[1]).Count);
        line[^1].JumpHostId = Guid.NewGuid();
        Assert.Throws<RemoteException>(() => store.JumpChain(line[^2]));
    }

    [Fact]
    public void A_connection_cannot_jump_through_itself_by_a_detour()
    {
        using var t = new TempDir();
        var (store, line) = JumpLine(t, 3);
        Assert.True(store.CanJumpThrough(line[1], line[0].Id));
        Assert.False(store.CanJumpThrough(line[0], line[2].Id));
        Assert.False(store.CanJumpThrough(line[1], line[2].Id));
    }

    [Fact]
    public void Deleting_a_profile_removes_its_secrets_and_jump_references()
    {
        using var t = new TempDir();
        var store = new ConnectionStore(t.Path, new FakeProtector());
        var jump = Sample("rebond");
        store.SaveProfile(jump);
        var inner = Sample("interne");
        inner.JumpHostId = jump.Id;
        store.SaveProfile(inner);
        store.Vault.Set(SecretIds.Password(jump.Id), "x");
        store.DeleteProfile(jump.Id);
        Assert.Null(store.FindProfile(inner.Id)!.JumpHostId);
        Assert.False(store.Vault.Contains(SecretIds.Password(jump.Id)));
    }

    [Fact]
    public void Turning_off_save_password_erases_it()
    {
        using var t = new TempDir();
        var store = new ConnectionStore(t.Path, new FakeProtector());
        var p = Sample();
        p.SavePassword = true;
        store.SaveProfile(p);
        store.Vault.Set(SecretIds.Password(p.Id), "x");
        p.SavePassword = false;
        store.SaveProfile(p);
        Assert.False(store.Vault.Contains(SecretIds.Password(p.Id)));
    }

    [Fact]
    public void Validation_catches_common_mistakes()
    {
        var p = new ConnectionProfile { Name = "", Host = "sftp://exemple.be", Port = 0, Protocol = RemoteProtocol.Ftp, Auth = AuthMode.Key };
        var errors = p.Validate();
        Assert.Contains(errors, e => e.Contains("nom"));
        Assert.Contains(errors, e => e.Contains("barre oblique"));
        Assert.Contains(errors, e => e.Contains("port"));
        Assert.Contains(errors, e => e.Contains("SFTP et SCP"));
        Assert.Empty(Sample().Validate());
    }

    [Fact]
    public async Task Generated_key_stored_in_vault_resolves_for_connection()
    {
        using var t = new TempDir();
        var store = new ConnectionStore(t.Path, new FakeProtector());
        var text = SshKeys.GenerateEd25519("test");
        var info = SshKeys.Inspect(text, null, "test");
        var key = new KeychainKey { Name = "Ma clé", KeyType = info.KeyType, Fingerprint = info.Fingerprint, PublicKey = info.PublicKeyLine };
        store.SaveKey(key);
        store.Vault.Set(SecretIds.KeyMaterial(key.Id), text);
        var p = Sample();
        p.Auth = AuthMode.Key;
        p.KeyId = key.Id;
        store.SaveProfile(p);

        var creds = await CredentialResolver.ResolveAsync(p, store, new NoPrompts());
        Assert.NotNull(creds);
        Assert.Equal(text, creds!.KeyText);
        Assert.Equal("utilisateur", creds.UserName);
        Assert.Null(creds.Password);
    }
}

/// <summary>Aucune question ne doit être posée dans ces tests.</summary>
internal sealed class NoPrompts : IConnectionPrompts
{
    public Task<HostKeyDecision> ConfirmHostKeyAsync(HostKeyPrompt prompt) => throw new InvalidOperationException("question inattendue");
    public Task<CertificateDecision> ConfirmCertificateAsync(CertificatePrompt prompt) => throw new InvalidOperationException("question inattendue");
    public Task<SecretAnswer?> AskSecretAsync(SecretRequest request) => throw new InvalidOperationException("question inattendue : " + request.Message);
    public Task<bool> UnlockVaultAsync(SecretVault vault) => throw new InvalidOperationException("question inattendue");
    public Task<IReadOnlyList<string>?> AnswerInteractiveAsync(string title, string instruction, IReadOnlyList<InteractivePrompt> prompts) =>
        throw new InvalidOperationException("question inattendue");
}

/// <summary>Serveur imaginaire en mémoire, pour tester les transferts sans réseau.</summary>
internal sealed class MemoryFileSystem : IRemoteFileSystem, IServerSideMove
{
    /// <summary>Renommage refusé comme un serveur SFTP vers un autre disque (« Failure »).</summary>
    public bool RefuseRename { get; set; }
    /// <summary>« mv » du serveur : null = pas de commandes sur ce serveur ; sinon message d'erreur (vide = réussi).</summary>
    public string? ServerMoveError { get; set; }
    public bool HasServerMove { get; set; }

    /// <summary>Appelé pendant que « cp » ou « mv » tourne sur le serveur.</summary>
    public Func<Task>? DuringServerCommand { get; set; }
    /// <summary>La prochaine commande est coupée ; l'action dit ce que le serveur a fait entre-temps.</summary>
    public Action? CutServerCommand { get; set; }
    /// <summary>Appelé à chaque lecture d'informations : le serveur avance pendant qu'on le regarde.</summary>
    public Action? OnStat { get; set; }

    private void CutIfAsked(string what)
    {
        if (CutServerCommand is not { } cut) return;
        CutServerCommand = null;
        cut();
        throw ConnectionFailure.DuringCommand(what, new Renci.SshNet.Common.SshConnectionException("coupure"));
    }

    public async Task<ServerMoveResult> TryMoveOnServerAsync(string from, string to, CancellationToken ct)
    {
        if (!HasServerMove) return ServerMoveResult.NotAvailable;
        if (!string.IsNullOrEmpty(ServerMoveError)) return new ServerMoveResult(false, ServerMoveError);
        if (DuringServerCommand is { } during) await during();
        CutIfAsked("le déplacement");
        Nodes[N(to)] = Nodes[N(from)];
        Nodes.Remove(N(from));
        return ServerMoveResult.Done;
    }

    public Task<long?> MeasureOnServerAsync(string path, CancellationToken ct) => Task.FromResult<long?>(
        Nodes.Where(n => n.Key == N(path) || n.Key.StartsWith(N(path) + "/", StringComparison.Ordinal)).Sum(n => n.Value?.Length ?? 0));

    /// <summary>« sha256sum » disponible sur ce serveur.</summary>
    public bool HasServerHash { get; set; }
    public int ServerHashes { get; private set; }
    /// <summary>Fichiers que le serveur refuse de lire.</summary>
    public readonly HashSet<string> Unreadable = [];

    public Task<ServerHashResult> TryHashOnServerAsync(string path, CancellationToken ct)
    {
        if (!HasServerHash) return Task.FromResult(ServerHashResult.NotAvailable);
        ServerHashes++;
        if (Unreadable.Contains(path)) return Task.FromResult(ServerHashResult.From(1, string.Empty));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Nodes[N(path)]!));
        return Task.FromResult(ServerHashResult.From(0, $"{hash.ToLowerInvariant()}  {path}\n"));
    }

    public int ServerCopies { get; private set; }

    public async Task<ServerMoveResult> TryCopyOnServerAsync(string from, string to, CancellationToken ct)
    {
        if (!HasServerMove) return ServerMoveResult.NotAvailable;
        if (!string.IsNullOrEmpty(ServerMoveError)) return new ServerMoveResult(false, ServerMoveError);
        if (DuringServerCommand is { } during) await during();
        CutIfAsked("la copie");
        Nodes[N(to)] = Nodes[N(from)];
        if (Dates.TryGetValue(N(from), out var date)) Dates[N(to)] = date;
        ServerCopies++;
        return ServerMoveResult.Done;
    }

    public readonly Dictionary<string, byte[]?> Nodes = new(StringComparer.Ordinal) { ["/"] = null }; // null = dossier
    public readonly Dictionary<string, DateTime> Dates = new(StringComparer.Ordinal);

    public RemoteProtocol Protocol => RemoteProtocol.Sftp;
    public string HomeDirectory => "/";
    public bool IsConnected => true;
    public bool SupportsPermissions => false;

    private static string N(string p) => RemotePath.Normalize(p);

    public void AddFile(string path, string content, DateTime? date = null)
    {
        Nodes[N(path)] = Encoding.UTF8.GetBytes(content);
        Dates[N(path)] = date ?? new DateTime(2024, 1, 1, 12, 0, 0);
    }

    public void AddDir(string path) => Nodes[N(path)] = null;

    public Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct)
    {
        var dir = N(path);
        if (!Nodes.TryGetValue(dir, out var d) || d is not null) throw new RemoteException("introuvable : " + dir);
        IReadOnlyList<RemoteItem> r = Nodes.Keys.Where(k => k != "/" && RemotePath.Parent(k) == dir)
            .Select(k => Item(k)).ToList();
        return Task.FromResult(r);
    }

    private RemoteItem Item(string k) => new(RemotePath.FileName(k), k, Nodes[k] is null, Nodes[k]?.Length ?? 0, Dates.GetValueOrDefault(k, DateTime.Now),
        Mode: Modes.GetValueOrDefault(k, Nodes[k] is null ? 0x1ED : 0x1A4));

    public Task<RemoteItem?> StatAsync(string path, CancellationToken ct)
    {
        OnStat?.Invoke();
        return Task.FromResult(Nodes.ContainsKey(N(path)) ? Item(N(path)) : null);
    }

    public async Task DownloadAsync(string path, Stream target, CancellationToken ct) =>
        await DownloadFromAsync(path, target, 0, ct);

    public async Task UploadAsync(Stream source, string path, CancellationToken ct)
    {
        if (!Nodes.ContainsKey(RemotePath.Parent(N(path))!)) throw new RemoteException("parent manquant");
        await ReceiveAsync(source, path, [], ct);
    }

    // Coupures et reprise
    /// <summary>Prochains transferts : coupés après ce nombre d'octets (un par coupure voulue).</summary>
    public readonly Queue<long> CutAfter = new();
    public bool ResumeSupported { get; set; } = true;
    /// <summary>Le serveur annonce la reprise, puis la refuse.</summary>
    public bool RefuseResume { get; set; }
    public long BytesMoved { get; private set; }
    public readonly List<string> Uploads = [];

    public bool CanResumeDownload => ResumeSupported;
    public bool CanResumeUpload => ResumeSupported;

    public async Task DownloadFromAsync(string path, Stream target, long offset, CancellationToken ct)
    {
        if (RefuseResume && offset > 0) throw new RemoteException("reprise refusée");
        var data = (Nodes[N(path)] ?? throw new RemoteException("dossier")).AsMemory((int)offset);
        if (CutAfter.TryDequeue(out var cut) && cut < data.Length)
        {
            await target.WriteAsync(data[..(int)cut], ct);
            BytesMoved += cut;
            throw new RemoteException("coupure", new Renci.SshNet.Common.SshConnectionException("coupure"));
        }
        await target.WriteAsync(data, ct);
        BytesMoved += data.Length;
    }

    public Task AppendAsync(Stream source, string path, long offset, CancellationToken ct)
    {
        if (RefuseResume) throw new RemoteException("reprise refusée");
        return ReceiveAsync(source, path, Nodes[N(path)]!.AsSpan(0, (int)offset).ToArray(), ct);
    }

    private async Task ReceiveAsync(Stream source, string path, byte[] start, CancellationToken ct)
    {
        var ms = new MemoryStream();
        await source.CopyToAsync(ms, ct);
        var data = ms.ToArray();
        Uploads.Add(N(path));
        if (CutAfter.TryDequeue(out var cut) && cut < data.Length)
        {
            Nodes[N(path)] = [.. start, .. data.AsSpan(0, (int)cut)];
            BytesMoved += cut;
            throw new RemoteException("coupure", new Renci.SshNet.Common.SshConnectionException("coupure"));
        }
        Nodes[N(path)] = [.. start, .. data];
        BytesMoved += data.Length;
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        if (Nodes.ContainsKey(N(path))) throw new RemoteException("existe");
        Nodes[N(path)] = null;
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path, CancellationToken ct) { Nodes.Remove(N(path)); return Task.CompletedTask; }

    public Task DeleteDirectoryAsync(string path, CancellationToken ct)
    {
        if (Nodes.Keys.Any(k => RemotePath.Parent(k) == N(path) && k != "/")) throw new RemoteException("pas vide");
        Nodes.Remove(N(path));
        return Task.CompletedTask;
    }

    public Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct)
    {
        if (RefuseRename) throw new RemoteException("Renommage de " + RemotePath.FileName(from) + " : Failure");
        if (Nodes.ContainsKey(N(to)) && !overwrite) throw new RemoteException("existe");
        Nodes[N(to)] = Nodes[N(from)];
        if (Dates.TryGetValue(N(from), out var d)) Dates[N(to)] = d;
        Nodes.Remove(N(from));
        return Task.CompletedTask;
    }

    public Task SetModifiedAsync(string path, DateTime modified, CancellationToken ct) { Dates[N(path)] = modified; return Task.CompletedTask; }
    public Task SetPermissionsAsync(string path, int mode, CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Droits imaginaires (pour les tests de la fenêtre Propriétés)
    public readonly Dictionary<string, int> Modes = new(StringComparer.Ordinal);
    public readonly Dictionary<string, (int Uid, int Gid)> Owners = new(StringComparer.Ordinal);
    public readonly List<string> Refused = [];
    public bool SupportsOwnership => true;

    public Task<TwinPanel.Core.Rights.RightsDetails?> GetDetailsAsync(string path, CancellationToken ct)
    {
        var k = N(path);
        if (!Nodes.ContainsKey(k)) return Task.FromResult<TwinPanel.Core.Rights.RightsDetails?>(null);
        var o = Owners.GetValueOrDefault(k, (0, 0));
        return Task.FromResult<TwinPanel.Core.Rights.RightsDetails?>(new TwinPanel.Core.Rights.RightsDetails(k, Nodes[k] is null, false, Nodes[k]?.Length ?? 0, DateTime.Now)
        {
            Mode = Modes.GetValueOrDefault(k, Nodes[k] is null ? 0x1ED : 0x1A4), UserId = o.Item1, GroupId = o.Item2,
        });
    }

    public Task SetModeAsync(string path, int mode, CancellationToken ct)
    {
        if (Refused.Contains(N(path))) throw new RemoteException("refusé");
        Modes[N(path)] = mode;
        return Task.CompletedTask;
    }

    public Task SetOwnerAsync(string path, TwinPanel.Core.Rights.UnixAccount? user, TwinPanel.Core.Rights.UnixAccount? group, CancellationToken ct)
    {
        var o = Owners.GetValueOrDefault(N(path), (0, 0));
        Owners[N(path)] = (user?.Id ?? o.Item1, group?.Id ?? o.Item2);
        return Task.CompletedTask;
    }

    public Task<TwinPanel.Core.Rights.UnixAccounts?> GetAccountsAsync(CancellationToken ct) =>
        Task.FromResult<TwinPanel.Core.Rights.UnixAccounts?>(TwinPanel.Core.Rights.UnixAccounts.Parse("root:x:0:0::/root:/bin/sh\nchris:x:1000:1000::/home/chris:/bin/bash", "root:x:0:\nwww-data:x:33:"));
}

public class RemoteTransferTests
{
    [Fact]
    public async Task Upload_then_download_a_tree_keeps_content_and_dates()
    {
        using var t = new TempDir();
        t.File("src/photos/a.jpg", "AAAA");
        t.File("src/photos/2024/b.jpg", "BB");
        t.File("src/notes.txt", "texte");
        t.Dir("src/vide");
        var date = new DateTime(2023, 5, 6, 7, 8, 10);
        File.SetLastWriteTime(Path.Combine(t.Path, "src", "notes.txt"), date);

        var server = new MemoryFileSystem();
        server.AddDir("/home");
        var src = Path.Combine(t.Path, "src");
        await RemoteTransfer.UploadAsync(server, [Path.Combine(src, "photos"), Path.Combine(src, "notes.txt"), Path.Combine(src, "vide")], "/home", ConflictPolicy.Overwrite, null, default);

        Assert.Equal("BB", Encoding.UTF8.GetString(server.Nodes["/home/photos/2024/b.jpg"]!));
        Assert.Null(server.Nodes["/home/vide"]);
        Assert.Equal(date, server.Dates["/home/notes.txt"]);
        Assert.DoesNotContain(server.Nodes.Keys, k => k.EndsWith(RemoteTransfer.PartSuffix));

        var dest = t.Dir("retour");
        var items = await server.ListAsync("/home", default);
        await RemoteTransfer.DownloadAsync(server, items, dest, ConflictPolicy.Overwrite, null, default);
        Assert.Equal("AAAA", File.ReadAllText(Path.Combine(dest, "photos", "a.jpg")));
        Assert.Equal("BB", File.ReadAllText(Path.Combine(dest, "photos", "2024", "b.jpg")));
        Assert.True(Directory.Exists(Path.Combine(dest, "vide")));
        Assert.Equal(date, File.GetLastWriteTime(Path.Combine(dest, "notes.txt")));
        Assert.Empty(Directory.GetFiles(dest, "*" + RemoteTransfer.PartSuffix, SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Conflict_policies_on_upload()
    {
        using var t = new TempDir();
        var local = t.File("rapport.txt", "nouveau");
        var server = new MemoryFileSystem();
        server.AddFile("/rapport.txt", "ancien");

        await RemoteTransfer.UploadAsync(server, [local], "/", ConflictPolicy.Skip, null, default);
        Assert.Equal("ancien", Encoding.UTF8.GetString(server.Nodes["/rapport.txt"]!));

        await RemoteTransfer.UploadAsync(server, [local], "/", ConflictPolicy.RenameNew, null, default);
        Assert.Equal("nouveau", Encoding.UTF8.GetString(server.Nodes["/rapport (2).txt"]!));

        await RemoteTransfer.UploadAsync(server, [local], "/", ConflictPolicy.Overwrite, null, default);
        Assert.Equal("nouveau", Encoding.UTF8.GetString(server.Nodes["/rapport.txt"]!));
    }

    [Fact]
    public async Task Names_impossible_on_windows_are_reported_not_written()
    {
        using var t = new TempDir();
        var server = new MemoryFileSystem();
        server.AddDir("/d");
        server.AddFile("/d/ok.txt", "1");
        server.AddFile("/d/mauvais:nom.txt", "2");
        var dest = t.Dir("dest");
        var top = await server.ListAsync("/", default);
        var ex = await Assert.ThrowsAsync<FileOperationException>(() =>
            RemoteTransfer.DownloadAsync(server, top, dest, ConflictPolicy.Overwrite, null, default));
        Assert.Contains("mauvais:nom.txt", ex.Message);
        Assert.True(File.Exists(Path.Combine(dest, "d", "ok.txt")));
    }

    [Fact]
    public async Task Move_from_server_deletes_only_what_was_copied()
    {
        using var t = new TempDir();
        var server = new MemoryFileSystem();
        server.AddDir("/a");
        server.AddFile("/a/x.txt", "x");
        server.AddFile("/b.txt", "b");
        var dest = t.Dir("dest");
        t.File("dest/b.txt", "local");
        await RemoteTransfer.DownloadAsync(server, await server.ListAsync("/", default), dest, ConflictPolicy.Skip, null, default, deleteSource: true);
        Assert.False(server.Nodes.ContainsKey("/a"));
        Assert.True(server.Nodes.ContainsKey("/b.txt"));   // ignoré (conflit) : pas supprimé du serveur
        Assert.Equal("local", File.ReadAllText(Path.Combine(dest, "b.txt")));
    }

    [Fact]
    public async Task Cancelling_leaves_no_part_file()
    {
        using var t = new TempDir();
        var server = new MemoryFileSystem();
        server.AddFile("/gros.bin", new string('z', 100_000));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var top = await server.ListAsync("/", default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RemoteTransfer.DownloadAsync(server, top, t.Path, ConflictPolicy.Overwrite, null, cts.Token));
        Assert.Empty(Directory.GetFiles(t.Path, "*" + RemoteTransfer.PartSuffix));
    }
}

public class QuickConnectTests
{
    [Theory]
    [InlineData("utilisateur@192.168.0.10", RemoteProtocol.Sftp, "192.168.0.10", 22, "utilisateur", "")]
    [InlineData("sftp://root@srv:2222/var/www", RemoteProtocol.Sftp, "srv", 2222, "root", "/var/www")]
    [InlineData("ftps://chris@ftp.exemple.be", RemoteProtocol.FtpsExplicit, "ftp.exemple.be", 21, "chris", "")]
    [InlineData("ftp://ftp.debian.org/debian", RemoteProtocol.Ftp, "ftp.debian.org", 21, "", "/debian")]
    public void Addresses(string text, RemoteProtocol protocol, string host, int port, string user, string dir)
    {
        Assert.True(QuickConnect.TryParse(text, out var p, out var error), error);
        Assert.Equal((protocol, host, port, user, dir), (p.Protocol, p.Host, p.Port, p.UserName, p.RemoteDirectory));
    }

    [Fact]
    public void WebDav_path_becomes_service_root_and_passwords_are_dropped()
    {
        Assert.True(QuickConnect.TryParse("https://chris:secret@nc.exemple.be/remote.php/dav/files/chris", out var p, out _));
        Assert.Equal(RemoteProtocol.WebDavHttps, p.Protocol);
        Assert.Equal("/remote.php/dav/files/chris", p.WebDavRoot);
        Assert.Equal("chris", p.UserName);
        Assert.Equal(443, p.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gopher://x")]
    [InlineData("serveur-sans-utilisateur")]
    public void Rejected(string text) => Assert.False(QuickConnect.TryParse(text, out _, out _));
}

public class AuthorizedKeysTests
{
    [Fact]
    public async Task Installs_once_and_keeps_existing_keys()
    {
        var server = new MemoryFileSystem();
        var key = SshKeys.Inspect(SshKeys.GenerateEd25519("a@b"), null, "a@b").PublicKeyLine;

        Assert.True(await AuthorizedKeys.InstallAsync(server, key, default));
        Assert.Equal(key + "\n", Encoding.UTF8.GetString(server.Nodes["/.ssh/authorized_keys"]!));

        // Déjà présente (même avec un autre commentaire) : rien ne change.
        Assert.False(await AuthorizedKeys.InstallAsync(server, AuthorizedKeys.KeyPart(key) + " autre-commentaire", default));

        server.Nodes["/.ssh/authorized_keys"] = Encoding.UTF8.GetBytes("ssh-rsa AAAAexistante vieux@pc");
        Assert.True(await AuthorizedKeys.InstallAsync(server, key, default));
        Assert.Equal("ssh-rsa AAAAexistante vieux@pc\n" + key + "\n", Encoding.UTF8.GetString(server.Nodes["/.ssh/authorized_keys"]!));
        Assert.DoesNotContain(server.Nodes.Keys, k => k.EndsWith(RemoteTransfer.PartSuffix));
    }

    [Fact]
    public async Task Refuses_a_private_key()
    {
        var server = new MemoryFileSystem();
        await Assert.ThrowsAsync<RemoteException>(() => AuthorizedKeys.InstallAsync(server, "-----BEGIN OPENSSH PRIVATE KEY-----", default));
    }
}

public class RemoteSearchTests
{
    private static MemoryFileSystem Server()
    {
        var s = new MemoryFileSystem();
        s.AddDir("/home");
        s.AddDir("/home/docs");
        s.AddFile("/home/docs/rapport.txt", "le mot TwinPanel est ici");
        s.AddFile("/home/docs/photo.jpg", "binaire");
        s.AddFile("/home/notes.txt", "rien à voir");
        return s;
    }

    private static async Task<List<FileEntry>> Run(MemoryFileSystem fs, Search.SearchCriteria c)
    {
        Assert.True(PanelRemotePath.TryParse("sftp://chrypox" + c.Root, out var root));
        var list = new List<FileEntry>();
        await foreach (var e in Search.RemoteSearch.SearchAsync(fs, root, c)) list.Add(e);
        return list;
    }

    [Fact]
    public async Task Finds_by_mask_recursively_with_panel_paths()
    {
        var r = await Run(Server(), new Search.SearchCriteria { Root = "/home", NameMask = "*.txt", IncludeFolders = false });
        Assert.Equal(new[] { "sftp://chrypox/home/docs/rapport.txt", "sftp://chrypox/home/notes.txt" }, r.Select(e => e.FullPath).Order().ToArray());
    }

    [Fact]
    public async Task Finds_text_inside_files_and_respects_depth()
    {
        var r = await Run(Server(), new Search.SearchCriteria { Root = "/home", ContainsText = "twinpanel", IncludeFolders = false });
        Assert.Equal("rapport.txt", Assert.Single(r).Name);
        var shallow = await Run(Server(), new Search.SearchCriteria { Root = "/home", NameMask = "*.txt", MaxDepth = 0, IncludeFolders = false });
        Assert.Equal("notes.txt", Assert.Single(shallow).Name);
    }

    [Fact]
    public void Several_roots_are_split_and_deduplicated()
    {
        Assert.Equal(new[] { @"C:\", @"D:\Photos", "sftp://chrypox/home" },
            Search.SearchRoots.Split(@" C:\ ; ""D:\Photos"";sftp://chrypox/home;c:\ ;"));
    }
}

/// <summary>Déplacement sur le même serveur quand le renommage est refusé (destination sur un autre disque du serveur).</summary>
public class MoveOnServerFallbackTests
{
    private static (MemoryFileSystem Fs, RemoteItem Item) Setup()
    {
        var fs = new MemoryFileSystem { RefuseRename = true };
        fs.AddDir("/disque1");
        fs.AddDir("/disque2");
        fs.AddFile("/disque1/film.srt", "1\n00:00:01,000 --> 00:00:02,000\nBonjour");
        var item = fs.ListAsync("/disque1", CancellationToken.None).Result.Single();
        return (fs, item);
    }

    [Fact]
    public async Task Refused_rename_uses_server_mv()
    {
        var (fs, item) = Setup();
        fs.HasServerMove = true;
        var copy = new List<RemoteItem>();
        await RemoteTransfer.RenameIntoAsync(fs, [item], "/disque2", ConflictPolicy.Skip, null, copy, CancellationToken.None);
        Assert.Empty(copy);
        Assert.True(fs.Nodes.ContainsKey("/disque2/film.srt"));
        Assert.False(fs.Nodes.ContainsKey("/disque1/film.srt"));
    }

    [Fact]
    public async Task Refused_rename_without_commands_falls_back_to_copy()
    {
        var (fs, item) = Setup();
        var copy = new List<RemoteItem>();
        await RemoteTransfer.RenameIntoAsync(fs, [item], "/disque2", ConflictPolicy.Skip, null, copy, CancellationToken.None);
        Assert.Single(copy);
        Assert.True(fs.Nodes.ContainsKey("/disque1/film.srt")); // rien de perdu : la copie + suppression se fera ensuite
    }

    [Fact]
    public async Task Server_mv_error_is_reported_clearly()
    {
        var (fs, item) = Setup();
        fs.HasServerMove = true;
        fs.ServerMoveError = "mv: cannot move: Permission denied";
        var ex = await Assert.ThrowsAsync<RemoteException>(() =>
            RemoteTransfer.RenameIntoAsync(fs, [item], "/disque2", ConflictPolicy.Skip, null, [], CancellationToken.None));
        Assert.Contains("Permission denied", ex.Message);
        Assert.True(fs.Nodes.ContainsKey("/disque1/film.srt"));
    }

    [Fact]
    public async Task Copy_on_same_server_is_done_by_server_except_conflicts()
    {
        var fs = new MemoryFileSystem { HasServerMove = true };
        fs.AddDir("/a");
        fs.AddDir("/b");
        fs.AddFile("/a/un.srt", "1");
        fs.AddFile("/a/deux.srt", "2");
        fs.AddFile("/b/deux.srt", "ancien");
        var items = await fs.ListAsync("/a", CancellationToken.None);
        var progress = new List<TransferProgress>();
        var rest = await RemoteTransfer.CopyOnServerAsync(fs, items, "/b", new SyncProgress(progress), CancellationToken.None);
        Assert.Equal(1, fs.ServerCopies);
        Assert.True(fs.Nodes.ContainsKey("/b/un.srt"));
        Assert.True(fs.Nodes.ContainsKey("/a/un.srt")); // une copie : l'original reste
        Assert.Equal("deux.srt", Assert.Single(rest).Name); // conflit : conduite choisie, par le chemin habituel
    }

    /// <summary>Le serveur a écrit la moitié ; la commande ne finit qu'une fois cette moitié annoncée par la barre.</summary>
    private static Func<Task> HalfDone(MemoryFileSystem fs, string path, ConcurrentQueue<TransferProgress> seen, long half) => async () =>
    {
        fs.Nodes[path] = new byte[half];
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!seen.Any(p => p.BytesDone > 0 && p.BytesDone < p.BytesTotal)) await Task.Delay(5, limit.Token);
    };

    private sealed class QueueProgress(ConcurrentQueue<TransferProgress> queue) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => queue.Enqueue(value);
    }

    [Fact]
    public async Task Bar_moves_while_the_server_copies_a_file()
    {
        var fs = new MemoryFileSystem { HasServerMove = true };
        fs.AddDir("/a");
        fs.AddDir("/b");
        fs.Nodes["/a/film.mkv"] = new byte[1000];
        var seen = new ConcurrentQueue<TransferProgress>();
        fs.DuringServerCommand = HalfDone(fs, "/b/film.mkv", seen, 400);

        await RemoteTransfer.CopyOnServerAsync(fs, await fs.ListAsync("/a", default), "/b", new QueueProgress(seen), default, TimeSpan.FromMilliseconds(5));

        Assert.Contains(seen, p => p.BytesDone == 400 && p.BytesTotal == 1000);
        Assert.Equal(1000, seen.Last().BytesDone);
        Assert.Equal(1, seen.Last().FilesDone);
    }

    [Fact]
    public async Task Bar_moves_while_the_server_copies_a_folder()
    {
        var fs = new MemoryFileSystem { HasServerMove = true };
        fs.AddDir("/a");
        fs.AddDir("/a/photos");
        fs.AddDir("/b");
        fs.Nodes["/a/photos/1.jpg"] = new byte[600];
        fs.Nodes["/a/photos/2.jpg"] = new byte[400];
        var seen = new ConcurrentQueue<TransferProgress>();
        fs.DuringServerCommand = HalfDone(fs, "/b/photos", seen, 300);

        await RemoteTransfer.CopyOnServerAsync(fs, await fs.ListAsync("/a", default), "/b", new QueueProgress(seen), default, TimeSpan.FromMilliseconds(5));

        Assert.Contains(seen, p => p.BytesDone == 300 && p.BytesTotal == 1000);
        Assert.Equal(1000, seen.Last().BytesDone);
    }

    [Fact]
    public async Task Bar_moves_while_the_server_moves_to_another_disk()
    {
        var fs = new MemoryFileSystem { HasServerMove = true, RefuseRename = true };
        fs.AddDir("/a");
        fs.AddDir("/b");
        fs.Nodes["/a/film.mkv"] = new byte[1000];
        var seen = new ConcurrentQueue<TransferProgress>();
        fs.DuringServerCommand = HalfDone(fs, "/b/film.mkv", seen, 700);

        await RemoteTransfer.RenameIntoAsync(fs, await fs.ListAsync("/a", default), "/b", ConflictPolicy.Overwrite, new QueueProgress(seen), [], default, TimeSpan.FromMilliseconds(5));

        Assert.Contains(seen, p => p.BytesDone == 700);
        Assert.Equal(1000, seen.Last().BytesDone);
        Assert.False(fs.Nodes.ContainsKey("/a/film.mkv"));
    }

    [Fact]
    public async Task Instant_rename_fills_the_bar()
    {
        var fs = new MemoryFileSystem();
        fs.AddDir("/a");
        fs.AddDir("/b");
        fs.Nodes["/a/film.mkv"] = new byte[1000];
        var seen = new ConcurrentQueue<TransferProgress>();

        await RemoteTransfer.RenameIntoAsync(fs, await fs.ListAsync("/a", default), "/b", ConflictPolicy.Overwrite, new QueueProgress(seen), [], default);

        Assert.Equal(1, seen.Last().Fraction);
    }

    [Theory]
    [InlineData("39874561024\t/srv/films\n", 39874561024L)]
    [InlineData("0\t/vide", 0L)]
    [InlineData("du: option inconnue", null)]
    [InlineData("", null)]
    public void Disk_usage_output_is_read(string output, long? expected) => Assert.Equal(expected, RemoteTransfer.ParseDiskUsage(output));

    [Fact]
    public async Task Copy_on_server_without_commands_goes_through_pc()
    {
        var fs = new MemoryFileSystem();
        fs.AddDir("/a");
        fs.AddDir("/b");
        fs.AddFile("/a/un.srt", "1");
        var items = await fs.ListAsync("/a", CancellationToken.None);
        var rest = await RemoteTransfer.CopyOnServerAsync(fs, items, "/b", null, CancellationToken.None);
        Assert.Single(rest);
        Assert.False(fs.Nodes.ContainsKey("/b/un.srt"));
    }

    private sealed class SyncProgress(List<TransferProgress> list) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => list.Add(value);
    }
}

