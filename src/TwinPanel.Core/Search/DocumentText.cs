// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace TwinPanel.Core.Search;

/// <summary>
/// Texte lisible des documents pour la recherche de texte (Alt+F7) : PDF, epub, Word, Excel, PowerPoint, OpenDocument.
/// Ces fichiers sont compressés : lus octet par octet, leur texte est invisible et on tombe sur des morceaux
/// sans rapport (noms de pages internes, métadonnées). Ici, on en tire le vrai texte.
/// </summary>
public static partial class DocumentText
{
    /// <summary>Au-delà, le document n'est pas lu (le texte extrait resterait en mémoire).</summary>
    public const long MaxFileSize = 256L * 1024 * 1024;

    /// <summary>Texte gardé au plus (protège d'un zip qui gonfle énormément une fois décompressé).</summary>
    public const int MaxTextChars = 64 * 1024 * 1024;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".epub",
        ".docx", ".docm", ".dotx", ".dotm",
        ".xlsx", ".xlsm", ".xltx",
        ".pptx", ".pptm", ".potx",
        ".odt", ".ott", ".ods", ".ots", ".odp", ".otp",
    };

    /// <summary>Ce nom de fichier est-il un document dont on sait lire le texte ?</summary>
    public static bool IsDocument(string fileName) => Extensions.Contains(Path.GetExtension(fileName));

    /// <summary>
    /// Texte du document (null s'il est illisible : abîmé, protégé par mot de passe, format inattendu).
    /// Le flux doit permettre de se déplacer (fichier ou MemoryStream).
    /// </summary>
    public static string? Extract(Stream stream, string fileName, CancellationToken ct = default)
    {
        try
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext == ".pdf" ? FromPdf(stream, ct) : FromZip(stream, PartsOf(ext), ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // document abîmé ou chiffré : on passe, comme un fichier illisible
        }
    }

    /// <summary>Parties du zip qui contiennent le texte, selon le format.</summary>
    private static Regex PartsOf(string ext) => ext switch
    {
        ".epub" => EpubParts(),
        ".docx" or ".docm" or ".dotx" or ".dotm" => WordParts(),
        ".xlsx" or ".xlsm" or ".xltx" => ExcelParts(),
        ".pptx" or ".pptm" or ".potx" => PowerPointParts(),
        _ => OpenDocumentParts(),
    };

    [GeneratedRegex(@"\.(x?html?|opf|ncx)$", RegexOptions.IgnoreCase)]
    private static partial Regex EpubParts();

    [GeneratedRegex(@"^(word/(document|header\d*|footer\d*|footnotes|endnotes|comments)\.xml|docProps/core\.xml)$", RegexOptions.IgnoreCase)]
    private static partial Regex WordParts();

    [GeneratedRegex(@"^(xl/sharedStrings\.xml|xl/worksheets/sheet\d+\.xml|docProps/core\.xml)$", RegexOptions.IgnoreCase)]
    private static partial Regex ExcelParts();

    [GeneratedRegex(@"^(ppt/slides/slide\d+\.xml|ppt/notesSlides/notesSlide\d+\.xml|docProps/core\.xml)$", RegexOptions.IgnoreCase)]
    private static partial Regex PowerPointParts();

    [GeneratedRegex(@"^(content|meta)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex OpenDocumentParts();

    private static string FromZip(Stream stream, Regex parts, CancellationToken ct)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var text = new StringBuilder();
        foreach (var entry in zip.Entries.Where(e => parts.IsMatch(e.FullName)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            var buffer = new char[64 * 1024];
            var xml = new StringBuilder();
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (text.Length + xml.Length + read > MaxTextChars) break;
                xml.Append(buffer, 0, read);
            }
            text.Append(XmlToText(xml.ToString())).Append('\n');
            if (text.Length >= MaxTextChars) break;
        }
        return text.ToString();
    }

    /// <summary>
    /// Retire les balises. Les fins de paragraphe, de cellule, les sauts de ligne deviennent des retours à la ligne ;
    /// les espaces écrits en balises deviennent des espaces ; les autres balises disparaissent sans espace, car Word coupe souvent un mot en plusieurs morceaux (« des » + « tin »).
    /// </summary>
    internal static string XmlToText(string xml)
    {
        xml = HiddenBlocks().Replace(xml, " ");
        xml = Spaces().Replace(xml, " ");
        xml = BlockBreaks().Replace(xml, "\n");
        xml = Tags().Replace(xml, string.Empty);
        return WebUtility.HtmlDecode(xml).Replace(' ', ' ').Replace("­", string.Empty, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"<(style|script)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HiddenBlocks();

    [GeneratedRegex(@"</(w:p|w:tc|a:p|text:p|text:h|p|div|h[1-6]|li|tr|td|th|title|si|c|dc:\w+)\s*>|<(br|w:br|w:cr|text:line-break)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreaks();

    /// <summary>Espaces et tabulations écrits comme des balises (OpenDocument, Word).</summary>
    [GeneratedRegex(@"<(w:tab|text:s|text:tab)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tags();

    private static string FromPdf(Stream stream, CancellationToken ct)
    {
        using var pdf = PdfDocument.Open(stream);
        var text = new StringBuilder();
        if (pdf.Information.Title is { } title) text.Append(title).Append('\n');
        if (pdf.Information.Author is { } author) text.Append(author).Append('\n');
        foreach (var page in pdf.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var word in page.GetWords()) text.Append(word.Text).Append(' ');
            text.Append('\n');
            if (text.Length >= MaxTextChars) break;
        }
        return text.ToString();
    }
}
