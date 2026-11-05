// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Settings;

/// <summary>Phrase secrète fausse, ou fichier abîmé : les deux se voient de la même façon.</summary>
public sealed class SyncPassphraseException()
    : Exception(L.T("Phrase secrète incorrecte, ou fichier de synchronisation abîmé."));

/// <summary>Réglages qui ont du sens sur Windows comme sur Linux. Chemins, éditeurs et terminal restent sur chaque machine.</summary>
public sealed class SyncedSettings
{
    public string Language { get; set; } = string.Empty;
    public string Theme { get; set; } = "System";
    public Dictionary<string, string> Shortcuts { get; set; } = [];
    public List<SavedTerminalCommand> SavedCommands { get; set; } = [];
    public bool ColorNames { get; set; }
    public Dictionary<FileCategory, string> CategoryColors { get; set; } = [];
    public List<ColorRule> ColorRules { get; set; } = [];
    public List<string> CustomColors { get; set; } = [];
    public bool CustomDriveColors { get; set; }
    public Dictionary<string, string> DriveTypeColors { get; set; } = [];

    public static SyncedSettings From(AppSettings s) => new()
    {
        Language = s.Language,
        Theme = s.Theme,
        Shortcuts = s.Shortcuts,
        SavedCommands = s.Terminal.SavedCommands,
        ColorNames = s.Appearance.ColorNames,
        CategoryColors = s.Appearance.CategoryColors,
        ColorRules = s.Appearance.ColorRules,
        CustomColors = s.Appearance.CustomColors,
        CustomDriveColors = s.Appearance.CustomDriveColors,
        DriveTypeColors = s.Appearance.DriveTypeColors,
    };

    /// <summary>Remplace les réglages communs. Une commande enregistrée seulement ici est gardée.</summary>
    public void ApplyTo(AppSettings s)
    {
        s.Language = Language;
        s.Theme = Theme;
        s.Shortcuts = Shortcuts;
        s.Appearance.ColorNames = ColorNames;
        s.Appearance.CategoryColors = CategoryColors;
        s.Appearance.ColorRules = ColorRules;
        s.Appearance.CustomColors = CustomColors;
        s.Appearance.CustomDriveColors = CustomDriveColors;
        s.Appearance.DriveTypeColors = DriveTypeColors;
        var onlyHere = s.Terminal.SavedCommands.Where(c => !SavedCommands.Any(o => o.Name == c.Name && o.ServerId == c.ServerId));
        s.Terminal.SavedCommands = [.. SavedCommands, .. onlyHere];
    }
}

/// <summary>Parties d'une synchronisation ou d'une sauvegarde, choisies avant l'envoi et à la réception.</summary>
[Flags]
public enum TransferParts
{
    None = 0,
    Settings = 1,
    Connections = 2,
    KnownHosts = 4,
    Tabs = 8,
    All = Settings | Connections | KnownHosts | Tabs,
}

/// <summary>Contenu du fichier, une fois déchiffré.</summary>
public sealed class SyncPackage
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    /// <summary>Change à chaque envoi : c'est ainsi qu'on voit qu'une autre machine a écrit depuis.</summary>
    public Guid Revision { get; set; } = Guid.NewGuid();
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public string Machine { get; set; } = string.Empty;
    public SyncedSettings Settings { get; set; } = new();
    public ConnectionDocument Connections { get; set; } = new();
    public Dictionary<string, string> Secrets { get; set; } = [];
    public List<KnownSshHost> KnownHosts { get; set; } = [];
    public List<TrustedCertificate> Certificates { get; set; } = [];
    /// <summary>Faux si l'envoi ne les contenait pas (les fichiers d'avant ce choix contiennent tout).</summary>
    public bool HasSettings { get; set; } = true;
    public bool HasConnections { get; set; } = true;
    public bool HasKnownHosts { get; set; } = true;
    /// <summary>Onglets ouverts à l'envoi ; null s'ils n'ont pas été envoyés.</summary>
    public TransferredTabs? Tabs { get; set; }

    /// <summary>Ce que contient le fichier.</summary>
    [JsonIgnore]
    public TransferParts Parts =>
        (HasSettings ? TransferParts.Settings : 0) | (HasConnections ? TransferParts.Connections : 0)
        | (HasKnownHosts ? TransferParts.KnownHosts : 0) | (Tabs is { Count: > 0 } ? TransferParts.Tabs : 0);
}

