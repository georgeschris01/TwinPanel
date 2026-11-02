// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Remote;

/// <summary>Un élément d'un dossier distant.</summary>
/// <param name="Mode">Droits Unix sur 12 bits (-1 : inconnus) ; Owner / Group : noms donnés par le serveur (FTP), sinon vides.</param>
public sealed record RemoteItem(string Name, string FullPath, bool IsDirectory, long Size, DateTime Modified, bool IsLink = false, string Permissions = "",
    int Mode = -1, string Owner = "", string Group = "", int Uid = -1, int Gid = -1);

/// <summary>Résultat d'un déplacement fait par le serveur lui-même (Error null et Moved faux : pas possible sur ce serveur).</summary>
public sealed record ServerMoveResult(bool Moved, string? Error)
{
    public static readonly ServerMoveResult Done = new(true, null);
    public static readonly ServerMoveResult NotAvailable = new(false, null);
}

/// <summary>Empreinte calculée par le serveur (Available faux : il ne sait pas le faire ; Hash null : fichier illisible).</summary>
public sealed record ServerHashResult(bool Available, string? Hash)
{
    /// <summary>Vérifie que la commande existe vraiment : une erreur sur un fichier ne dit pas si c'est lui ou la commande.</summary>
    public const string ProbeCommand = "printf '' | sha256sum";
    private const string EmptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const int HashLength = 64;

    public static readonly ServerHashResult NotAvailable = new(false, null);

    public static bool IsProbeOk(int? exitStatus, string? output) => exitStatus == 0 && ParseHash(output) == EmptyHash;

    public static ServerHashResult From(int? exitStatus, string? output) => new(true, exitStatus == 0 ? ParseHash(output) : null);

    /// <summary>« empreinte  nom » ; une barre oblique précède l'empreinte quand le nom contient un caractère spécial.</summary>
    public static string? ParseHash(string? output)
    {
        var text = (output ?? string.Empty).TrimStart().TrimStart('\\');
        if (text.Length < HashLength || !text[..HashLength].All(Uri.IsHexDigit)) return null;
        return text[..HashLength].ToLowerInvariant();
    }
}

/// <summary>Système de fichiers distant capable de déplacer / copier par une commande du serveur (SFTP + « mv » / « cp »).</summary>
public interface IServerSideMove
{
    Task<ServerMoveResult> TryMoveOnServerAsync(string from, string to, CancellationToken ct);
    /// <summary>Copie par le serveur lui-même (rien ne passe par le PC) ; la cible ne doit pas exister.</summary>
    Task<ServerMoveResult> TryCopyOnServerAsync(string from, string to, CancellationToken ct);
    /// <summary>Taille réelle d'un dossier et de son contenu, demandée au serveur ; null s'il ne sait pas répondre.</summary>
    Task<long?> MeasureOnServerAsync(string path, CancellationToken ct);
    /// <summary>SHA-256 d'un fichier calculé par le serveur (« sha256sum », lecture seule) : rien ne passe par le réseau.</summary>
    Task<ServerHashResult> TryHashOnServerAsync(string path, CancellationToken ct);
}

/// <summary>Erreur côté serveur ou réseau, avec un message en français compréhensible.</summary>
public sealed class RemoteException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>
/// Opérations de base sur un serveur (SFTP, SCP, FTP, FTPS, WebDAV). Les chemins sont de style Unix (« /home/x »).
/// Les opérations récursives (copie de dossiers, suppression) sont écrites une seule fois dans <see cref="RemoteTransfer"/>.
/// Une instance n'est pas prévue pour des appels simultanés : le gestionnaire de sessions ouvre une connexion par tâche.
/// </summary>
public interface IRemoteFileSystem : Rights.IRightsTarget, IAsyncDisposable
{
    RemoteProtocol Protocol { get; }
    /// <summary>Dossier où l'on arrive par défaut (dossier personnel en SSH, « / » ailleurs).</summary>
    string HomeDirectory { get; }
    bool IsConnected { get; }

