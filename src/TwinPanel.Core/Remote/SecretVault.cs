// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
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
    public static string TerminalCommand(Guid command) => $"cmd:{command:N}";
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
    private (DateTime, long)? _stamp;

    public SecretVault(string path, IUserDataProtector? protector = null)
    {
        _path = path;
        _protector = protector ?? (OperatingSystem.IsLinux() ? new LinuxKeyringProtector() : new WindowsDataProtector());
        Load();
    }

    /// <summary>Un mot de passe maître protège le coffre.</summary>
    public bool HasMasterPassword { get { lock (_gate) { Refresh(); return _file.Master is not null; } } }
    /// <summary>Vrai si les secrets sont lisibles maintenant.</summary>
    public bool IsUnlocked { get { lock (_gate) { Refresh(); CheckAutoLock(); return _file.Master is null || _masterKey is not null; } } }
    /// <summary>Le coffre peut-il fonctionner sans mot de passe maître ?</summary>
    public bool CanWorkWithoutMaster => _protector.IsAvailable;
    /// <summary>Les secrets sont aussi protégés par le compte de l'utilisateur.</summary>
    public bool UsesAccountProtection { get { lock (_gate) { Refresh(); return Layered; } } }
    /// <summary>Verrouillage automatique après ce délai sans utilisation (0 : jamais).</summary>
    public int AutoLockMinutes { get { lock (_gate) { Refresh(); return _file.AutoLockMinutes; } } set { lock (_gate) { Refresh(); _file.AutoLockMinutes = Math.Max(0, value); Save(); } } }

    /// <summary>Un code de secours permet de retrouver le coffre si le mot de passe maître est oublié.</summary>
    public bool HasRecoveryCode { get { lock (_gate) { Refresh(); return _file.Master?.Recovery is not null; } } }

    public bool Contains(string id) { lock (_gate) { Refresh(); return _file.Items.ContainsKey(id); } }

    /// <summary>Lit un secret ; null s'il n'existe pas. Lève <see cref="VaultLockedException"/> si le coffre est verrouillé.</summary>
    public string? Get(string id)
    {
        lock (_gate)
        {
            Refresh();
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
            Refresh();
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
        lock (_gate) { Refresh(); if (_file.Items.Remove(id)) Save(); }
    }

    /// <summary>Supprime tous les secrets dont le nom commence par ce préfixe… pour un identifiant donné (ex. tous ceux d'une connexion).</summary>
    public void RemoveAllFor(Guid owner)
    {
        var suffix = ":" + owner.ToString("N");
        lock (_gate)
        {
            Refresh();
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
            Refresh();
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

    /// <summary>
    /// Crée un nouveau code de secours (l'ancien ne marche plus) et le rend : il n'est montré qu'une fois.
    /// Il ouvre la clé du mot de passe maître, mais pas la protection du compte (Windows / trousseau) : volé seul, il ne sert à rien.
    /// Le coffre doit avoir un mot de passe maître et être déverrouillé.
    /// </summary>
    public string CreateRecoveryCode()
    {
        lock (_gate)
        {
            Refresh();
            CheckAutoLock();
            if (_file.Master is not { } master) throw new InvalidOperationException(L.T("Il faut d'abord un mot de passe maître."));
            if (_masterKey is null) throw new VaultLockedException();
            var code = RecoveryCodes.New();
            var salt = RandomNumberGenerator.GetBytes(16);
            var key = DeriveKey(RecoveryCodes.Normalize(code), salt, Iterations);
            try
            {
                master.Recovery = new RecoveryInfo
                {
                    Salt = Convert.ToBase64String(salt),
                    Iterations = Iterations,
                    Wrapped = Convert.ToBase64String(AesSeal(key, _masterKey)),
                };
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            Save();
            return code;
        }
    }

    /// <summary>Mot de passe maître oublié : déverrouille avec le code de secours. Retourne false s'il est faux (ou s'il n'y en a pas).</summary>
    public bool UnlockWithRecoveryCode(string code)
    {
        lock (_gate)
        {
            Refresh();
            if (_file.Master is not { Recovery: { } recovery } master) return false;
            if (recovery.Iterations is < 1 or > 10_000_000) return false;
            var key = DeriveKey(RecoveryCodes.Normalize(code), Convert.FromBase64String(recovery.Salt), recovery.Iterations);
            byte[]? masterKey = null;
            try
            {
                masterKey = AesOpen(key, Convert.FromBase64String(recovery.Wrapped));
                var check = AesOpen(masterKey, Convert.FromBase64String(master.Check));
                if (!CryptographicOperations.FixedTimeEquals(check, CheckPlain)) { CryptographicOperations.ZeroMemory(masterKey); return false; }
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
            {
                if (masterKey is not null) CryptographicOperations.ZeroMemory(masterKey);
                return false;
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            ForgetKey();
            _masterKey = masterKey;
            _lastUseUtc = DateTime.UtcNow;
            return true;
        }
    }

    /// <summary>
    /// Mot de passe maître oublié, sans code de secours : tous les secrets sont effacés et le coffre repart vide, sans mot de passe maître.
    /// L'ancien fichier est d'abord copié à côté (au cas où le mot de passe reviendrait) : son chemin est rendu.
    /// </summary>
    public string? ResetForgotten()
    {
        lock (_gate)
        {
            Refresh();
            string? kept = null;
            if (File.Exists(_path))
            {
                kept = $"{_path}.oublie-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(_path, kept, overwrite: true);
            }
            ForgetKey();
            _file = new VaultFile { AutoLockMinutes = _file.AutoLockMinutes };
            Save();
            return kept;
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
            Refresh();
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
        _stamp = Stamp();
        if (!File.Exists(_path)) return;
        try { _file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(_path)) ?? new VaultFile(); }
        catch (JsonException)
        {
            // On ne détruit jamais un coffre illisible : on le met de côté.
            File.Copy(_path, _path + ".illisible", overwrite: true);
            _file = new VaultFile();
        }
    }

    /// <summary>Date et taille du fichier : un changement veut dire qu'un autre TwinPanel ouvert l'a réécrit.</summary>
    private (DateTime, long)? Stamp()
    {
        try
        {
            var info = new FileInfo(_path);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (IOException) { return _stamp; }
        catch (UnauthorizedAccessException) { return _stamp; }
    }

    /// <summary>
    /// Relit le coffre s'il a été réécrit par un autre TwinPanel ouvert : sinon, le dernier qui enregistre effacerait les changements de l'autre.
    /// Si le mot de passe maître a changé entre-temps, le coffre se verrouille : il faudra le nouveau.
    /// Un fichier illisible pour un instant (en cours de remplacement) est relu à la prochaine occasion.
    /// </summary>
    private void Refresh()
    {
        var stamp = Stamp();
        if (stamp == _stamp) return;
        VaultFile fresh;
        try
        {
            fresh = stamp is null ? new VaultFile() : JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(_path)) ?? new VaultFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return; }
        if (fresh.Master?.Salt != _file.Master?.Salt || fresh.Master?.Check != _file.Master?.Check) ForgetKey();
        _file = fresh;
        _stamp = stamp;
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
        _stamp = Stamp();
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
        /// <summary>Clé du mot de passe maître, chiffrée avec le code de secours ; null sans code. Remise à zéro à chaque changement de mot de passe maître.</summary>
        public RecoveryInfo? Recovery { get; set; }
    }

    private sealed class RecoveryInfo
    {
        public string Salt { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Wrapped { get; set; } = string.Empty;
    }
}
