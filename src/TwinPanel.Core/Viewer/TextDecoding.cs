// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;

namespace TwinPanel.Core.Viewer;

public enum TextEncodingChoice { Auto, Utf8, Latin1, Utf16LE, Utf16BE }

/// <summary>Lecture de texte avec détection d'encodage (BOM, UTF-8 valide, sinon Windows Latin-1).</summary>
public static class TextDecoding
{
    public static Encoding Resolve(TextEncodingChoice choice, ReadOnlySpan<byte> sample) => choice switch
    {
        TextEncodingChoice.Utf8 => new UTF8Encoding(false),
        TextEncodingChoice.Latin1 => Encoding.Latin1,
        TextEncodingChoice.Utf16LE => Encoding.Unicode,
        TextEncodingChoice.Utf16BE => Encoding.BigEndianUnicode,
        _ => Detect(sample),
    };

    /// <param name="complete">Vrai si <paramref name="bytes"/> contient tout le fichier (pas seulement son début).</param>
    public static Encoding Detect(ReadOnlySpan<byte> bytes, bool complete = false)
    {
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return new UTF8Encoding(true);
        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE })) return Encoding.Unicode;
        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF })) return Encoding.BigEndianUnicode;
        // Beaucoup d'octets nuls en position impaire : UTF-16 sans BOM.
        if (bytes.Length >= 8)
        {
            int zerosOdd = 0, zerosEven = 0;
            for (int i = 0; i < Math.Min(bytes.Length, 4096); i++)
                if (bytes[i] == 0) { if (i % 2 == 1) zerosOdd++; else zerosEven++; }
            int pairs = Math.Min(bytes.Length, 4096) / 2;
            if (zerosOdd > pairs * 0.4 && zerosEven < pairs * 0.05) return Encoding.Unicode;
            if (zerosEven > pairs * 0.4 && zerosOdd < pairs * 0.05) return Encoding.BigEndianUnicode;
        }
        return IsValidUtf8(bytes, complete) ? new UTF8Encoding(false) : Encoding.Latin1;
    }

    /// <summary>Décode en lignes (fins de ligne Windows, Unix ou Mac acceptées) ; le BOM est retiré.</summary>
    public static string[] DecodeLines(byte[] bytes, Encoding encoding)
    {
        var text = encoding.GetString(bytes);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    /// <summary>
    /// Vrai si les octets sont de l'UTF-8 valide. Pour un début de fichier (lecture partielle), seul un caractère
    /// réellement coupé à la fin est ignoré — et non plus les 4 derniers octets d'office, ce qui faisait prendre
    /// un petit fichier Windows (« café ») pour de l'UTF-8.
    /// </summary>
    private static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool complete)
    {
        if (!complete) bytes = bytes[..(bytes.Length - IncompleteTail(bytes))];
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try { strict.GetCharCount(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }

    /// <summary>Nombre d'octets en fin de tampon qui forment le début d'un caractère UTF-8 incomplet (0 à 3).</summary>
    private static int IncompleteTail(ReadOnlySpan<byte> bytes)
    {
        for (int back = 1; back <= Math.Min(3, bytes.Length); back++)
        {
            byte b = bytes[^back];
            if ((b & 0xC0) == 0x80) continue;          // octet de continuation : on remonte
            if ((b & 0x80) == 0) return 0;             // ASCII : rien de coupé
            int needed = (b & 0xE0) == 0xC0 ? 2 : (b & 0xF0) == 0xE0 ? 3 : (b & 0xF8) == 0xF0 ? 4 : 1;
            return needed > back ? back : 0;           // séquence plus longue que ce qui reste : coupée
        }
        return 0;
    }

    /// <summary>Un octet nul dans les premiers Ko (hors UTF-16) : contenu binaire.</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        var enc = Detect(sample);
        if (enc is UnicodeEncoding) return false;
        return sample[..Math.Min(sample.Length, 8192)].IndexOf((byte)0) >= 0;
    }
}

/// <summary>Affichage hexadécimal classique : adresse, 16 octets, caractères lisibles.</summary>
public static class HexFormatter
{
    public const int BytesPerLine = 16;

    public static string FormatLine(ReadOnlySpan<byte> data, long offset)
    {
        var sb = new StringBuilder(80);
        sb.Append(offset.ToString("X8")).Append("  ");
        for (int i = 0; i < BytesPerLine; i++)
        {
            if (i < data.Length) sb.Append(data[i].ToString("X2")).Append(' ');
            else sb.Append("   ");
            if (i == 7) sb.Append(' ');
        }
        sb.Append(" |");
        foreach (var b in data) sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        sb.Append('|');
        return sb.ToString();
    }

    public static string[] FormatAll(byte[] data, long baseOffset = 0)
    {
        int lines = (data.Length + BytesPerLine - 1) / BytesPerLine;
        var result = new string[lines];
        for (int l = 0; l < lines; l++)
        {
            int start = l * BytesPerLine;
            result[l] = FormatLine(data.AsSpan(start, Math.Min(BytesPerLine, data.Length - start)), baseOffset + start);
        }
        return result;
    }
}