    // ListAsync, droits et propriétaire : voir IRightsTarget.
    /// <summary>Informations sur un chemin ; null s'il n'existe pas.</summary>
    Task<RemoteItem?> StatAsync(string path, CancellationToken ct);
    /// <summary>Lit le fichier distant dans « target ». Le flux cible gère progression, pause et annulation.</summary>
    Task DownloadAsync(string path, Stream target, CancellationToken ct);
    /// <summary>Écrit « source » dans le fichier distant (remplacé s'il existe).</summary>
    Task UploadAsync(Stream source, string path, CancellationToken ct);
    /// <summary>Ce protocole sait lire un fichier à partir d'une position.</summary>
    bool CanResumeDownload { get; }
    /// <summary>Ce protocole sait compléter un fichier commencé.</summary>
    bool CanResumeUpload { get; }
    /// <summary>Lit le fichier distant à partir de « offset », jusqu'à sa fin.</summary>
    Task DownloadFromAsync(string path, Stream target, long offset, CancellationToken ct);
    /// <summary>Complète le fichier distant à partir de « offset » ; « source » est déjà placé au bon endroit.</summary>
    Task AppendAsync(Stream source, string path, long offset, CancellationToken ct);
    Task CreateDirectoryAsync(string path, CancellationToken ct);
    Task DeleteFileAsync(string path, CancellationToken ct);
    /// <summary>Supprime un dossier VIDE.</summary>
    Task DeleteDirectoryAsync(string path, CancellationToken ct);
    /// <summary>Renomme / déplace sur le serveur ; overwrite remplace une cible existante.</summary>
    Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct);
    /// <summary>Change la date de modification (si le protocole le permet ; sinon ne fait rien).</summary>
    Task SetModifiedAsync(string path, DateTime modified, CancellationToken ct);
    /// <summary>Droits Unix écrits en chiffres octaux (755) ; voir aussi IRightsTarget.SetModeAsync (valeur réelle).</summary>
    Task SetPermissionsAsync(string path, int mode, CancellationToken ct);
}

/// <summary>Chemins distants (toujours avec « / »).</summary>
public static class RemotePath
{
    public static string Combine(string dir, string name) =>
        dir.EndsWith('/') ? dir + name : dir + "/" + name;

    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        var parts = new List<string>();
        foreach (var p in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (p == ".") continue;
            if (p == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(p);
        }
        return "/" + string.Join('/', parts);
    }

    /// <summary>Dossier parent ; null à la racine.</summary>
    public static string? Parent(string path)
    {
        var n = Normalize(path);
        if (n == "/") return null;
        int i = n.LastIndexOf('/');
        return i <= 0 ? "/" : n[..i];
    }

    public static string FileName(string path)
    {
        var n = Normalize(path);
        return n == "/" ? string.Empty : n[(n.LastIndexOf('/') + 1)..];
    }

    /// <summary>Nom reçu d'un serveur acceptable comme simple nom (pas de chemin caché dedans).</summary>
    public static bool IsSafeName(string name) =>
        name.Length > 0 && name != "." && name != ".." && !name.Contains('/') && !name.Contains('\0');

    private static readonly HashSet<string> WindowsReserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Le nom peut-il exister tel quel sur un disque Windows ? (sinon le fichier est signalé et ignoré au téléchargement)</summary>
    public static bool IsValidWindowsName(string name)
    {
        if (!IsSafeName(name) || name.EndsWith('.') || name.EndsWith(' ')) return false;
        if (name.IndexOfAny(['<', '>', ':', '"', '/', '\\', '|', '?', '*']) >= 0 || name.Any(c => c < 32)) return false;
        var stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        return !WindowsReserved.Contains(stem);
    }
}

