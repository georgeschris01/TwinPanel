// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Liste des connexions, des clés et des identités (fichier connections.json, SANS secrets),
/// plus le coffre des secrets et les serveurs connus, rangés à côté.
/// </summary>
public sealed class ConnectionStore
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private ConnectionDocument _doc = new();

    public ConnectionStore(string directory, IUserDataProtector? protector = null, string? openSshKnownHosts = null)
    {
        Directory = directory;
        MakePrivate(directory);
        _path = System.IO.Path.Combine(directory, "connections.json");
        Vault = new SecretVault(System.IO.Path.Combine(directory, "secrets.dat"), protector);
        KnownHosts = new KnownHosts(System.IO.Path.Combine(directory, "known_hosts.json"), openSshKnownHosts);
        Load();
    }

    /// <summary>Sous Linux, un nouveau dossier est lisible par tous les comptes du PC : la liste des serveurs n'a pas à l'être.</summary>
    private static void MakePrivate(string directory)
    {
        if (OperatingSystem.IsWindows()) return;
        const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        System.IO.Directory.CreateDirectory(directory, OwnerOnly);
        System.IO.File.SetUnixFileMode(directory, OwnerOnly);
    }

    /// <summary>%APPDATA%\TwinPanel (même dossier que settings.json).</summary>
    public static string DefaultDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TwinPanel");

    public string Directory { get; }
    public SecretVault Vault { get; }
    public KnownHosts KnownHosts { get; }

    public IReadOnlyList<ConnectionProfile> Profiles => _doc.Profiles;
    public IReadOnlyList<KeychainKey> Keys => _doc.Keys;
    public IReadOnlyList<KeychainIdentity> Identities => _doc.Identities;

    public event EventHandler? Changed;

    public ConnectionProfile? FindProfile(Guid? id) => id is null ? null : _doc.Profiles.FirstOrDefault(p => p.Id == id);
    public KeychainKey? FindKey(Guid? id) => id is null ? null : _doc.Keys.FirstOrDefault(k => k.Id == id);
    public KeychainIdentity? FindIdentity(Guid? id) => id is null ? null : _doc.Identities.FirstOrDefault(i => i.Id == id);

    /// <summary>Groupes existants, triés (pour la liste déroulante).</summary>
    public IReadOnlyList<string> Groups =>
        _doc.Profiles.Select(p => p.Group).Where(g => g.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    // ---- Connexions

    /// <summary>Serveurs de rebond à la suite, au plus. Au-delà, c'est presque sûrement une erreur de fiche.</summary>
    public const int MaxJumps = 8;

    /// <summary>Les rebonds de cette connexion, du plus proche d'elle au premier à joindre.</summary>
    public IReadOnlyList<ConnectionProfile> JumpChain(ConnectionProfile profile)
    {
        var chain = new List<ConnectionProfile>();
        var seen = new HashSet<Guid> { profile.Id };
        var current = profile;
        while (current.JumpHostId is { } id)
        {
            var next = FindProfile(id) ?? throw new RemoteException(L.T("Le serveur de rebond de cette connexion n'existe plus."));
            if (!RemoteProtocols.IsSsh(next.Protocol)) throw new RemoteException(L.T("Le serveur de rebond doit être une connexion SSH (SFTP ou SCP)."));
            chain.Add(next);
            if (!seen.Add(id)) throw new RemoteException(L.F("Ces connexions passent l'une par l'autre, en boucle : {0}.", string.Join(" → ", chain.Select(p => p.Name))));
            if (chain.Count > MaxJumps) throw new RemoteException(L.F("Trop de serveurs de rebond à la suite ({0} au plus).", MaxJumps));
            current = next;
        }
        return chain;
    }

    /// <summary>Cette connexion peut servir de rebond à « user » : pas de boucle, et une chaîne de longueur permise.</summary>
    public bool CanJumpThrough(ConnectionProfile candidate, Guid? user)
    {
        var seen = new HashSet<Guid>();
        for (var current = candidate; current is not null; current = FindProfile(current.JumpHostId))
            if (current.Id == user || !seen.Add(current.Id)) return false;
        return seen.Count <= MaxJumps;
    }

    /// <summary>Ajoute ou remplace (même identifiant) une connexion.</summary>
    public void SaveProfile(ConnectionProfile profile)
    {
        var errors = profile.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(Environment.NewLine, errors));
        if (profile.KeyId is not null || profile.IdentityId is not null || profile.Auth is not (AuthMode.Key or AuthMode.KeyAndPassword))
            profile.MissingKey = string.Empty;
        Replace(_doc.Profiles, profile, p => p.Id == profile.Id);
        if (!profile.SavePassword) Vault.Remove(SecretIds.Password(profile.Id));
        if (!profile.SaveProxyPassword) Vault.Remove(SecretIds.ProxyPassword(profile.Id));
        if (!profile.SaveClientCertificatePassword) Vault.Remove(SecretIds.CertificatePassword(profile.Id));
        Save();
    }

    public void DeleteProfile(Guid id)
    {
        _doc.Profiles.RemoveAll(p => p.Id == id);
        foreach (var p in _doc.Profiles.Where(p => p.JumpHostId == id)) p.JumpHostId = null;
        Vault.RemoveAllFor(id);
        Save();
    }

    public void MarkUsed(Guid id)
    {
        if (FindProfile(id) is { } p) { p.LastUsedUtc = DateTime.UtcNow; Save(); }
    }

    /// <summary>Nom libre dérivé de « name » (« chrypox », « chrypox (2) »…).</summary>
    public string UniqueName(string name, Guid? except = null)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? L.T("Nouvelle connexion") : name.Trim();
        var candidate = baseName;
        for (int i = 2; _doc.Profiles.Any(p => p.Id != except && string.Equals(p.Name, candidate, StringComparison.CurrentCultureIgnoreCase)); i++)
            candidate = $"{baseName} ({i})";
        return candidate;
    }

    // ---- Trousseau

    public void SaveKey(KeychainKey key)
    {
        Replace(_doc.Keys, key, k => k.Id == key.Id);
        if (!key.SavePassphrase) Vault.Remove(SecretIds.KeyPassphrase(key.Id));
        Save();
    }

    /// <summary>Supprime une clé ; les connexions et identités qui l'utilisaient n'ont plus de clé.</summary>
    public void DeleteKey(Guid id)
    {
        _doc.Keys.RemoveAll(k => k.Id == id);
        foreach (var p in _doc.Profiles.Where(p => p.KeyId == id)) p.KeyId = null;
        foreach (var i in _doc.Identities.Where(i => i.KeyId == id)) i.KeyId = null;
        Vault.RemoveAllFor(id);
        Save();
    }

    public void SaveIdentity(KeychainIdentity identity)
    {
        Replace(_doc.Identities, identity, i => i.Id == identity.Id);
        if (!identity.SavePassword) Vault.Remove(SecretIds.IdentityPassword(identity.Id));
        Save();
    }

    public void DeleteIdentity(Guid id)
    {
        _doc.Identities.RemoveAll(i => i.Id == id);
        foreach (var p in _doc.Profiles.Where(p => p.IdentityId == id)) p.IdentityId = null;
        Vault.RemoveAllFor(id);
        Save();
    }

    public IEnumerable<ConnectionProfile> ProfilesUsingKey(Guid keyId) =>
        _doc.Profiles.Where(p => p.KeyId == keyId || (p.IdentityId is { } i && FindIdentity(i)?.KeyId == keyId));

    // ---- Synchronisation entre machines

    /// <summary>Copie de la liste, sans ce qui ne vaut que sur ce PC (chemins locaux).</summary>
    public ConnectionDocument CopyForSync()
    {
        var copy = JsonSerializer.Deserialize<ConnectionDocument>(JsonSerializer.Serialize(_doc, Json), Json)!;
        foreach (var p in copy.Profiles) p.LocalDirectory = p.ClientCertificatePath = string.Empty;
        foreach (var k in copy.Keys) k.ImportedFrom = string.Empty;
        return copy;
    }

    /// <summary>
    /// Reçoit la liste d'une autre machine : même identifiant, ou même serveur créé des deux côtés, la fiche reçue
    /// remplace celle d'ici, qui garde ses chemins locaux. Ce qui n'existe qu'ici reste. Les secrets passent d'abord :
    /// si le coffre les refuse, rien d'autre n'a changé. Retourne le nombre de connexions reçues.
    /// </summary>
    public int MergeSynced(ConnectionDocument incoming, IReadOnlyDictionary<string, string> secrets)
    {
        foreach (var (id, value) in secrets) Vault.Set(id, value);
        foreach (var key in incoming.Keys)
        {
            Replace(_doc.Keys, key, k => k.Id == key.Id);
            if (!key.SavePassphrase) Vault.Remove(SecretIds.KeyPassphrase(key.Id));
        }
        foreach (var identity in incoming.Identities)
        {
            Replace(_doc.Identities, identity, i => i.Id == identity.Id);
            if (!identity.SavePassword) Vault.Remove(SecretIds.IdentityPassword(identity.Id));
        }
        int count = 0;
        foreach (var profile in incoming.Profiles)
        {
            if (profile.Validate().Count > 0) continue;
            var here = _doc.Profiles.FirstOrDefault(p => p.Id == profile.Id) ?? _doc.Profiles.FirstOrDefault(p => IsSameServer(p, profile));
            if (here is not null)
            {
                profile.LocalDirectory = here.LocalDirectory;
                profile.ClientCertificatePath = here.ClientCertificatePath;
                if (here.Id != profile.Id)
                {
                    _doc.Profiles.Remove(here);
                    foreach (var p in _doc.Profiles.Where(p => p.JumpHostId == here.Id)) p.JumpHostId = profile.Id;
                    Vault.RemoveAllFor(here.Id);
                }
            }
            profile.Name = UniqueName(profile.Name, profile.Id);
            Replace(_doc.Profiles, profile, p => p.Id == profile.Id);
            if (!profile.SavePassword) Vault.Remove(SecretIds.Password(profile.Id));
            if (!profile.SaveProxyPassword) Vault.Remove(SecretIds.ProxyPassword(profile.Id));
            if (!profile.SaveClientCertificatePassword) Vault.Remove(SecretIds.CertificatePassword(profile.Id));
            count++;
        }
        Save();
        return count;
    }

    private static bool IsSameServer(ConnectionProfile a, ConnectionProfile b) =>
        a.Protocol == b.Protocol && a.Port == b.Port && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    // ---- Export / import (sans secrets)

    /// <summary>Exporte des connexions (et les clés publiques / identités qu'elles utilisent) SANS aucun secret.</summary>
    public void Export(IEnumerable<ConnectionProfile> profiles, string file)
    {
        var list = profiles.Select(p => p.Clone()).ToList();
        var identityIds = list.Where(p => p.IdentityId is not null).Select(p => p.IdentityId!.Value).ToHashSet();
        var identities = _doc.Identities.Where(i => identityIds.Contains(i.Id)).ToList();
        var keyIds = list.Where(p => p.KeyId is not null).Select(p => p.KeyId!.Value)
            .Concat(identities.Where(i => i.KeyId is not null).Select(i => i.KeyId!.Value)).ToHashSet();
        var export = new ConnectionDocument
        {
            Profiles = list,
            Identities = identities,
            // Clés : seulement la description et la partie publique (la clé privée reste dans le coffre).
            Keys = _doc.Keys.Where(k => keyIds.Contains(k.Id)).ToList(),
        };
        File.WriteAllText(file, JsonSerializer.Serialize(export, Json));
    }

    /// <summary>Lit un fichier exporté par TwinPanel (rien n'est encore ajouté).</summary>
    public static ConnectionDocument ReadExport(string file) =>
        JsonSerializer.Deserialize<ConnectionDocument>(File.ReadAllText(file), Json)
        ?? throw new InvalidDataException(L.T("Fichier de connexions vide ou invalide."));

    /// <summary>
    /// Ajoute des connexions venues d'une sauvegarde (nouveaux identifiants, noms rendus uniques), sans aucun secret.
    /// keys / identities : celles décrites dans la sauvegarde (parties publiques), pour retrouver une clé déjà présente ici
    /// par son empreinte, ou dire laquelle manque. Une connexion par clé reste par clé : jamais changée en mot de passe en silence.
    /// </summary>
    public ImportReport AddImported(IEnumerable<ConnectionProfile> profiles, IEnumerable<KeychainKey>? keys = null, IEnumerable<KeychainIdentity>? identities = null)
    {
        var report = new ImportReport();
        var exportedKeys = (keys ?? []).ToList();
        var exportedIdentities = (identities ?? []).ToList();
        foreach (var p in profiles)
        {
            var copy = p.Clone(newId: true);
            copy.Name = UniqueName(copy.Name);
            // Identité absente d'ici : son identifiant et sa clé passent dans la connexion.
            if (copy.IdentityId is not null && FindIdentity(copy.IdentityId) is null)
            {
                var identity = exportedIdentities.FirstOrDefault(i => i.Id == copy.IdentityId);
                if (copy.UserName.Length == 0 && identity is not null) copy.UserName = identity.UserName;
                if (copy.KeyId is null && identity?.KeyId is not null) copy.KeyId = identity.KeyId;
                copy.IdentityId = null;
            }
            if (copy.KeyId is not null && FindKey(copy.KeyId) is null)
            {
                // Même clé déjà ici sous un autre identifiant (importée à part) : reconnue par son empreinte.
                var described = exportedKeys.FirstOrDefault(k => k.Id == copy.KeyId);
                var local = described is { Fingerprint.Length: > 0 }
                    ? _doc.Keys.FirstOrDefault(k => string.Equals(k.Fingerprint, described.Fingerprint, StringComparison.Ordinal)) : null;
                copy.KeyId = local?.Id;
                copy.MissingKey = local is not null ? string.Empty
                    : described is null ? L.T("clé inconnue")
                    : described.Fingerprint.Length > 0 ? $"{described.Name} ({described.Fingerprint})" : described.Name;
            }
            if (copy.Auth is AuthMode.Key or AuthMode.KeyAndPassword && copy.KeyId is null && copy.IdentityId is null && copy.MissingKey.Length == 0)
                copy.MissingKey = L.T("clé inconnue");
            if (FindProfile(copy.JumpHostId) is null) copy.JumpHostId = null;
            copy.SavePassword = copy.SaveProxyPassword = copy.SaveClientCertificatePassword = false;
            if (copy.Validate().Count > 0) { report.Rejected.Add(copy.Name); continue; }
            _doc.Profiles.Add(copy);
            report.Added.Add(copy.Name);
            if (copy.MissingKey.Length > 0) report.MissingKey.Add($"{copy.Name} : {copy.MissingKey}");
            if (copy.Auth is AuthMode.Password or AuthMode.KeyAndPassword) report.NeedPassword.Add(copy.Name);
        }
        if (report.Added.Count > 0) Save();
        return report;
    }

    /// <summary>
    /// Ajoute des connexions trouvées dans d'autres programmes (choisies par l'utilisateur).
    /// keyImporter reçoit un fichier de clé et rend la clé du trousseau (déjà ajoutée) ou null ;
    /// les mots de passe trouvés ne sont enregistrés (chiffrés) que si withPasswords.
    /// Retourne le nombre de connexions ajoutées.
    /// </summary>
    public int ImportCandidates(IEnumerable<ImportedConnection> candidates, bool withPasswords, Func<string, KeychainKey?> keyImporter)
    {
        var added = new List<(ImportedConnection Source, ConnectionProfile Profile)>();
        var keysByFile = new Dictionary<string, KeychainKey?>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in candidates)
        {
            var p = c.Profile.Clone(newId: true);
            p.Name = UniqueName(p.Name);
            p.SavePassword = false;
            if (c.KeyFile is { Length: > 0 } file)
            {
                if (!keysByFile.TryGetValue(file, out var key)) keysByFile[file] = key = keyImporter(file);
                p.KeyId = key?.Id;
                if (key is null && p.Auth is AuthMode.Key or AuthMode.KeyAndPassword) p.Auth = AuthMode.Password;
            }
            else if (p.Auth is AuthMode.Key or AuthMode.KeyAndPassword && p.KeyId is null) p.Auth = AuthMode.Password;
            if (p.Validate().Count > 0) continue;
            _doc.Profiles.Add(p);
            added.Add((c, p));
        }
        // Rebonds (ProxyJump) : par nom parmi les connexions importées ou existantes.
        foreach (var (c, p) in added.Where(a => a.Source.JumpHostName is not null))
        {
            var jump = added.Select(a => a.Profile).FirstOrDefault(x => x != p && (string.Equals(x.Name, c.JumpHostName, StringComparison.OrdinalIgnoreCase)
                                                                                     || string.Equals(x.Host, c.JumpHostName, StringComparison.OrdinalIgnoreCase)))
                       ?? _doc.Profiles.FirstOrDefault(x => x != p && string.Equals(x.Name, c.JumpHostName, StringComparison.OrdinalIgnoreCase));
            if (jump is not null && RemoteProtocols.IsSsh(jump.Protocol) && RemoteProtocols.IsSsh(p.Protocol)) p.JumpHostId = jump.Id;
        }
        if (added.Count > 0) Save();
        if (withPasswords)
        {
            foreach (var (c, p) in added.Where(a => !string.IsNullOrEmpty(a.Source.Password) && a.Profile.Auth is AuthMode.Password or AuthMode.KeyAndPassword))
            {
                Vault.Set(SecretIds.Password(p.Id), c.Password);
                p.SavePassword = true;
            }
            Save();
        }
        return added.Count;
    }

    /// <summary>Ajoute au trousseau une clé privée (texte), ou rend celle déjà présente (même empreinte).</summary>
    public KeychainKey AddKey(string name, string privateKeyText, string? passphrase, bool savePassphrase, string importedFrom = "")
    {
        var info = SshKeys.Inspect(privateKeyText, passphrase, name);
        if (_doc.Keys.FirstOrDefault(k => k.Fingerprint == info.Fingerprint) is { } existing) return existing;
        bool hasPass = !string.IsNullOrEmpty(passphrase);
        var key = new KeychainKey
        {
            Name = name,
            KeyType = info.KeyType,
            Fingerprint = info.Fingerprint,
            PublicKey = info.PublicKeyLine,
            HasPassphrase = hasPass,
            SavePassphrase = hasPass && savePassphrase,
            ImportedFrom = importedFrom,
        };
        Vault.Set(SecretIds.KeyMaterial(key.Id), privateKeyText);
        if (key.SavePassphrase) Vault.Set(SecretIds.KeyPassphrase(key.Id), passphrase);
        _doc.Keys.Add(key);
        Save();
        return key;
    }

    // ---- Interne

    private static void Replace<T>(List<T> list, T item, Predicate<T> same)
    {
        int i = list.FindIndex(same);
        if (i >= 0) list[i] = item; else list.Add(item);
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try { _doc = JsonSerializer.Deserialize<ConnectionDocument>(File.ReadAllText(_path), Json) ?? new ConnectionDocument(); }
        catch (JsonException)
        {
            File.Copy(_path, _path + ".illisible", overwrite: true);
            _doc = new ConnectionDocument();
        }
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_doc, Json));
        File.Move(tmp, _path, overwrite: true);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Bilan d'une restauration de connexions : noms ajoutés, refusés, à compléter.</summary>
public sealed class ImportReport
{
    public List<string> Added { get; } = [];
    /// <summary>Fiches incomplètes (sans hôte, sans identifiant…), pas ajoutées.</summary>
    public List<string> Rejected { get; } = [];
    /// <summary>Mot de passe demandé à la première connexion.</summary>
    public List<string> NeedPassword { get; } = [];
    /// <summary>« Connexion : clé attendue ».</summary>
    public List<string> MissingKey { get; } = [];
}