/// <summary>Où en est cette machine : dossier choisi et dernier envoi vu. Rien de secret, et rien qui voyage.</summary>
public sealed class SyncState
{
    private const string FileName = "synchro.json";

    public string Folder { get; set; } = string.Empty;
    public Guid? LastRevision { get; set; }

    public static SyncState Load(string directory)
    {
        try { return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(Path.Combine(directory, FileName))) ?? new SyncState(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new SyncState(); }
    }

    public void Save(string directory)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, FileName);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this));
        File.Move(tmp, file, overwrite: true);
    }
}

/// <summary>
/// Configuration partagée entre plusieurs installations, dans un seul fichier chiffré posé dans un dossier
/// choisi par l'utilisateur. Tout est chiffré, y compris les adresses des serveurs : AES-256-GCM avec une clé tirée
/// d'une phrase secrète (PBKDF2-SHA256), que TwinPanel ne garde jamais. Une phrase perdue ne se récupère pas.
/// </summary>
public static class ConfigSync
{
    public const string FileName = "twinpanel-synchro.tpsync";
    public const int MinPassphraseLength = 12;
    private const string FormatName = "TwinPanel-synchro";
    private const int Iterations = 600_000;
    // Au-delà, ce n'est pas un fichier écrit par TwinPanel : on ne le laisse pas bloquer le programme.
    private const int MaxIterations = 10_000_000;

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static string FilePath(string folder) => Path.Combine(folder, FileName);

    public static bool Exists(string folder) => folder.Length > 0 && File.Exists(FilePath(folder));

    /// <summary>Rassemble ce qui voyage (tout, par défaut). Lève <see cref="VaultLockedException"/> si le coffre est verrouillé et que les connexions partent.</summary>
    public static SyncPackage Build(AppSettings settings, ConnectionStore store, TransferParts parts = TransferParts.All)
    {
        var package = new SyncPackage
        {
            Machine = Environment.MachineName,
            HasSettings = parts.HasFlag(TransferParts.Settings),
            HasConnections = parts.HasFlag(TransferParts.Connections),
            HasKnownHosts = parts.HasFlag(TransferParts.KnownHosts),
        };
        if (package.HasSettings) package.Settings = SyncedSettings.From(settings);
        if (package.HasKnownHosts)
        {
            package.KnownHosts = [.. store.KnownHosts.SshHosts];
            package.Certificates = [.. store.KnownHosts.Certificates];
        }
        if (parts.HasFlag(TransferParts.Tabs)) package.Tabs = TabTransfer.Capture(settings.Left, settings.Right, id => store.FindProfile(id));
        var secrets = new Dictionary<string, string>();
        void Take(string id) { if (store.Vault.Get(id) is { } value) secrets[id] = value; }
        // Commandes secrètes du terminal : leur texte voyage avec les réglages, chiffré comme les mots de passe.
        if (package.HasSettings)
            foreach (var c in settings.Terminal.SavedCommands.Where(c => c.Secret)) Take(TwinPanel.Core.Terminal.TerminalCommandSecrets.VaultId(c));
        package.Secrets = secrets;
        if (!package.HasConnections) return package;

        var connections = store.CopyForSync();
        foreach (var p in connections.Profiles)
        {
            Take(SecretIds.Password(p.Id));
            Take(SecretIds.ProxyPassword(p.Id));
            Take(SecretIds.CertificatePassword(p.Id));
        }
        foreach (var k in connections.Keys)
        {
            Take(SecretIds.KeyMaterial(k.Id));
            Take(SecretIds.KeyPassphrase(k.Id));
        }
        foreach (var i in connections.Identities) Take(SecretIds.IdentityPassword(i.Id));
        package.Connections = connections;
        return package;
    }