/// <summary>
/// Chemin affiché dans un panneau : « sftp://chrypox/home/utilisateur ».
/// « chrypox » est le nom (court) d'une session ouverte, pas forcément l'hôte réel.
/// </summary>
public readonly record struct PanelRemotePath(string Scheme, string Session, string Path)
{
    public override string ToString() => $"{Scheme}://{Session}{(Path == "/" ? "/" : Path)}";

    public static bool TryParse(string? text, out PanelRemotePath result)
    {
        result = default;
        if (string.IsNullOrEmpty(text)) return false;
        int sep = text.IndexOf("://", StringComparison.Ordinal);
        if (sep <= 0) return false;
        var scheme = text[..sep].ToLowerInvariant();
        if (scheme is not ("sftp" or "scp" or "ftp" or "ftps" or "dav" or "davs")) return false;
        var rest = text[(sep + 3)..].Replace('\\', '/');
        int slash = rest.IndexOf('/');
        var session = slash < 0 ? rest : rest[..slash];
        if (session.Length == 0) return false;
        result = new PanelRemotePath(scheme, session, RemotePath.Normalize(slash < 0 ? "/" : rest[slash..]));
        return true;
    }

    public PanelRemotePath WithPath(string path) => this with { Path = RemotePath.Normalize(path) };
}

/// <summary>Connexion rapide : « sftp://utilisateur@serveur:2222/var/www », « user@hôte », « ftps://… », « https://… » (WebDAV).</summary>
public static class QuickConnect
{
    public static bool TryParse(string text, out ConnectionProfile profile, out string error)
    {
        profile = new ConnectionProfile();
        error = string.Empty;
        var s = text.Trim();
        if (s.Length == 0) { error = L.T("Tape une adresse, par exemple sftp://utilisateur@serveur"); return false; }

        var protocol = RemoteProtocol.Sftp;
        int sep = s.IndexOf("://", StringComparison.Ordinal);
        if (sep > 0)
        {
            var scheme = s[..sep].ToLowerInvariant();
            RemoteProtocol? p = scheme switch
            {
                "sftp" or "ssh" => RemoteProtocol.Sftp,
                "scp" => RemoteProtocol.Scp,
                "ftp" => RemoteProtocol.Ftp,
                "ftps" or "ftpes" => RemoteProtocol.FtpsExplicit,
                "ftpsi" => RemoteProtocol.FtpsImplicit,
                "https" or "davs" or "webdavs" => RemoteProtocol.WebDavHttps,
                "http" or "dav" or "webdav" => RemoteProtocol.WebDavHttp,
                _ => null,
            };
            if (p is null) { error = L.F("Protocole inconnu : {0}", scheme); return false; }
            protocol = p.Value;
            s = s[(sep + 3)..];
        }

        string path = string.Empty;
        int slash = s.IndexOf('/');
        if (slash >= 0) { path = s[slash..]; s = s[..slash]; }
        string user = string.Empty;
        int at = s.LastIndexOf('@');
        if (at >= 0) { user = Uri.UnescapeDataString(s[..at]); s = s[(at + 1)..]; }
        if (user.Contains(':')) user = user[..user.IndexOf(':')]; // un mot de passe dans l'adresse n'est jamais repris
        var (host, port) = ConnectionImport.SplitHostPort(s, RemoteProtocols.DefaultPort(protocol));
        if (host.Length == 0) { error = L.T("Il manque le nom du serveur."); return false; }

        profile.Name = host;
        profile.Protocol = protocol;
        profile.Host = host;
        profile.Port = port;
        profile.UserName = user;
        profile.Auth = user.Length == 0 && RemoteProtocols.IsFtp(protocol) ? AuthMode.Anonymous : AuthMode.Password;
        if (RemoteProtocols.IsWebDav(protocol)) profile.WebDavRoot = path.Length > 0 ? Uri.UnescapeDataString(path) : "/";
        else profile.RemoteDirectory = Uri.UnescapeDataString(path);
        var errors = profile.Validate().Where(e => !e.Contains("identifiant", StringComparison.Ordinal)).ToList();
        if (user.Length == 0 && profile.Auth != AuthMode.Anonymous) errors.Add(L.T("Indique l'identifiant : utilisateur@serveur"));
        if (errors.Count > 0) { error = string.Join(Environment.NewLine, errors); return false; }
        return true;
    }
}
