// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Rights;

/// <summary>
/// Droits Unix : 12 bits (setuid, setgid, sticky, puis rwx pour propriétaire, groupe, autres).
/// On travaille toujours avec la vraie valeur numérique (0755 = 493) ; « digits » désigne l'écriture décimale
/// des chiffres octaux (755) attendue par SSH.NET et FluentFTP.
/// </summary>
public static class UnixMode
{
    public const int Mask = 0xFFF; // 07777
    public const int SetUid = 0x800, SetGid = 0x400, Sticky = 0x200;

    /// <summary>Bit pour une personne (0 propriétaire, 1 groupe, 2 autres) et une action (0 lire, 1 écrire, 2 exécuter).</summary>
    public static int Bit(int who, int what) => 1 << ((2 - who) * 3 + (2 - what));

    /// <summary>« 0755 » (4 chiffres si un bit spécial est mis, sinon 3 : « 755 »).</summary>
    public static string ToOctal(int mode)
    {
        mode &= Mask;
        var s = Convert.ToString(mode, 8);
        return mode > 0x1FF ? s.PadLeft(4, '0') : s.PadLeft(3, '0');
    }

    /// <summary>Accepte « 755 », « 0755 », « 4755 » (1 à 4 chiffres de 0 à 7).</summary>
    public static bool TryParseOctal(string? text, out int mode)
    {
        mode = 0;
        var s = (text ?? string.Empty).Trim();
        if (s.Length is < 1 or > 4 || s.Any(c => c is < '0' or > '7')) return false;
        mode = Convert.ToInt32(s, 8);
        return true;
    }

    public static int ToDigits(int mode) => int.Parse(Convert.ToString(mode & Mask, 8), CultureInfo.InvariantCulture);

    public static int FromDigits(int digits) =>
        TryParseOctal(digits.ToString(CultureInfo.InvariantCulture), out var m) ? m : 0;

    /// <summary>« rwxr-sr-t » (s/S, t/T comme ls).</summary>
    public static string ToSymbolic(int mode)
    {
        var sb = new StringBuilder(9);
        for (int who = 0; who < 3; who++)
        {
            sb.Append((mode & Bit(who, 0)) != 0 ? 'r' : '-');
            sb.Append((mode & Bit(who, 1)) != 0 ? 'w' : '-');
            bool x = (mode & Bit(who, 2)) != 0;
            int special = who switch { 0 => SetUid, 1 => SetGid, _ => Sticky };
            char sp = who == 2 ? 't' : 's';
            sb.Append((mode & special) != 0 ? (x ? sp : char.ToUpperInvariant(sp)) : (x ? 'x' : '-'));
        }
        return sb.ToString();
    }

    /// <summary>Lit « rwxr-xr-x » ou « drwxr-xr-x » (sortie de ls, listes FTP) ; faux si illisible.</summary>
    public static bool TryParseSymbolic(string? text, out int mode)
    {
        mode = 0;
        var s = (text ?? string.Empty).Trim();
        if (s.Length >= 10) s = s.Substring(1, 9); // type en tête (d, l, -)
        if (s.Length != 9) return false;
        for (int who = 0; who < 3; who++)
        {
            char r = s[who * 3], w = s[who * 3 + 1], x = s[who * 3 + 2];
            if (r is not ('r' or '-') || w is not ('w' or '-')) return false;
            if (r == 'r') mode |= Bit(who, 0);
            if (w == 'w') mode |= Bit(who, 1);
            int special = who switch { 0 => SetUid, 1 => SetGid, _ => Sticky };
            switch (x)
            {
                case 'x': mode |= Bit(who, 2); break;
                case 's' or 't': mode |= Bit(who, 2) | special; break;
                case 'S' or 'T': mode |= special; break;
                case '-': break;
                default: return false;
            }
        }
        return true;
    }

    /// <summary>Droits connus d'un élément de liste (-1 si inconnus).</summary>
    public static int Of(RemoteItem item) =>
        item.Mode >= 0 ? item.Mode & Mask : TryParseSymbolic(item.Permissions, out var m) ? m : -1;
}

