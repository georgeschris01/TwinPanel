// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TwinPanel.Core.Remote;

/// <summary>Le coffre est protégé par un mot de passe maître qui n'a pas encore été donné.</summary>
public sealed class VaultLockedException() : Exception(L.T("Le coffre des mots de passe est verrouillé : il faut le mot de passe maître."));

/// <summary>Noms des secrets rangés dans le coffre.</summary>
public static class SecretIds
{
    public static string Password(Guid profile) => $"pwd:{profile:N}";
    public static string ProxyPassword(Guid profile) => $"proxy:{profile:N}";
    public static string CertificatePassword(Guid profile) => $"cert:{profile:N}";
    public static string KeyMaterial(Guid key) => $"key:{key:N}";
    public static string KeyPassphrase(Guid key) => $"keypass:{key:N}";
    public static string IdentityPassword(Guid identity) => $"idpwd:{identity:N}";
}

/// <summary>Chiffrement lié au compte de l'utilisateur : DPAPI sous Windows, trousseau sous Linux. S'il est indisponible, le mot de passe maître devient obligatoire.</summary>
public interface IUserDataProtector
{
    bool IsAvailable { get; }
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class WindowsDataProtector : IUserDataProtector
{
    // « Entropie » propre à TwinPanel : un autre programme qui utilise DPAPI ne déchiffre pas nos données par hasard.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TwinPanel.SecretVault.v1");

    public bool IsAvailable => OperatingSystem.IsWindows();

    public byte[] Protect(byte[] data) => OperatingSystem.IsWindows() ? ProtectWin(data) : throw new PlatformNotSupportedException();
    public byte[] Unprotect(byte[] data) => OperatingSystem.IsWindows() ? UnprotectWin(data) : throw new PlatformNotSupportedException();

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWin(byte[] data) => ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWin(byte[] data) => ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// Coffre des secrets (mots de passe, phrases de passe, clés privées), séparé de la liste des connexions.
/// Couche 1 (optionnelle) : AES-256-GCM avec une clé tirée du mot de passe maître (PBKDF2-SHA256, 600 000 tours).
/// Couche 2 : le compte de l'utilisateur, avec DPAPI sous Windows ou le trousseau sous Linux.
/// Le fichier retient si cette couche a servi : le trousseau peut apparaître ou disparaître d'une session à l'autre.
/// Rien n'est jamais écrit en clair ; le fichier est réécrit d'un bloc (fichier temporaire puis remplacement).
/// </summary>
public sealed class SecretVault
{
    private const int Iterations = 600_000;
    private static readonly byte[] CheckPlain = Encoding.UTF8.GetBytes("TwinPanel-vault-check");

    private readonly string _path;
    private readonly IUserDataProtector _protector;
    private readonly object _gate = new();
    private VaultFile _file = new();
    private byte[]? _masterKey;
    private DateTime _lastUseUtc = DateTime.UtcNow;

    public SecretVault(string path, IUserDataProtector? protector = null)
    {
        _path = path;
        _protector = protector ?? (OperatingSystem.IsLinux() ? new LinuxKeyringProtector() : new WindowsDataProtector());
        Load();
    }

    /// <summary>Un mot de passe maître protège le coffre.</summary>
    public bool HasMasterPassword => _file.Master is not null;
    /// <summary>Vrai si les secrets sont lisibles maintenant.</summary>
    public bool IsUnlocked { get { lock (_gate) { CheckAutoLock(); return _file.Master is null || _masterKey is not null; } } }
    /// <summary>Le coffre peut-il fonctionner sans mot de passe maître ?</summary>
    public bool CanWorkWithoutMaster => _protector.IsAvailable;
    /// <summary>Les secrets sont aussi protégés par le compte de l'utilisateur.</summary>
    public bool UsesAccountProtection { get { lock (_gate) return Layered; } }
    /// <summary>Verrouillage automatique après ce délai sans utilisation (0 : jamais).</summary>
    public int AutoLockMinutes { get => _file.AutoLockMinutes; set { lock (_gate) { _file.AutoLockMinutes = Math.Max(0, value); Save(); } } }

    public bool Contains(string id) { lock (_gate) return _file.Items.ContainsKey(id); }

    /// <summary>Lit un secret ; null s'il n'existe pas. Lève <see cref="VaultLockedException"/> si le coffre est verrouillé.</summary>
    public string? Get(string id)
    {
        lock (_gate)
        {
            CheckAutoLock();
            if (!_file.Items.TryGetValue(id, out var b64)) return null;
            var plain = Decrypt(Convert.FromBase64String(b64));
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }

    /// <summary>Enregistre (ou remplace) un secret. Une valeur null ou vide le supprime.</summary>
    public void Set(string id, string? value)
    {
        lock (_gate)
        {
            CheckAutoLock();
            if (string.IsNullOrEmpty(value)) { if (_file.Items.Remove(id)) Save(); return; }
            var plain = Encoding.UTF8.GetBytes(value);
            try { _file.Items[id] = Convert.ToBase64String(Encrypt(plain)); }
            finally { CryptographicOperations.ZeroMemory(plain); }
            Save();
        }
    }

    public void Remove(string id)
    {
        lock (_gate) { if (_file.Items.Remove(id)) Save(); }
    }

    /// <summary>Supprime tous les secrets dont le nom commence par ce préfixe… pour un identifiant donné (ex. tous ceux d'une connexion).</summary>
    public void RemoveAllFor(Guid owner)
    {
        var suffix = ":" + owner.ToString("N");
        lock (_gate)
        {
            var ids = _file.Items.Keys.Where(k => k.EndsWith(suffix, StringComparison.Ordinal)).ToList();
            foreach (var k in ids) _file.Items.Remove(k);
            if (ids.Count > 0) Save();
        }
    }

    /// <summary>Déverrouille avec le mot de passe maître. Retourne false s'il est faux.</summary>
    public bool Unlock(string masterPassword)
    {
        lock (_gate)
        {
            if (_file.Master is not { } m) return true;
            var key = DeriveKey(masterPassword, Convert.FromBase64String(m.Salt), m.Iterations);
            try
            {
                var check = AesOpen(key, Convert.FromBase64String(m.Check));
                if (!CryptographicOperations.FixedTimeEquals(check, CheckPlain)) { CryptographicOperations.ZeroMemory(key); return false; }
            }
            catch (CryptographicException)
            {
                CryptographicOperations.ZeroMemory(key);
                return false;
            }
            ForgetKey();
            _masterKey = key;
            _lastUseUtc = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>Oublie la clé maître en mémoire (les secrets redeviennent illisibles jusqu'au prochain déverrouillage).</summary>
    public void Lock()
    {
        lock (_gate) ForgetKey();
    }

    /// <summary>
    /// Ajoute, change (newPassword non vide) ou retire (newPassword vide) le mot de passe maître.
    /// Tous les secrets sont rechiffrés. Le coffre doit être déverrouillé.
    /// </summary>
    public void SetMasterPassword(string? newPassword)
    {
        lock (_gate)
        {
            CheckAutoLock();
            if (string.IsNullOrEmpty(newPassword) && !_protector.IsAvailable)
                throw new InvalidOperationException(L.T("Sur ce système, le mot de passe maître est obligatoire."));

            // 1. Tout déchiffrer avec l'ancienne protection.
            var plain = _file.Items.ToDictionary(kv => kv.Key, kv => Decrypt(Convert.FromBase64String(kv.Value)));
            try
            {
                // 2. Nouvelle protection.
                ForgetKey();
                if (string.IsNullOrEmpty(newPassword)) _file.Master = null;
                else
                {
                    var salt = RandomNumberGenerator.GetBytes(16);
                    _masterKey = DeriveKey(newPassword, salt, Iterations);
                    _file.Master = new MasterInfo
                    {
                        Salt = Convert.ToBase64String(salt),
                        Iterations = Iterations,
                        Check = Convert.ToBase64String(AesSeal(_masterKey, CheckPlain)),
                    };
                }
                // 3. Tout rechiffrer, avec la protection du compte si elle est là maintenant.
                _file.UsesAccountProtection = _protector.IsAvailable;
                _file.Items = plain.ToDictionary(kv => kv.Key, kv => Convert.ToBase64String(Encrypt(kv.Value)));
                Save();
            }
            finally
            {
                foreach (var v in plain.Values) CryptographicOperations.ZeroMemory(v);
            }
        }
    }

    // ---- Interne ---------------------------------------------------------------------

    private void CheckAutoLock()
    {
        if (_masterKey is not null && _file.AutoLockMinutes > 0 && DateTime.UtcNow - _lastUseUtc > TimeSpan.FromMinutes(_file.AutoLockMinutes))
            ForgetKey();
        if (_file.Master is not null && _masterKey is null) return;
        _lastUseUtc = DateTime.UtcNow;
    }

    /// <summary>La couche du compte a-t-elle servi pour ce coffre ? Un coffre vide peut choisir à nouveau.</summary>
    private bool Layered
    {
        get
        {
            if (_file.Items.Count == 0) _file.UsesAccountProtection = null;
            return _file.UsesAccountProtection ??= _protector.IsAvailable;
        }
    }

    private byte[] Encrypt(byte[] plain)
    {
        bool layered = Layered;
        byte[] inner;
        if (_file.Master is not null)
        {
            if (_masterKey is null) throw new VaultLockedException();
            inner = AesSeal(_masterKey, plain);
        }
        else
        {
            if (!layered) throw new InvalidOperationException(L.T("Sur ce système, il faut un mot de passe maître pour enregistrer des secrets."));
            inner = (byte[])plain.Clone();
        }
        if (!layered) return inner; // déjà chiffré par le mot de passe maître
        try { return _protector.IsAvailable ? _protector.Protect(inner) : throw new VaultKeyringException(); }
        finally { CryptographicOperations.ZeroMemory(inner); }
    }

    private byte[] Decrypt(byte[] stored)
    {
        if (_file.Master is not null && _masterKey is null) throw new VaultLockedException();
        byte[] inner = stored;
        if (Layered) inner = _protector.IsAvailable ? _protector.Unprotect(stored) : throw new VaultKeyringException();
        if (_file.Master is null) return inner;
        try { return AesOpen(_masterKey!, inner); }
        finally { CryptographicOperations.ZeroMemory(inner); }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>nonce (12) + tag (16) + données chiffrées.</summary>
    internal static byte[] AesSeal(byte[] key, byte[] plain)
    {
        var output = new byte[12 + 16 + plain.Length];
        var nonce = output.AsSpan(0, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, output.AsSpan(28), output.AsSpan(12, 16));
        return output;
    }

    internal static byte[] AesOpen(byte[] key, byte[] sealedData)
    {
        if (sealedData.Length < 28) throw new CryptographicException(L.T("Donnée chiffrée trop courte."));
        var plain = new byte[sealedData.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(sealedData.AsSpan(0, 12), sealedData.AsSpan(28), sealedData.AsSpan(12, 16), plain);
        return plain;
    }

    private void ForgetKey()
    {
        if (_masterKey is not null) CryptographicOperations.ZeroMemory(_masterKey);
        _masterKey = null;
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try { _file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(_path)) ?? new VaultFile(); }
        catch (JsonException)
        {
            // On ne détruit jamais un coffre illisible : on le met de côté.
            File.Copy(_path, _path + ".illisible", overwrite: true);
            _file = new VaultFile();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.Delete(tmp);
        var text = JsonSerializer.Serialize(_file, new JsonSerializerOptions { WriteIndented = true });
        // Chiffré ou non, ce fichier n'a pas à être lisible par les autres comptes du PC.
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var writer = new StreamWriter(tmp, Encoding.UTF8, options)) writer.Write(text);
        File.Move(tmp, _path, overwrite: true);
    }

    private sealed class VaultFile
    {
        public int Version { get; set; } = 1;
        public MasterInfo? Master { get; set; }
        public int AutoLockMinutes { get; set; } = 15;
        /// <summary>Absent dans un coffre plus ancien : la couche du compte y servait dès qu'elle était disponible.</summary>
        public bool? UsesAccountProtection { get; set; }
        public Dictionary<string, string> Items { get; set; } = [];
    }

    private sealed class MasterInfo
    {
        public string Salt { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Check { get; set; } = string.Empty;
    }
}
