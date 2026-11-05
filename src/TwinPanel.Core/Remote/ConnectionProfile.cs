// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Remote;

/// <summary>Protocole d'une connexion. Un seul choix dans l'interface : le chiffrement fait partie du protocole.</summary>
public enum RemoteProtocol
{
    Sftp,
    Scp,
    Ftp,
    FtpsExplicit,
    FtpsImplicit,
    WebDavHttps,
    WebDavHttp,
}

/// <summary>Façon de s'authentifier.</summary>
public enum AuthMode
{
    /// <summary>Mot de passe (et, en SSH, « clavier interactif » avec le même mot de passe).</summary>
    Password,
    /// <summary>Clé privée du trousseau.</summary>
    Key,
    /// <summary>Clé puis mot de passe (serveurs qui exigent les deux).</summary>
    KeyAndPassword,
    /// <summary>FTP anonyme.</summary>
    Anonymous,
}

public enum ProxyKind { None, Http, Socks4, Socks5 }

public enum FtpDataMode { AutoPassive, Passive, Active }

public enum TlsMinimum { Tls12, Tls13 }

public static class RemoteProtocols
{
    public static int DefaultPort(RemoteProtocol p) => p switch
    {
        RemoteProtocol.Sftp or RemoteProtocol.Scp => 22,
        RemoteProtocol.Ftp or RemoteProtocol.FtpsExplicit => 21,
        RemoteProtocol.FtpsImplicit => 990,
        RemoteProtocol.WebDavHttps => 443,
        RemoteProtocol.WebDavHttp => 80,
        _ => 0,
    };

    /// <summary>Préfixe des chemins affichés dans les panneaux (sftp://nom/…).</summary>
    public static string Scheme(RemoteProtocol p) => p switch
    {
        RemoteProtocol.Sftp => "sftp",
        RemoteProtocol.Scp => "scp",
        RemoteProtocol.Ftp => "ftp",
        RemoteProtocol.FtpsExplicit or RemoteProtocol.FtpsImplicit => "ftps",
        RemoteProtocol.WebDavHttps => "davs",
        RemoteProtocol.WebDavHttp => "dav",
        _ => "remote",
    };

    public static string Label(RemoteProtocol p) => p switch
    {
        RemoteProtocol.Sftp => "SFTP (SSH)",
        RemoteProtocol.Scp => "SCP (SSH)",
        RemoteProtocol.Ftp => L.T("FTP (sans chiffrement)"),
        RemoteProtocol.FtpsExplicit => L.T("FTPS explicite (FTP + TLS)"),
        RemoteProtocol.FtpsImplicit => L.T("FTPS implicite"),
        RemoteProtocol.WebDavHttps => L.T("WebDAV sécurisé (HTTPS)"),
        RemoteProtocol.WebDavHttp => L.T("WebDAV sans chiffrement (HTTP)"),
        _ => p.ToString(),
    };

    public static bool IsSsh(RemoteProtocol p) => p is RemoteProtocol.Sftp or RemoteProtocol.Scp;
    public static bool IsFtp(RemoteProtocol p) => p is RemoteProtocol.Ftp or RemoteProtocol.FtpsExplicit or RemoteProtocol.FtpsImplicit;
    public static bool IsWebDav(RemoteProtocol p) => p is RemoteProtocol.WebDavHttps or RemoteProtocol.WebDavHttp;
    public static bool IsTls(RemoteProtocol p) => p is RemoteProtocol.FtpsExplicit or RemoteProtocol.FtpsImplicit or RemoteProtocol.WebDavHttps;

    /// <summary>Vrai si identifiant et mot de passe circulent en clair sur le réseau.</summary>
    public static bool IsUnencrypted(RemoteProtocol p) => p is RemoteProtocol.Ftp or RemoteProtocol.WebDavHttp;
}

