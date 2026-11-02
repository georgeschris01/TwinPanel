// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;

namespace TwinPanel.Core.Viewer;

/// <summary>
/// Un fichier texte ouvert dans l'éditeur intégré. On retient son encodage, son BOM et ses fins de ligne
/// pour l'enregistrer exactement comme il était (un fichier Windows reste en CRLF, un fichier Linux en LF).
/// Dans <see cref="Text"/>, les fins de ligne sont toujours « \n ».
/// </summary>
public sealed record TextDocument(string Text, Encoding Encoding, bool HasBom, string NewLine)
{
    /// <summary>Au-delà, l'éditeur intégré devient trop lent : on conseille un éditeur externe.</summary>
    public const long MaxEditableBytes = 10L * 1024 * 1024;

    public string EncodingLabel => Encoding switch
    {
        UTF8Encoding => HasBom ? L.T("UTF-8 avec BOM") : "UTF-8",
        UnicodeEncoding when Encoding.CodePage == 1201 => "UTF-16 BE",
        UnicodeEncoding => "UTF-16 LE",
        _ when Encoding.CodePage == 28591 => "Windows / Latin-1",
        _ => Encoding.WebName,
    };

    public string NewLineLabel => NewLine switch
    {
        "\r\n" => "CRLF (Windows)",
        "\r" => L.T("CR (ancien Mac)"),
        _ => "LF (Linux)",
    };

    public static TextDocument Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxEditableBytes)
            throw new FileOperationException(L.F("Fichier trop gros pour l'éditeur intégré ({0}, maximum 10 Mo). Utilise un éditeur externe.", FileEntry.FormatSize(info.Length)));

        var bytes = File.ReadAllBytes(path);
        if (TextDecoding.LooksBinary(bytes))
            throw new FileOperationException(L.T("Ce fichier semble binaire : il n'est pas modifiable comme du texte."));

        var encoding = TextDecoding.Detect(bytes, complete: true);
        var preamble = encoding.GetPreamble();
        bool hasBom = preamble.Length > 0 && bytes.AsSpan().StartsWith(preamble);
        var raw = encoding.GetString(bytes, hasBom ? preamble.Length : 0, bytes.Length - (hasBom ? preamble.Length : 0));

        var newLine = DetectNewLine(raw);
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        // L'encodage gardé n'écrit jamais de BOM tout seul : on le remet nous-mêmes si le fichier en avait un.
        var plain = encoding switch
        {
            UTF8Encoding => new UTF8Encoding(false),
            UnicodeEncoding when encoding.CodePage == 1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
            UnicodeEncoding => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
            _ => encoding,
        };
        return new TextDocument(text, plain, hasBom, newLine);
    }

    /// <summary>
    /// Enregistre dans un fichier temporaire puis remplace l'original : en cas de coupure ou d'erreur,
    /// l'ancien contenu reste intact. Les attributs (caché…) sont conservés ; un fichier en lecture seule est refusé.
    /// </summary>
    public void Save(string path)
    {
        FileAttributes? attributes = File.Exists(path) ? File.GetAttributes(path) : null;
        if (attributes is { } a && a.HasFlag(FileAttributes.ReadOnly))
            throw new FileOperationException(L.T("Le fichier est en lecture seule : modification impossible."));

        var text = Text.Replace("\r\n", "\n");
        if (NewLine != "\n") text = text.Replace("\n", NewLine);

        var tmp = path + ".twinpanel-save";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (HasBom)
                {
                    var bom = Encoding switch
                    {
                        UTF8Encoding => new byte[] { 0xEF, 0xBB, 0xBF },
                        UnicodeEncoding when Encoding.CodePage == 1201 => [0xFE, 0xFF],
                        UnicodeEncoding => [0xFF, 0xFE],
                        _ => [],
                    };
                    fs.Write(bom);
                }
                fs.Write(Encoding.GetBytes(text));
            }
            File.Move(tmp, path, overwrite: true);
            if (attributes is { } keep) File.SetAttributes(path, keep);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { /* rien de plus à faire */ }
            throw;
        }
    }

    /// <summary>La fin de ligne la plus fréquente du fichier (CRLF par défaut sous Windows pour un fichier d'une ligne).</summary>
    private static string DetectNewLine(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (text[i] == '\n') lf++;
        }
        if (crlf == 0 && lf == 0 && cr == 0) return Environment.NewLine;
        if (crlf >= lf && crlf >= cr) return "\r\n";
        return lf >= cr ? "\n" : "\r";
    }
}