/// <summary>
/// Changement de droits : bits à mettre et bits à enlever (les autres restent comme ils sont).
/// Permet de modifier plusieurs éléments aux droits différents sans tout écraser.
/// </summary>
public readonly record struct ModeChange(int Set, int Clear)
{
    public static ModeChange None => default;
    public static ModeChange Absolute(int mode) => new(mode & UnixMode.Mask, UnixMode.Mask);
    public bool IsEmpty => Set == 0 && Clear == 0;
    /// <summary>Tous les bits sont fixés : l'ancien mode n'a pas d'importance.</summary>
    public bool IsAbsolute => ((Set | Clear) & UnixMode.Mask) == UnixMode.Mask;
    public int Apply(int old) => ((old & ~Clear) | Set) & UnixMode.Mask;
}

/// <summary>Un utilisateur ou un groupe Unix.</summary>
public sealed record UnixAccount(int Id, string Name)
{
    public override string ToString() => $"{Name} ({Id})";
}

/// <summary>Utilisateurs et groupes connus d'une machine (lus dans /etc/passwd et /etc/group, ou par getent).</summary>
public sealed class UnixAccounts
{
    public IReadOnlyList<UnixAccount> Users { get; }
    public IReadOnlyList<UnixAccount> Groups { get; }

    public UnixAccounts(IEnumerable<UnixAccount> users, IEnumerable<UnixAccount> groups)
    {
        Users = users.DistinctBy(u => u.Name).OrderBy(u => u.Name, StringComparer.Ordinal).ToList();
        Groups = groups.DistinctBy(g => g.Name).OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Format passwd / group : « nom:x:id:… » (lignes vides et commentaires ignorés).</summary>
    public static IEnumerable<UnixAccount> ParseColonFile(string? text)
    {
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split(':');
            if (f.Length < 3 || f[0].Length == 0) continue;
            if (int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id >= 0)
                yield return new UnixAccount(id, f[0]);
        }
    }

    public static UnixAccounts Parse(string? passwd, string? group) => new(ParseColonFile(passwd), ParseColonFile(group));

    public string? UserName(int id) => Users.FirstOrDefault(u => u.Id == id)?.Name;
    public string? GroupName(int id) => Groups.FirstOrDefault(g => g.Id == id)?.Name;

    /// <summary>Utilisateur tapé : nom connu, « nom (1000) », ou numéro.</summary>
    public UnixAccount? FindUser(string? text) => Find(Users, text);
    public UnixAccount? FindGroup(string? text) => Find(Groups, text);

    public static UnixAccount? Find(IReadOnlyList<UnixAccount>? list, string? text)
    {
        var s = (text ?? string.Empty).Trim();
        if (s.Length == 0) return null;
        int paren = s.IndexOf(" (", StringComparison.Ordinal);
        if (paren > 0 && s.EndsWith(')')) s = s[..paren];
        if (list?.FirstOrDefault(a => a.Name == s) is { } byName) return byName;
        if (int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return list?.FirstOrDefault(a => a.Id == id) ?? new UnixAccount(id, s);
        return null;
    }
}

/// <summary>Informations détaillées sur un élément (fenêtre Propriétés).</summary>
public sealed record RightsDetails(string Path, bool IsDirectory, bool IsLink, long Size, DateTime Modified)
{
    public DateTime? Accessed { get; init; }
    public DateTime? Created { get; init; }
    /// <summary>Droits (12 bits) ; -1 si le protocole ne les donne pas.</summary>
    public int Mode { get; init; } = -1;
    public int? UserId { get; init; }
    public int? GroupId { get; init; }
    public string? Owner { get; init; }
    public string? Group { get; init; }
    public string? LinkTarget { get; init; }

    public string OwnerDisplay => Display(Owner, UserId);
    public string GroupDisplay => Display(Group, GroupId);

    private static string Display(string? name, int? id) =>
        name is { Length: > 0 } && id is { } i && name != i.ToString(CultureInfo.InvariantCulture) ? $"{name} ({i})"
        : name is { Length: > 0 } ? name
        : id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public RemoteItem ToItem() => new(RemotePath.FileName(Path), Path, IsDirectory, Size, Modified, IsLink, Mode >= 0 ? UnixMode.ToSymbolic(Mode) : string.Empty, Mode);
}

/// <summary>Ce qui sait lire et changer des droits Unix : un serveur (SFTP, SCP, FTP) ou le disque local sous Linux.</summary>
public interface IRightsTarget
{
    /// <summary>Changement des droits (chmod) possible.</summary>
    bool SupportsPermissions { get; }
    /// <summary>Changement du propriétaire / groupe (chown) possible (il faut quand même en avoir le droit).</summary>
    bool SupportsOwnership { get; }
    Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct);
    Task<RightsDetails?> GetDetailsAsync(string path, CancellationToken ct);
    /// <summary>mode : valeur réelle sur 12 bits (0755 = 493).</summary>
    Task SetModeAsync(string path, int mode, CancellationToken ct);
    /// <summary>null : ne pas changer.</summary>
    Task SetOwnerAsync(string path, UnixAccount? user, UnixAccount? group, CancellationToken ct);
    /// <summary>Utilisateurs et groupes proposés ; null si on ne peut pas les lire.</summary>
    Task<UnixAccounts?> GetAccountsAsync(CancellationToken ct);
}

