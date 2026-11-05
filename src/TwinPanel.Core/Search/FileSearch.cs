// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using TwinPanel.Core.Selection;

namespace TwinPanel.Core.Search;

/// <summary>Critères de recherche (Alt+F7).</summary>
public sealed record SearchCriteria
{
    public required string Root { get; init; }
    /// <summary>Masque des noms : « *.jpg;*.png », exclusions après « | ».</summary>
    public string NameMask { get; init; } = "*";
    /// <summary>0 = seulement le dossier de départ ; null = sans limite.</summary>
    public int? MaxDepth { get; init; }
    public string? ContainsText { get; init; }
    public bool MatchCase { get; init; }
    public bool TextIsRegex { get; init; }
    public long? MinSize { get; init; }
    public long? MaxSize { get; init; }
    public DateTime? ModifiedAfter { get; init; }
    public DateTime? ModifiedBefore { get; init; }
    public bool IncludeFiles { get; init; } = true;
    public bool IncludeFolders { get; init; } = true;
    /// <summary>Ignore les éléments cachés, et ne parcourt pas les dossiers cachés.</summary>
    public bool SkipHidden { get; init; }
}

/// <summary>Recherche de fichiers : les résultats arrivent au fur et à mesure.</summary>
public static class FileSearch
{
    /// <summary>Au-delà de cette taille, la recherche par expression régulière dans le contenu est ignorée.</summary>
    private const long MaxRegexFileSize = 64L * 1024 * 1024;

    public static async IAsyncEnumerable<FileEntry> SearchAsync(SearchCriteria c, IProgress<string>? currentFolder = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var channel = Channel.CreateBounded<FileEntry>(new BoundedChannelOptions(512) { SingleReader = true, SingleWriter = true });
        var producer = Task.Run(() =>
        {
            try { Walk(c, channel.Writer, currentFolder, ct); }
            finally { channel.Writer.TryComplete(); }
        }, ct);

        await foreach (var e in channel.Reader.ReadAllAsync(ct))
            yield return e;
        await producer; // remonte une éventuelle erreur (ex. dossier de départ introuvable)
    }

    private static void Walk(SearchCriteria c, ChannelWriter<FileEntry> output, IProgress<string>? currentFolder, CancellationToken ct)
    {
        if (!Directory.Exists(c.Root)) throw new DirectoryNotFoundException(L.F("Dossier introuvable : {0}", c.Root));

        var mask = new MaskMatcher(c.NameMask);
        var textMatcher = BuildTextMatcher(c);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((c.Root, 0));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = pending.Pop();
            currentFolder?.Report(dir);

            IEnumerable<FileSystemInfo> items;
            try { items = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var info in items)
            {
                ct.ThrowIfCancellationRequested();
                if (c.SkipHidden && (info.Attributes & FileAttributes.Hidden) != 0) continue;
                bool isDir = info is DirectoryInfo;
                if (isDir && (c.MaxDepth is null || depth < c.MaxDepth) && info.LinkTarget is null)
                    pending.Push((info.FullName, depth + 1));

                if (isDir ? !c.IncludeFolders : !c.IncludeFiles) continue;
                if (!mask.IsMatch(info.Name)) continue;
                if (c.ModifiedAfter is { } after && info.LastWriteTime < after) continue;
                if (c.ModifiedBefore is { } before && info.LastWriteTime > before) continue;

                long size = info is FileInfo f ? f.Length : 0;
                if (!isDir && c.MinSize is { } min && size < min) continue;
                if (!isDir && c.MaxSize is { } max && size > max) continue;

                if (textMatcher is not null)
                {
                    if (isDir) continue; // le texte ne concerne que les fichiers
                    if (!FileContains(info.FullName, size, textMatcher, c.TextIsRegex, ct)) continue;
                }

                var entry = new FileEntry(info.Name, info.FullName, isDir, size, info.LastWriteTime, info.Attributes);
                // Attente si le lecteur est en retard : la mémoire reste bornée.
                output.WriteAsync(entry, ct).AsTask().GetAwaiter().GetResult();
            }
        }
    }

    /// <summary>Test du texte recherché (null s'il n'y a pas de texte à chercher). ArgumentException si l'expression régulière est invalide.</summary>
    public static Func<string, bool>? BuildTextMatcher(SearchCriteria c)
    {
        if (string.IsNullOrEmpty(c.ContainsText)) return null;
        if (c.TextIsRegex)
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | (c.MatchCase ? 0 : RegexOptions.IgnoreCase);
            var regex = new Regex(c.ContainsText, options, TimeSpan.FromSeconds(5)); // ArgumentException si invalide
            return regex.IsMatch;
        }
        var cmp = c.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var needle = c.ContainsText;
        return text => text.Contains(needle, cmp);
    }

    /// <summary>
    /// Cherche le texte par morceaux de 1 Mo avec chevauchement (un mot à cheval sur deux morceaux est trouvé).
    /// Encodage : BOM s'il y en a, sinon UTF-8 ; si le fichier n'est pas de l'UTF-8 valide, essai en Windows-1252/Latin-1.
    /// </summary>
    private static bool FileContains(string path, long size, Func<string, bool> matcher, bool isRegex, CancellationToken ct)
    {
        try
        {
            if (isRegex)
            {
                if (size > MaxRegexFileSize) return false;
                return matcher(ReadAllText(path));
            }

            foreach (var encoding in CandidateEncodings(path))
            {
                using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
                var buffer = new char[1024 * 1024];
                string carry = string.Empty;
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var chunk = carry + new string(buffer, 0, read);
                    if (matcher(chunk)) return true;
                    carry = chunk.Length > 256 ? chunk[^256..] : chunk;
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            return false; // fichier verrouillé ou illisible : on passe
        }
    }

    private static IEnumerable<Encoding> CandidateEncodings(string path)
    {
        yield return new UTF8Encoding(false, throwOnInvalidBytes: false);
        if (!LooksLikeUtf8(path)) yield return Encoding.Latin1;
    }

    private static bool LooksLikeUtf8(string path)
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        using var fs = File.OpenRead(path);
        var bytes = new byte[Math.Min(fs.Length, 64 * 1024)];
        int n = fs.Read(bytes, 0, bytes.Length);
        try { strict.GetString(bytes, 0, Math.Max(0, n - 4)); return true; } // on ignore la fin (caractère coupé)
        catch (DecoderFallbackException) { return false; }
    }

    /// <summary>Le texte se trouve-t-il dans ce contenu (fichier distant téléchargé) ? UTF-8, puis Windows-1252 / Latin-1 si besoin.</summary>
    public static bool BytesContain(byte[] data, Func<string, bool> matcher)
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            var start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            return matcher(strict.GetString(data, start, data.Length - start));
        }
        catch (DecoderFallbackException) { return matcher(Encoding.Latin1.GetString(data)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static string ReadAllText(string path) =>
        LooksLikeUtf8(path) ? File.ReadAllText(path) : File.ReadAllText(path, Encoding.Latin1);
}
