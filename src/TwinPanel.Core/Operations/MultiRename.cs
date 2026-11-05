// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TwinPanel.Core.Operations;

public enum CaseMode { Unchanged, Lower, Upper, FirstLetterUpper, EachWordUpper }

/// <summary>Réglages du renommage multiple.</summary>
public sealed record RenameOptions
{
    /// <summary>
    /// Masque du nom. [N] nom, [N2-5] lettres 2 à 5, [C] compteur, [Y][M][D][h][m][s] date, [P] dossier parent,
    /// et les balises de <see cref="RenameMedia"/> ([Titre], [Artiste], [Résolution]…).
    /// </summary>
    public string NameMask { get; init; } = "[N]";
    /// <summary>Masque de l'extension. [E] extension, [E1-2]…</summary>
    public string ExtensionMask { get; init; } = "[E]";
    public string Search { get; init; } = string.Empty;
    public string Replace { get; init; } = string.Empty;
    public bool UseRegex { get; init; }
    public CaseMode Case { get; init; } = CaseMode.Unchanged;
    public int CounterStart { get; init; } = 1;
    public int CounterStep { get; init; } = 1;
    public int CounterDigits { get; init; } = 1;
}

/// <param name="Skipped">Le fichier garde son nom, sans empêcher de renommer les autres (valeur absente).</param>
public sealed record RenamePreview(FileEntry Entry, string NewName, string? Error, string? Skipped = null)
{
    public bool Changes => Error is null && Skipped is null && !string.Equals(Entry.Name, NewName, StringComparison.Ordinal);
    /// <summary>Ce que montre la colonne « Problème ».</summary>
    public string? Problem => Error ?? Skipped;
}

/// <summary>Ce qui a été renommé, pour pouvoir annuler.</summary>
public sealed record RenameLog(IReadOnlyList<(string OldPath, string NewPath)> Items);

/// <summary>Renommage multiple façon « Ctrl+M » : masques, compteur, rechercher/remplacer, casse.</summary>
public static partial class MultiRename
{
    private static string MediaUnavailable => L.T("Balises médias : fichiers du PC seulement");

    [GeneratedRegex(@"\[(?<k>N|E)(?:(?<a>\d+)(?<dash>-)?(?<b>\d+)?)?\]|\[(?<k>C|YMD|Y|M|D|h|m|s|P)\]|\[(?<media>Titre|Artiste|Album|Piste|Année|Genre|Résolution|Durée|Date de prise de vue|Title|Artist|Track|Year|Resolution|Duration|Date taken)\]")]
    private static partial Regex Placeholder();

    /// <summary>Le masque demande une information à lire dans les fichiers.</summary>
    public static bool UsesMedia(string mask) => Placeholder().Matches(mask).Any(m => m.Groups["media"].Success);