/// <summary>
/// Demande de la fenêtre Propriétés. FileMode / DirMode s'appliquent aux éléments choisis (et à leur contenu si Recursive).
/// ModeForContent faux : le contenu ne reçoit que le changement de propriétaire / groupe.
/// </summary>
public sealed record RightsRequest(ModeChange FileMode, ModeChange DirMode, UnixAccount? Owner, UnixAccount? Group, bool Recursive, bool ModeForContent = true)
{
    public bool ChangesOwner => Owner is not null || Group is not null;
    public bool IsEmpty => FileMode.IsEmpty && DirMode.IsEmpty && !ChangesOwner;
}

public sealed record RightsResult(int Changed, int Visited, IReadOnlyList<string> Errors);

/// <summary>Applique une demande à des éléments, avec leur contenu si demandé. Les erreurs n'arrêtent pas le travail : elles sont listées.</summary>
public static class RightsApplier
{
    public const int MaxErrors = 200;

    public static async Task<RightsResult> ApplyAsync(IRightsTarget target, IReadOnlyList<RemoteItem> items, RightsRequest request,
        IProgress<string>? progress, CancellationToken ct)
    {
        int changed = 0, visited = 0;
        var errors = new List<string>();
        var queue = new Queue<(RemoteItem Item, bool Top)>(items.Select(i => (i, true)));
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (item, top) = queue.Dequeue();
            visited++;
            progress?.Report(item.FullPath);

            // Contenu d'abord listé (on peut perdre le droit de lire en retirant « r » ou « x »).
            if (request.Recursive && item.IsDirectory && !item.IsLink)
            {
                try
                {
                    foreach (var child in await target.ListAsync(item.FullPath, ct).ConfigureAwait(false))
                        queue.Enqueue((child, false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AddError(errors, item.FullPath, ex);
                }
            }

            bool did = false;
            try
            {
                if (top || request.ModeForContent)
                {
                    var change = item.IsDirectory ? request.DirMode : request.FileMode;
                    if (!change.IsEmpty)
                    {
                        int old = UnixMode.Of(item);
                        if (old < 0 && !change.IsAbsolute)
                            old = (await target.GetDetailsAsync(item.FullPath, ct).ConfigureAwait(false))?.Mode ?? 0;
                        int mode = change.Apply(Math.Max(old, 0));
                        if (mode != old)
                        {
                            await target.SetModeAsync(item.FullPath, mode, ct).ConfigureAwait(false);
                            did = true;
                        }
                    }
                }
                // Un lien n'a pas de propriétaire à changer ici (chown suivrait le lien) : on ne touche qu'à sa cible quand il est choisi.
                if (request.ChangesOwner && (top || !item.IsLink))
                {
                    await target.SetOwnerAsync(item.FullPath, request.Owner, request.Group, ct).ConfigureAwait(false);
                    did = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AddError(errors, item.FullPath, ex);
            }
            if (did) changed++;
        }
        return new RightsResult(changed, visited, errors);
    }

    /// <summary>Taille totale d'éléments distants (dossiers parcourus, liens non suivis), avec le nombre de fichiers et de dossiers.</summary>
    public static async Task<(long Bytes, int Files, int Folders)> MeasureAsync(IRightsTarget target, IReadOnlyList<RemoteItem> items,
        IProgress<string>? progress, CancellationToken ct)
    {
        long bytes = 0; int files = 0, folders = 0;
        var queue = new Queue<RemoteItem>(items);
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var item = queue.Dequeue();
            if (!item.IsDirectory) { files++; bytes += item.Size; continue; }
            folders++;
            if (item.IsLink) continue;
            progress?.Report(item.FullPath);
            try
            {
                foreach (var child in await target.ListAsync(item.FullPath, ct).ConfigureAwait(false)) queue.Enqueue(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dossier illisible : ignoré */ }
        }
        return (bytes, files, folders);
    }

    private static void AddError(List<string> errors, string path, Exception ex)
    {
        if (errors.Count < MaxErrors) errors.Add($"{path} : {ex.Message}");
    }

    /// <summary>
    /// Case à trois états pour plusieurs éléments : vrai si le bit est mis partout, faux s'il ne l'est nulle part, null si c'est mélangé.
    /// </summary>
    public static bool? Common(IEnumerable<int> modes, int bit)
    {
        bool any = false, all = true, seen = false;
        foreach (var m in modes)
        {
            seen = true;
            if ((m & bit) != 0) any = true; else all = false;
        }
        return !seen ? false : all ? true : any ? null : false;
    }
}

/// <summary>
/// Commandes shell communes (SCP, disque local sous Linux) : chemins toujours entre apostrophes, numéros pour chown.
/// </summary>
public static class UnixCommands
{
    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    // %y type, %s taille, %T@ modif, %A@ accès, %m droits (octal, bits spéciaux compris), %U %G numéros, %u %g noms, %l cible du lien
    private const string DetailsFormat = "'%y\\t%s\\t%T@\\t%A@\\t%m\\t%U\\t%G\\t%u\\t%g\\t%l\\n'";

    /// <summary>Lit l'élément lui-même (-maxdepth 0) ; pour un lien on lit aussi sa cible (seconde ligne, -L).</summary>
    public static string DetailsCommand(string path) =>
        $"LC_ALL=C find {Quote(path)} -maxdepth 0 -printf {DetailsFormat}; LC_ALL=C find -L {Quote(path)} -maxdepth 0 -printf {DetailsFormat} 2>/dev/null";

    public static RightsDetails? ParseDetails(string path, string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        var self = lines[0].Split('\t');
        if (self.Length < 10) return null;
        bool isLink = self[0] == "l";
        var info = isLink && lines.Length > 1 && lines[1].Split('\t') is { Length: >= 10 } t ? t : self;
        long.TryParse(info[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
        bool isDir = info[0] == "d";
        return new RightsDetails(RemotePath.Normalize(path), isDir, isLink, isDir ? 0 : size, Epoch(info[2]))
        {
            Accessed = Epoch(info[3]),
            Mode = UnixMode.TryParseOctal(info[4], out var m) ? m : -1,
            UserId = int.TryParse(info[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : null,
            GroupId = int.TryParse(info[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ? g : null,
            Owner = info[7],
            Group = info[8],
            LinkTarget = isLink ? self[9] : null,
        };
    }

    private static DateTime Epoch(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)(v * 1000)).LocalDateTime
            : DateTime.MinValue;

    public static string ChmodCommand(string path, int mode) => $"chmod {UnixMode.ToOctal(mode)} -- {Quote(path)}";

    /// <summary>chown / chgrp avec les numéros (fonctionne même pour un compte sans nom).</summary>
    public static string? ChownCommand(string path, UnixAccount? user, UnixAccount? group) => (user, group) switch
    {
        ({ } u, { } g) => $"chown {u.Id.ToString(CultureInfo.InvariantCulture)}:{g.Id.ToString(CultureInfo.InvariantCulture)} -- {Quote(path)}",
        ({ } u, null) => $"chown {u.Id.ToString(CultureInfo.InvariantCulture)} -- {Quote(path)}",
        (null, { } g) => $"chgrp {g.Id.ToString(CultureInfo.InvariantCulture)} -- {Quote(path)}",
        _ => null,
    };

    public const string PasswdCommand = "getent passwd 2>/dev/null || cat /etc/passwd";
    public const string GroupCommand = "getent group 2>/dev/null || cat /etc/group";

    /// <summary>Message clair pour un refus de chown / chmod.</summary>
    public static string ExplainRefusal(string error, bool owner)
    {
        if (error.Contains("not permitted", StringComparison.OrdinalIgnoreCase) || error.Contains("non permise", StringComparison.OrdinalIgnoreCase)
            || error.Contains("denied", StringComparison.OrdinalIgnoreCase) || error.Contains("refusé", StringComparison.OrdinalIgnoreCase))
            return owner
                ? L.T("refusé : seul root (ou sudo) peut donner un fichier à un autre utilisateur ; le propriétaire peut seulement choisir un de ses groupes.")
                : L.T("refusé : seul le propriétaire du fichier (ou root) peut changer ses droits.");
        return error;
    }
}