    /// <summary>Écrit le fichier d'un bloc : une coupure laisse l'ancien intact.</summary>
    public static void Write(string folder, string passphrase, SyncPackage package)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = DeriveKey(passphrase, salt, Iterations);
        var plain = JsonSerializer.SerializeToUtf8Bytes(package, Json);
        try
        {
            var envelope = new Envelope
            {
                Salt = Convert.ToBase64String(salt),
                Iterations = Iterations,
                Data = Convert.ToBase64String(SecretVault.AesSeal(key, plain)),
            };
            var file = FilePath(folder);
            var tmp = file + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(envelope), new UTF8Encoding(false));
                File.Move(tmp, file, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* il est chiffré : sans danger */ }
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Lit et déchiffre le fichier.</summary>
    public static SyncPackage Read(string folder, string passphrase)
    {
        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(FilePath(folder))); }
        catch (JsonException ex) { throw new InvalidDataException(L.T("Ce fichier n'est pas un fichier de synchronisation TwinPanel."), ex); }
        if (envelope is null || envelope.Format != FormatName || envelope.Iterations is < 1 or > MaxIterations)
            throw new InvalidDataException(L.T("Ce fichier n'est pas un fichier de synchronisation TwinPanel."));

        byte[]? key = null, plain = null;
        try
        {
            key = DeriveKey(passphrase, Convert.FromBase64String(envelope.Salt), envelope.Iterations);
            plain = SecretVault.AesOpen(key, Convert.FromBase64String(envelope.Data));
            var package = JsonSerializer.Deserialize<SyncPackage>(plain, Json) ?? throw new SyncPassphraseException();
            if (package.Version > SyncPackage.CurrentVersion)
                throw new InvalidDataException(L.T("Ce fichier vient d'une version plus récente de TwinPanel : mets d'abord celui-ci à jour."));
            return package;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException) { throw new SyncPassphraseException(); }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Connexions, clés, secrets et serveurs connus. Ce qui n'existe qu'ici est gardé. Retourne le nombre de connexions reçues.</summary>
    public static int ApplyConnections(SyncPackage package, ConnectionStore store, TransferParts parts = TransferParts.All)
    {
        int count = parts.HasFlag(TransferParts.Connections) && package.HasConnections ? store.MergeSynced(package.Connections, package.Secrets) : 0;
        if (parts.HasFlag(TransferParts.KnownHosts) && package.HasKnownHosts) store.KnownHosts.Merge(package.KnownHosts, package.Certificates);
        return count;
    }

    /// <summary>Range dans le coffre le texte des commandes secrètes reçues avec les réglages. Retourne leur nombre.</summary>
    public static int ApplyCommandSecrets(SyncPackage package, ConnectionStore store)
    {
        int count = 0;
        foreach (var c in package.Settings.SavedCommands.Where(c => c.Secret))
        {
            var id = TwinPanel.Core.Terminal.TerminalCommandSecrets.VaultId(c);
            if (!package.Secrets.TryGetValue(id, out var text)) continue;
            store.Vault.Set(id, text);
            count++;
        }
        return count;
    }

    /// <summary>Une commande secrète part avec les réglages : il faut alors le coffre ouvert.</summary>
    public static bool NeedsVault(AppSettings settings, TransferParts parts) =>
        parts.HasFlag(TransferParts.Connections)
        || (parts.HasFlag(TransferParts.Settings) && settings.Terminal.SavedCommands.Any(c => c.Secret));

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, 32);

    private sealed class Envelope
    {
        public string Format { get; set; } = FormatName;
        public string Salt { get; set; } = string.Empty;
        public int Iterations { get; set; }
        public string Data { get; set; } = string.Empty;
    }
}
