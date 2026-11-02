// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwinPanel.Core.Remote;

/// <summary>Empreinte d'un serveur SSH déjà acceptée.</summary>
public sealed class KnownSshHost
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    /// <summary>Type de clé du serveur (« ssh-ed25519 »…).</summary>
    public string KeyType { get; set; } = string.Empty;
    /// <summary>« SHA256:… » (format d'OpenSSH).</summary>
    public string Fingerprint { get; set; } = string.Empty;
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Certificat TLS (FTPS / WebDAV) accepté explicitement alors que Windows ne lui fait pas confiance (auto-signé…).</summary>
public sealed class TrustedCertificate
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    /// <summary>SHA-256 du certificat, en hexadécimal.</summary>
    public string Sha256 { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime NotAfterUtc { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
}

public enum HostKeyStatus
{
    /// <summary>Clé connue et identique : on continue sans rien demander.</summary>
    Trusted,
    /// <summary>Premier contact (ou nouveau type de clé) : il faut demander.</summary>
    Unknown,
    /// <summary>La clé a CHANGÉ : possible attaque de l'homme du milieu. On bloque.</summary>
    Changed,
}

/// <summary>
/// Serveurs connus : empreintes SSH (comme ~/.ssh/known_hosts) et certificats acceptés à la main.
/// Le fichier known_hosts d'OpenSSH est aussi lu (sans jamais y écrire) : un serveur déjà connu d'OpenSSH n'est pas redemandé.
/// </summary>
public sealed class KnownHosts
{
    private readonly string _path;
    private readonly string? _openSshKnownHosts;
    private readonly object _gate = new();
    private Data _data = new();

    public KnownHosts(string path, string? openSshKnownHosts = null)
    {
        _path = path;
        _openSshKnownHosts = openSshKnownHosts;
        if (File.Exists(path))
        {
            try { _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new Data(); }
            catch (JsonException) { File.Copy(path, path + ".illisible", overwrite: true); }
        }
    }

    /// <summary>~/.ssh/known_hosts de l'utilisateur.</summary>
    public static string DefaultOpenSshFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "known_hosts");

    public IReadOnlyList<KnownSshHost> SshHosts { get { lock (_gate) return [.. _data.Ssh]; } }
    public IReadOnlyList<TrustedCertificate> Certificates { get { lock (_gate) return [.. _data.Certificates]; } }

    /// <summary>Format OpenSSH « SHA256:base64-sans-= » d'une clé publique (format SSH).</summary>
    public static string Sha256Fingerprint(byte[] publicKeyBlob) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(publicKeyBlob)).TrimEnd('=');

    public HostKeyStatus Check(string host, int port, string keyType, byte[] publicKeyBlob)
    {
        var fp = Sha256Fingerprint(publicKeyBlob);
        lock (_gate)
        {
            var sameHost = _data.Ssh.Where(h => SameHost(h.Host, h.Port, host, port)).ToList();
            var sameType = sameHost.FirstOrDefault(h => h.KeyType == keyType);
            if (sameType is not null) return sameType.Fingerprint == fp ? HostKeyStatus.Trusted : HostKeyStatus.Changed;
        }
        // Pas encore dans TwinPanel : OpenSSH le connaît-il déjà ?
        return CheckOpenSsh(host, port, keyType, publicKeyBlob) ?? HostKeyStatus.Unknown;
    }

    /// <summary>Enregistre (ou remplace) l'empreinte acceptée pour ce serveur et ce type de clé.</summary>
    public void Trust(string host, int port, string keyType, byte[] publicKeyBlob)
    {
        lock (_gate)
        {
            _data.Ssh.RemoveAll(h => SameHost(h.Host, h.Port, host, port) && h.KeyType == keyType);
            _data.Ssh.Add(new KnownSshHost { Host = host, Port = port, KeyType = keyType, Fingerprint = Sha256Fingerprint(publicKeyBlob) });
            Save();
        }
    }

    public void Forget(KnownSshHost entry)
    {
        lock (_gate)
        {
            _data.Ssh.RemoveAll(h => SameHost(h.Host, h.Port, entry.Host, entry.Port) && h.KeyType == entry.KeyType);
            Save();
        }
    }

    public bool IsCertificateTrusted(string host, int port, string sha256Hex)
    {
        lock (_gate)
            return _data.Certificates.Any(c => SameHost(c.Host, c.Port, host, port)
                                               && string.Equals(c.Sha256, sha256Hex, StringComparison.OrdinalIgnoreCase));
    }

    public void TrustCertificate(string host, int port, string sha256Hex, string subject, DateTime notAfterUtc)
    {
        lock (_gate)
        {
            _data.Certificates.RemoveAll(c => SameHost(c.Host, c.Port, host, port));
            _data.Certificates.Add(new TrustedCertificate { Host = host, Port = port, Sha256 = sha256Hex.ToUpperInvariant(), Subject = subject, NotAfterUtc = notAfterUtc });
            Save();
        }
    }

    public void Forget(TrustedCertificate entry)
    {
        lock (_gate)
        {
            _data.Certificates.RemoveAll(c => SameHost(c.Host, c.Port, entry.Host, entry.Port) && c.Sha256 == entry.Sha256);
            Save();
        }
    }

    private static bool SameHost(string h1, int p1, string h2, int p2) =>
        p1 == p2 && string.Equals(h1, h2, StringComparison.OrdinalIgnoreCase);

    // ---- known_hosts d'OpenSSH (lecture seule) ----------------------------------------------------

    private HostKeyStatus? CheckOpenSsh(string host, int port, string keyType, byte[] blob)
    {
        if (_openSshKnownHosts is null || !File.Exists(_openSshKnownHosts)) return null;
        string[] lines;
        try { lines = File.ReadAllLines(_openSshKnownHosts); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        return CheckOpenSshLines(lines, host, port, keyType, blob);
    }

    /// <summary>
    /// Vérifie un serveur dans des lignes au format known_hosts : « hôte[,hôte] type clé » ou « [hôte]:port … »,
    /// et les noms hachés « |1|sel|hachage ». Les lignes @revoked font refuser la clé (Changed) ; @cert-authority est ignoré.
    /// </summary>
    public static HostKeyStatus? CheckOpenSshLines(IEnumerable<string> lines, string host, int port, string keyType, byte[] blob)
    {
        var wanted = port == 22 ? host : $"[{host}]:{port}";
        var key64 = Convert.ToBase64String(blob);
        HostKeyStatus? result = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            bool revoked = false;
            if (parts[0].StartsWith('@'))
            {
                if (parts[0] == "@revoked") revoked = true;
                else continue; // @cert-authority : non pris en charge
                parts = parts[1..];
            }
            if (parts.Length < 3) continue;
            if (!parts[0].Split(',').Any(pattern => HostMatches(pattern, wanted))) continue;
            if (revoked) { if (parts[2] == key64) return HostKeyStatus.Changed; continue; }
            if (parts[1] != keyType) continue;
            if (parts[2] == key64) result = HostKeyStatus.Trusted;
            else result ??= HostKeyStatus.Changed;
        }
        return result;
    }

    private static bool HostMatches(string pattern, string wanted)
    {
        if (pattern.StartsWith("|1|", StringComparison.Ordinal))
        {
            var bits = pattern.Split('|');
            if (bits.Length != 4) return false;
            try
            {
                var salt = Convert.FromBase64String(bits[2]);
                var hash = HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(wanted));
                return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(bits[3]));
            }
            catch (FormatException) { return false; }
        }
        return !pattern.StartsWith('!') && !pattern.Contains('*') && !pattern.Contains('?')
               && string.Equals(pattern, wanted, StringComparison.OrdinalIgnoreCase);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }

    private sealed class Data
    {
        public List<KnownSshHost> Ssh { get; set; } = [];
        public List<TrustedCertificate> Certificates { get; set; } = [];
    }
}