/// <summary>
/// Une connexion enregistrée dans le gestionnaire. Ne contient AUCUN secret :
/// mots de passe et phrases de passe sont dans le coffre (<see cref="SecretVault"/>).
/// </summary>
public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>Groupe d'affichage (« Maison », « Clients/Durand »…). Vide : sans groupe.</summary>
    public string Group { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    /// <summary>Couleur d'étiquette (#RRGGBB), vide pour aucune.</summary>
    public string Color { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    /// <summary>Texte du bouton dans la barre des lecteurs (vide : le nom de la connexion).</summary>
    public string ButtonLabel { get; set; } = string.Empty;

    /// <summary>Ce qui s'affiche sur le bouton de la barre des lecteurs.</summary>
    public string DisplayButtonLabel => string.IsNullOrWhiteSpace(ButtonLabel) ? Name : ButtonLabel.Trim();

    // ---- Général
    public RemoteProtocol Protocol { get; set; } = RemoteProtocol.Sftp;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    /// <summary>Dossier distant ouvert à la connexion (vide : dossier personnel / racine).</summary>
    public string RemoteDirectory { get; set; } = string.Empty;
    /// <summary>Dossier local ouvert dans l'autre panneau à la connexion (vide : on n'y touche pas).</summary>
    public string LocalDirectory { get; set; } = string.Empty;
    /// <summary>WebDAV : adresse du service sur le serveur (ex. « /remote.php/dav/files/utilisateur » pour Nextcloud).</summary>
    public string WebDavRoot { get; set; } = "/";

    // ---- Authentification
    /// <summary>Identité du trousseau à utiliser (identifiant + mot de passe et/ou clé). Prioritaire si renseignée.</summary>
    public Guid? IdentityId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public AuthMode Auth { get; set; } = AuthMode.Password;
    /// <summary>Le mot de passe est enregistré dans le coffre (sinon il est demandé à chaque connexion).</summary>
    public bool SavePassword { get; set; }
    /// <summary>Clé du trousseau (modes Key et KeyAndPassword).</summary>
    public Guid? KeyId { get; set; }
    /// <summary>
    /// Connexion restaurée sans sa clé (une sauvegarde n'en contient jamais) : description de la clé attendue,
    /// « nom (empreinte) ». Vide dès qu'une clé est choisie. Tant qu'elle est là, la connexion explique quoi faire au lieu de se connecter.
    /// </summary>
    public string MissingKey { get; set; } = string.Empty;

    // ---- Sécurité
    /// <summary>FTP / WebDAV non chiffrés : l'utilisateur a accepté le risque pour cette connexion.</summary>
    public bool AcceptUnencrypted { get; set; }
    public TlsMinimum TlsMinimum { get; set; } = TlsMinimum.Tls12;
    /// <summary>Certificat client (.pfx / .p12) pour FTPS ou WebDAV ; son mot de passe est dans le coffre.</summary>
    public string ClientCertificatePath { get; set; } = string.Empty;
    public bool SaveClientCertificatePassword { get; set; }

    // ---- Avancé
    /// <summary>Connexion SSH enregistrée servant de rebond (SFTP / SCP).</summary>
    public Guid? JumpHostId { get; set; }
    public ProxyKind Proxy { get; set; } = ProxyKind.None;
    public string ProxyHost { get; set; } = string.Empty;
    public int ProxyPort { get; set; } = 1080;
    public string ProxyUser { get; set; } = string.Empty;
    public bool SaveProxyPassword { get; set; }
    public FtpDataMode FtpDataMode { get; set; } = FtpDataMode.AutoPassive;
    /// <summary>Encodage des noms sur FTP : "auto", "utf-8", "windows-1252", "iso-8859-1".</summary>
    public string FtpEncoding { get; set; } = "auto";
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>Maintien de connexion en secondes (0 : désactivé).</summary>
    public int KeepAliveSeconds { get; set; } = 30;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedUtc { get; set; }

    /// <summary>Copie complète (pour « Dupliquer » ou pour annuler une modification).</summary>
    public ConnectionProfile Clone(bool newId = false)
    {
        var c = (ConnectionProfile)MemberwiseClone();
        c.Tags = [.. Tags];
        if (newId) { c.Id = Guid.NewGuid(); c.CreatedUtc = DateTime.UtcNow; c.LastUsedUtc = null; }
        return c;
    }

    /// <summary>Résumé d'une ligne : « root@192.168.0.20:22 ».</summary>
    public string Summary
    {
        get
        {
            var user = UserName.Length > 0 && Auth != AuthMode.Anonymous ? UserName + "@" : string.Empty;
            var port = Port != RemoteProtocols.DefaultPort(Protocol) ? $":{Port}" : string.Empty;
            return $"{user}{Host}{port}";
        }
    }

    /// <summary>Erreurs bloquantes avant l'enregistrement ou la connexion (liste vide : tout va bien).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add(L.T("Donne un nom à la connexion."));
        if (string.IsNullOrWhiteSpace(Host)) errors.Add(L.T("L'hôte (adresse du serveur) est obligatoire."));
        else if (Host.Any(char.IsWhiteSpace) || Host.Contains('/') || Host.Contains('\\'))
            errors.Add(L.T("L'hôte ne doit contenir ni espace ni barre oblique (seulement le nom ou l'adresse IP)."));
        if (Port is < 1 or > 65535) errors.Add(L.T("Le port doit être compris entre 1 et 65535."));
        if (Auth == AuthMode.Anonymous && !RemoteProtocols.IsFtp(Protocol)) errors.Add(L.T("La connexion anonyme n'existe qu'en FTP."));
        if (Auth is AuthMode.Key or AuthMode.KeyAndPassword && !RemoteProtocols.IsSsh(Protocol))
            errors.Add(L.T("Les clés privées ne servent qu'en SFTP et SCP."));
        if (Auth is AuthMode.Key or AuthMode.KeyAndPassword && KeyId is null && IdentityId is null && MissingKey.Length == 0)
            errors.Add(L.T("Choisis une clé du trousseau (onglet Authentification)."));
        if (IdentityId is null && Auth != AuthMode.Anonymous && string.IsNullOrWhiteSpace(UserName))
            errors.Add(L.T("L'identifiant est obligatoire (ou choisis une identité du trousseau)."));
        if (Proxy != ProxyKind.None && (string.IsNullOrWhiteSpace(ProxyHost) || ProxyPort is < 1 or > 65535))
            errors.Add(L.T("Le proxy doit avoir une adresse et un port valides."));
        if (JumpHostId is not null && !RemoteProtocols.IsSsh(Protocol)) errors.Add(L.T("Le serveur de rebond ne sert qu'en SFTP et SCP."));
        if (JumpHostId == Id) errors.Add(L.T("Une connexion ne peut pas être son propre rebond."));
        if (TimeoutSeconds is < 5 or > 600) errors.Add(L.T("Le délai d'attente doit être entre 5 et 600 secondes."));
        return errors;
    }
}