    private static readonly StringComparer NameComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <param name="media">Valeurs médias par chemin de fichier ; null : on ne peut pas les lire (serveur).</param>
    public static IReadOnlyList<RenamePreview> Preview(IReadOnlyList<FileEntry> entries, RenameOptions o,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? media = null)
    {
        bool usesMedia = UsesMedia(o.NameMask) || UsesMedia(o.ExtensionMask);
        Regex? search = null;
        string? regexError = null;
        if (o.UseRegex && o.Search.Length > 0)
        {
            try { search = new Regex(o.Search, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { regexError = L.T("Expression régulière invalide : ") + ex.Message; }
        }

        var result = new List<RenamePreview>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (regexError is not null) { result.Add(new RenamePreview(e, e.Name, regexError)); continue; }
            if (usesMedia && media is null) { result.Add(new RenamePreview(e, e.Name, MediaUnavailable)); continue; }

            var missing = new List<string>();
            string name;
            try { name = BuildName(e, o, o.CounterStart + i * o.CounterStep, search, media?.GetValueOrDefault(e.FullPath), missing); }
            catch (RegexMatchTimeoutException) { result.Add(new RenamePreview(e, e.Name, L.T("Expression régulière trop lente"))); continue; }

            // Un nom à trous (« - - .mp3 ») ne vaut rien : ce fichier garde le sien.
            if (missing.Count > 0) { result.Add(new RenamePreview(e, e.Name, null, L.F("Pas de {0} : non renommé", string.Join(", ", missing.Distinct().Select(m => $"[{L.T(m)}]"))))); continue; }
            result.Add(new RenamePreview(e, name, Validate(name)));
        }

        // Doublons dans la nouvelle liste, et collisions avec des fichiers qui ne sont pas renommés.
        var renamedPaths = new HashSet<string>(entries.Select(e => e.FullPath), NameComparer);
        var groups = result.Where(r => r.Error is null)
                           .GroupBy(r => Path.Combine(Path.GetDirectoryName(r.Entry.FullPath)!, r.NewName), NameComparer);
        foreach (var g in groups)
        {
            var items = g.ToList();
            string? error = null;
            if (items.Count > 1) error = L.T("Deux éléments auraient le même nom");
            else if (!renamedPaths.Contains(g.Key) && (File.Exists(g.Key) || Directory.Exists(g.Key)))
                error = L.T("Un élément porte déjà ce nom");
            if (error is null) continue;
            foreach (var r in items) result[result.IndexOf(r)] = r with { Error = error };
        }
        return result;
    }

    /// <summary>
    /// Applique le renommage en deux temps (noms temporaires puis noms finaux) :
    /// permet d'échanger deux noms (a→b, b→a). En cas d'erreur, tout est remis en place.
    /// </summary>
    public static RenameLog Execute(IReadOnlyList<RenamePreview> previews)
    {
        if (previews.FirstOrDefault(p => p.Error is not null) is { } bad)
            throw new FileOperationException($"« {bad.Entry.Name} » : {bad.Error}");

        var todo = previews.Where(p => p.Changes).ToList();
        var staged = new List<(string Original, string Temp, string Final)>();
        var done = new List<(string OldPath, string NewPath)>();
        try
        {
            foreach (var p in todo)
            {
                var dir = Path.GetDirectoryName(p.Entry.FullPath)!;
                var temp = Path.Combine(dir, $".tp-rename-{Guid.NewGuid():N}");
                MovePath(p.Entry.FullPath, temp);
                staged.Add((p.Entry.FullPath, temp, Path.Combine(dir, p.NewName)));
            }
            foreach (var s in staged.ToList())
            {
                MovePath(s.Temp, s.Final);
                done.Add((s.Original, s.Final));
                staged.Remove(s);
            }
            return new RenameLog(done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Remise en place : d'abord les éléments encore en nom temporaire, puis ceux déjà renommés.
            foreach (var s in staged) TryMove(s.Temp, s.Original);
            foreach (var d in Enumerable.Reverse(done)) TryMove(d.NewPath, d.OldPath);
            throw new FileOperationException(L.T("Renommage annulé : ") + ex.Message, ex);
        }
    }

    /// <summary>
    /// Même principe que <see cref="Execute"/> pour des chemins de panneau quelconques (serveur « sftp://… ») :
    /// move renomme un chemin en un autre (il échoue si la cible existe).
    /// </summary>
    public static async Task<RenameLog> ExecuteAsync(IReadOnlyList<RenamePreview> previews, Func<string, string, CancellationToken, Task> move,
        CancellationToken ct = default)
    {
        if (previews.FirstOrDefault(p => p.Error is not null) is { } bad)
            throw new FileOperationException($"« {bad.Entry.Name} » : {bad.Error}");

        var todo = previews.Where(p => p.Changes).ToList();
        var staged = new List<(string Original, string Temp, string Final)>();
        var done = new List<(string OldPath, string NewPath)>();
        try
        {
            foreach (var p in todo)
            {
                var dir = DirOf(p.Entry.FullPath);
                var temp = Join(dir, $".tp-rename-{Guid.NewGuid():N}");
                await move(p.Entry.FullPath, temp, ct).ConfigureAwait(false);
                staged.Add((p.Entry.FullPath, temp, Join(dir, p.NewName)));
            }
            foreach (var s in staged.ToList())
            {
                await move(s.Temp, s.Final, ct).ConfigureAwait(false);
                done.Add((s.Original, s.Final));
                staged.Remove(s);
            }
            return new RenameLog(done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var s in staged) await TryMoveAsync(move, s.Temp, s.Original).ConfigureAwait(false);
            foreach (var d in Enumerable.Reverse(done)) await TryMoveAsync(move, d.NewPath, d.OldPath).ConfigureAwait(false);
            throw new FileOperationException(L.T("Renommage annulé : ") + ex.Message, ex);
        }
    }

    public static Task UndoAsync(RenameLog log, Func<string, string, CancellationToken, Task> move, CancellationToken ct = default) =>
        ExecuteAsync(log.Items
            .Select(i => new RenamePreview(new FileEntry(NameOf(i.NewPath), i.NewPath, false, 0, default, FileAttributes.Normal), NameOf(i.OldPath), null))
            .ToList(), move, ct);

    // Chemins avec « / » (serveur) ou « \ » (PC).
    private static string DirOf(string path) => path[..Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'))];
    private static string NameOf(string path) => path[(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1)..];
    private static string Join(string dir, string name) => dir + (dir.Contains("://", StringComparison.Ordinal) || dir.StartsWith('/') ? "/" : "\\") + name;

    private static async Task TryMoveAsync(Func<string, string, CancellationToken, Task> move, string from, string to)
    {
        try { await move(from, to, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* on fait au mieux pour remettre en place */ }
    }

    /// <summary>Annule un renommage précédent.</summary>
    public static void Undo(RenameLog log)
    {
        var previews = log.Items
            .Select(i => new RenamePreview(EntryFor(i.NewPath), Path.GetFileName(i.OldPath), null))
            .ToList();
        Execute(previews);
    }

    // ---------------------------------------------------------------------

    private static string BuildName(FileEntry e, RenameOptions o, int counter, Regex? search, IReadOnlyDictionary<string, string>? media, List<string> missing)
    {
        var baseName = e.IsDirectory ? e.Name : Path.GetFileNameWithoutExtension(e.Name);
        var ext = e.IsDirectory ? string.Empty : Path.GetExtension(e.Name).TrimStart('.');
        // Cas « .gitignore » : pas de nom, tout est extension → on garde le nom complet.
        if (baseName.Length == 0) { baseName = e.Name; ext = string.Empty; }

        var name = Expand(o.NameMask, e, baseName, ext, counter, o.CounterDigits, media, missing);
        var newExt = e.IsDirectory ? string.Empty : Expand(o.ExtensionMask, e, baseName, ext, counter, o.CounterDigits, media, missing);
        var full = newExt.Length > 0 ? $"{name}.{newExt}" : name;

        if (o.Search.Length > 0)
            full = search is not null ? search.Replace(full, o.Replace) : full.Replace(o.Search, o.Replace, StringComparison.OrdinalIgnoreCase);

        return ApplyCase(full, o.Case).Trim();
    }

    private static string Expand(string mask, FileEntry e, string baseName, string ext, int counter, int digits,
        IReadOnlyDictionary<string, string>? media, List<string> missing) =>
        Placeholder().Replace(mask, m =>
        {
            if (m.Groups["media"].Success)
            {
                var tag = RenameMedia.Canonical(m.Groups["media"].Value);
                if (media?.GetValueOrDefault(tag) is { Length: > 0 } value) return RenameMedia.Clean(value);
                missing.Add(tag);
                return string.Empty;
            }
            var k = m.Groups["k"].Value;
            var date = e.LastWriteTime;
            return k switch
            {
                "N" => Slice(baseName, m),
                "E" => Slice(ext, m),
                "C" => counter.ToString(new string('0', Math.Max(1, digits)), CultureInfo.InvariantCulture),
                "YMD" => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                "Y" => date.ToString("yyyy", CultureInfo.InvariantCulture),
                "M" => date.ToString("MM", CultureInfo.InvariantCulture),
                "D" => date.ToString("dd", CultureInfo.InvariantCulture),
                "h" => date.ToString("HH", CultureInfo.InvariantCulture),
                "m" => date.ToString("mm", CultureInfo.InvariantCulture),
                "s" => date.ToString("ss", CultureInfo.InvariantCulture),
                "P" => Path.GetFileName(Path.GetDirectoryName(e.FullPath)) ?? string.Empty,
                _ => m.Value,
            };
        });

    /// <summary>[N] tout, [N3] 3ᵉ lettre, [N2-5] lettres 2 à 5, [N4-] de la 4ᵉ à la fin (positions à partir de 1).</summary>
    private static string Slice(string text, Match m)
    {
        if (!m.Groups["a"].Success) return text;
        int a = int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
        int start = Math.Clamp(a - 1, 0, text.Length);
        int end = !m.Groups["dash"].Success ? a
            : m.Groups["b"].Success ? int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture)
            : text.Length;
        end = Math.Clamp(end, start, text.Length);
        return text[start..end];
    }

    private static string ApplyCase(string s, CaseMode mode) => mode switch
    {
        CaseMode.Lower => s.ToLowerInvariant(),
        CaseMode.Upper => s.ToUpperInvariant(),
        CaseMode.FirstLetterUpper => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant(),
        CaseMode.EachWordUpper => EachWord(s),
        _ => s,
    };

    private static string EachWord(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool start = true;
        foreach (var c in s)
        {
            sb.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            start = !char.IsLetterOrDigit(c);
        }
        return sb.ToString();
    }

    private static string? Validate(string name)
    {
        if (name.Length == 0) return L.T("Nom vide");
        if (name is "." or "..") return L.T("Nom réservé");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return L.T("Caractère interdit dans le nom");
        if (name.EndsWith('.') && OperatingSystem.IsWindows()) return L.T("Un nom ne peut pas finir par un point");
        return null;
    }

    private static FileEntry EntryFor(string path)
    {
        bool isDir = Directory.Exists(path);
        FileSystemInfo info = isDir ? new DirectoryInfo(path) : new FileInfo(path);
        return new FileEntry(info.Name, info.FullName, isDir, isDir ? 0 : ((FileInfo)info).Length, info.LastWriteTime, info.Attributes);
    }

    private static void MovePath(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static void TryMove(string from, string to)
    {
        try { MovePath(from, to); }
        catch (IOException) { /* on fait au mieux pour remettre en place */ }
        catch (UnauthorizedAccessException) { }
    }
}