/// <summary>Clé privée SSH du trousseau. La clé elle-même (et sa phrase de passe) sont dans le coffre.</summary>
public sealed class KeychainKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>« ssh-ed25519 », « ssh-rsa », « ecdsa-sha2-nistp256 »…</summary>
    public string KeyType { get; set; } = string.Empty;
    /// <summary>Empreinte « SHA256:… » de la clé publique.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Ligne à mettre dans ~/.ssh/authorized_keys du serveur.</summary>
    public string PublicKey { get; set; } = string.Empty;
    public bool HasPassphrase { get; set; }
    public bool SavePassphrase { get; set; }
    /// <summary>Fichier d'origine (information seulement : le contenu est copié dans le coffre).</summary>
    public string ImportedFrom { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Identité réutilisable (comme dans Termius) : un identifiant, un mot de passe et/ou une clé, partagés par plusieurs connexions.</summary>
public sealed class KeychainIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool SavePassword { get; set; }
    public Guid? KeyId { get; set; }
}

/// <summary>Contenu du fichier connections.json.</summary>
public sealed class ConnectionDocument
{
    public int Version { get; set; } = 1;
    public List<ConnectionProfile> Profiles { get; set; } = [];
    public List<KeychainKey> Keys { get; set; } = [];
    public List<KeychainIdentity> Identities { get; set; } = [];
}
