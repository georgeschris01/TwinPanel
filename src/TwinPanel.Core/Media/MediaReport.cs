// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text;

namespace TwinPanel.Core.Media;

public sealed record MediaField(string Label, string Value);

/// <summary>Une piste (vidéo, audio, sous-titres…) avec ses caractéristiques lisibles.</summary>
public sealed class MediaTrack
{
    public required MediaInfoStream Kind { get; init; }
    /// <summary>Numéro dans son type, à partir de 1 (comme « track:a2 » de mkvpropedit).</summary>
    public required int Number { get; init; }
    public string Codec { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    /// <summary>Code de langue tel que stocké (« fr », « eng », « zh-TW ») ; vide si aucun.</summary>
    public string LanguageCode { get; init; } = string.Empty;
    public string LanguageName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
    public bool IsForced { get; init; }
    public IReadOnlyList<MediaField> Fields { get; init; } = [];

    public string KindLabel => Kind switch
    {
        MediaInfoStream.Video => L.T("Vidéo"),
        MediaInfoStream.Audio => "Audio",
        MediaInfoStream.Text => L.T("Sous-titres"),
        MediaInfoStream.Image => "Image",
        MediaInfoStream.Menu => L.T("Chapitres"),
        _ => L.T("Autre"),
    };

    /// <summary>« Audio 1 — français — E-AC-3 5.1 — par défaut ».</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{KindLabel} {Number}" };
            if (LanguageName.Length > 0) parts.Add(LanguageName);
            if (Codec.Length > 0) parts.Add(Codec);
            if (Title.Length > 0) parts.Add($"« {Title} »");
            if (IsDefault) parts.Add(L.T("par défaut"));
            if (IsForced) parts.Add(L.T("forcé"));
            return string.Join(" — ", parts);
        }
    }
}

/// <summary>Résumé MediaInfo d'un fichier : informations générales + pistes.</summary>
public sealed class MediaReport
{
    public IReadOnlyList<MediaField> General { get; init; } = [];
    public IReadOnlyList<MediaTrack> Tracks { get; init; } = [];
    /// <summary>Rapport complet de MediaInfo (anglais), pour « Copier ».</summary>
    public string FullText { get; init; } = string.Empty;
    public string Container { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
    /// <summary>Analyse faite sur le début du fichier seulement (fichier d'un serveur).</summary>
    public bool IsPartial { get; init; }

    public MediaTrack? FirstVideo => Tracks.FirstOrDefault(t => t.Kind == MediaInfoStream.Video);

    // ---- Colonnes des panneaux ----

    public string DurationText => Duration is { } d ? (d.TotalHours >= 1 ? d.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : d.ToString(@"m\:ss", CultureInfo.InvariantCulture)) : string.Empty;
    public string ResolutionText { get; init; } = string.Empty;
    public string VideoCodecText { get; init; } = string.Empty;
    public string AudioLanguagesText => string.Join(", ", Tracks.Where(t => t.Kind == MediaInfoStream.Audio)
        .Select(t => t.LanguageCode.Length > 0 ? ShortLanguage(t.LanguageCode) : "?").Distinct());

    /// <summary>« fr », « en » à partir de « fre », « fr-FR », « eng »…</summary>
    public static string ShortLanguage(string code)
    {
        var c = code.Split('-', '_')[0].ToLowerInvariant();
        if (c.Length == 3 && ThreeToTwo.Value.TryGetValue(BibliographicCodes.GetValueOrDefault(c, c), out var two)) return two;
        return c;
    }

    /// <summary>« fra » → « fr », calculé une fois à partir des langues connues de .NET.</summary>
    private static readonly Lazy<Dictionary<string, string>> ThreeToTwo = new(() =>
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
            if (c.TwoLetterISOLanguageName.Length == 2 && c.ThreeLetterISOLanguageName.Length == 3)
                d.TryAdd(c.ThreeLetterISOLanguageName, c.TwoLetterISOLanguageName);
        return d;
    });

    /// <summary>Codes ISO 639-2/B (anciens MKV) → 639-2/T utilisés par .NET.</summary>
    private static readonly Dictionary<string, string> BibliographicCodes = new(StringComparer.Ordinal)
    {
        ["fre"] = "fra", ["ger"] = "deu", ["dut"] = "nld", ["chi"] = "zho", ["cze"] = "ces", ["gre"] = "ell",
        ["per"] = "fas", ["rum"] = "ron", ["slo"] = "slk", ["wel"] = "cym", ["alb"] = "sqi", ["arm"] = "hye",
        ["baq"] = "eus", ["bur"] = "mya", ["geo"] = "kat", ["ice"] = "isl", ["mac"] = "mkd", ["mao"] = "mri",
        ["may"] = "msa", ["tib"] = "bod",
    };

    /// <summary>Nom de la langue dans la langue de Windows (« français ») ; à défaut, celui donné par MediaInfo.</summary>
    public static string LanguageDisplay(string code, string fallback)
    {
        if (code.Length == 0 || code == "und" || code == "zxx") return fallback;
        try
        {
            var shortCode = ShortLanguage(code);
            var full = code.Contains('-') ? code : shortCode;
            var culture = CultureInfo.GetCultureInfo(full);
            if (!string.IsNullOrEmpty(culture.DisplayName) && !culture.DisplayName.StartsWith("Unknown", StringComparison.Ordinal))
                return culture.DisplayName;
        }
        catch (CultureNotFoundException) { }
        return fallback.Length > 0 ? fallback : code;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var f in General) sb.Append(f.Label).Append(" : ").AppendLine(f.Value);
        foreach (var t in Tracks)
        {
            sb.AppendLine().AppendLine(t.Summary);
            foreach (var f in t.Fields) sb.Append("  ").Append(f.Label).Append(" : ").AppendLine(f.Value);
        }
        if (IsPartial) sb.AppendLine().AppendLine(L.T("(analyse faite sur le début du fichier seulement)"));
        return sb.ToString();
    }
}

/// <summary>Lecture des caractéristiques d'un fichier par MediaInfo.</summary>
public static class MediaAnalyzer
{
    private static readonly (string Param, string Label)[] GeneralFields =
    [
        ("Format", L.T("Conteneur")), ("Format_Profile", L.T("Profil")), ("Duration/String3", L.T("Durée")), ("FileSize/String", L.T("Taille")),
        ("OverallBitRate/String", L.T("Débit total")), ("Title", L.T("Titre")), ("Movie", L.T("Film")), ("Album", "Album"), ("Performer", L.T("Artiste")),
        ("Track", L.T("Morceau")), ("Recorded_Date", L.T("Date d'enregistrement")), ("Encoded_Date", L.T("Date d'encodage")),
        ("Encoded_Application", L.T("Créé avec")), ("Encoded_Library", L.T("Bibliothèque d'encodage")), ("Comment", L.T("Commentaire")),
    ];

    private static readonly (string Param, string Label)[] VideoFields =
    [
        ("Format", "Codec"), ("Format_Profile", L.T("Profil")), ("CodecID", L.T("Identifiant")), ("Duration/String3", L.T("Durée")),
        ("BitRate/String", L.T("Débit")), ("FrameRate/String", L.T("Images par seconde")), ("FrameRate_Mode/String", L.T("Mode d'images")),
        ("DisplayAspectRatio/String", L.T("Rapport d'image")), ("BitDepth/String", L.T("Profondeur de couleur")), ("ChromaSubsampling", L.T("Sous-échantillonnage")),
        ("ScanType/String", L.T("Balayage")), ("HDR_Format/String", "HDR"), ("HDR_Format_Compatibility", L.T("Compatibilité HDR")),
        ("colour_primaries", L.T("Primaires")), ("transfer_characteristics", L.T("Transfert")), ("MasteringDisplay_Luminance", L.T("Luminance de mastering")),
        ("MaxCLL", "MaxCLL"), ("MaxFALL", "MaxFALL"), ("StreamSize/String", L.T("Taille de la piste")), ("Encoded_Library_Name", L.T("Encodeur")),
    ];

    private static readonly (string Param, string Label)[] AudioFields =
    [
        ("Format", "Codec"), ("Format_Commercial_IfAny", L.T("Nom commercial")), ("Format_AdditionalFeatures", "Extensions"),
        ("Channel(s)/String", L.T("Canaux")), ("ChannelLayout", L.T("Disposition")), ("SamplingRate/String", L.T("Fréquence")),
        ("BitRate/String", L.T("Débit")), ("BitRate_Mode/String", L.T("Mode de débit")), ("BitDepth/String", L.T("Résolution")),
        ("Compression_Mode/String", "Compression"), ("Duration/String3", L.T("Durée")), ("StreamSize/String", L.T("Taille de la piste")),
    ];

    private static readonly (string Param, string Label)[] TextFields =
    [
        ("Format", "Format"), ("CodecID", L.T("Identifiant")), ("Duration/String3", L.T("Durée")), ("ElementCount", L.T("Nombre de sous-titres")),
        ("StreamSize/String", L.T("Taille de la piste")),
    ];

    private static readonly (string Param, string Label)[] ImageFields =
    [
        ("Format", "Format"), ("BitDepth/String", L.T("Profondeur")), ("ColorSpace", L.T("Espace de couleur")), ("Compression_Mode/String", "Compression"),
    ];

    /// <summary>Analyse un fichier local ; null si MediaInfo n'est pas disponible ou ne reconnaît pas le fichier.</summary>
    public static MediaReport? Analyze(string path, bool partial = false)
    {
        if (!MediaInfoLib.IsAvailable) return null;
        using var mi = new MediaInfoLib();
        if (!mi.Open(path)) return null;

        var general = Fields(mi, MediaInfoStream.General, 0, GeneralFields);
        var tracks = new List<MediaTrack>();
        foreach (var (kind, fields) in new[]
                 {
                     (MediaInfoStream.Video, VideoFields), (MediaInfoStream.Audio, AudioFields),
                     (MediaInfoStream.Text, TextFields), (MediaInfoStream.Image, ImageFields),
                 })
        {
            int count = mi.Count(kind);
            for (int i = 0; i < count; i++)
            {
                var list = Fields(mi, kind, i, fields);
                if (kind is MediaInfoStream.Video or MediaInfoStream.Image)
                {
                    var w = mi.Get(kind, i, "Width");
                    var h = mi.Get(kind, i, "Height");
                    if (w.Length > 0 && h.Length > 0) list.Insert(1, new MediaField(L.T("Résolution"), $"{w} × {h}"));
                }
                var lang = mi.Get(kind, i, "Language");
                tracks.Add(new MediaTrack
                {
                    Kind = kind,
                    Number = i + 1,
                    Codec = CodecText(mi, kind, i),
                    Title = mi.Get(kind, i, "Title"),
                    LanguageCode = lang,
                    LanguageName = MediaReport.LanguageDisplay(lang, mi.Get(kind, i, "Language/String")),
                    IsDefault = mi.Get(kind, i, "Default") == "Yes",
                    IsForced = mi.Get(kind, i, "Forced") == "Yes",
                    Fields = list,
                });
            }
        }
        int chapters = 0;
        for (int i = 0; i < mi.Count(MediaInfoStream.Menu); i++)
        {
            if (int.TryParse(mi.Get(MediaInfoStream.Menu, i, "Chapters_Pos_End"), out var end)
                && int.TryParse(mi.Get(MediaInfoStream.Menu, i, "Chapters_Pos_Begin"), out var begin)) chapters += Math.Max(0, end - begin);
        }
        if (chapters > 0) general.Add(new MediaField(L.T("Chapitres"), chapters.ToString(CultureInfo.CurrentCulture)));

        TimeSpan? duration = double.TryParse(mi.Get(MediaInfoStream.General, 0, "Duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms) : null;
        var video = tracks.FirstOrDefault(t => t.Kind == MediaInfoStream.Video);
        string resolution = string.Empty;
        if (video is not null)
        {
            var w = mi.Get(MediaInfoStream.Video, 0, "Width");
            var h = mi.Get(MediaInfoStream.Video, 0, "Height");
            if (w.Length > 0 && h.Length > 0) resolution = $"{w}×{h}";
        }
        return new MediaReport
        {
            General = general,
            Tracks = tracks,
            FullText = mi.Inform(),
            Container = mi.Get(MediaInfoStream.General, 0, "Format"),
            Duration = duration,
            ResolutionText = resolution,
            VideoCodecText = video is null ? string.Empty : VideoShort(mi),
            IsPartial = partial,
        };
    }

    private static List<MediaField> Fields(MediaInfoLib mi, MediaInfoStream kind, int i, (string Param, string Label)[] wanted)
    {
        var list = new List<MediaField>();
        foreach (var (param, label) in wanted)
        {
            var v = mi.Get(kind, i, param);
            if (v.Length > 0 && !list.Any(f => f.Label == label)) list.Add(new MediaField(label, v));
        }
        return list;
    }

    private static string CodecText(MediaInfoLib mi, MediaInfoStream kind, int i)
    {
        var format = mi.Get(kind, i, "Format");
        if (kind == MediaInfoStream.Audio)
        {
            var channels = mi.Get(kind, i, "Channel(s)");
            var layout = channels switch { "1" => "mono", "2" => L.T("stéréo"), "6" => "5.1", "8" => "7.1", "" => "", _ => channels + L.T(" canaux") };
            var commercial = mi.Get(kind, i, "Format_Commercial_IfAny");
            var name = commercial.Length > 0 ? commercial : format;
            return layout.Length > 0 ? $"{name} {layout}" : name;
        }
        if (kind == MediaInfoStream.Video)
        {
            var hdr = HdrShort(mi.Get(kind, i, "HDR_Format/String"), mi.Get(kind, i, "transfer_characteristics"));
            return hdr.Length > 0 ? $"{format} {hdr}" : format;
        }
        return format;
    }

    /// <summary>« HEVC DV », « HEVC HDR10 », « AV1 HDR10+ »…</summary>
    private static string VideoShort(MediaInfoLib mi)
    {
        var format = mi.Get(MediaInfoStream.Video, 0, "Format");
        var hdr = HdrShort(mi.Get(MediaInfoStream.Video, 0, "HDR_Format/String"), mi.Get(MediaInfoStream.Video, 0, "transfer_characteristics"));
        return hdr.Length > 0 ? $"{format} {hdr}" : format;
    }

    public static string HdrShort(string hdrFormat, string transfer)
    {
        var parts = new List<string>();
        if (hdrFormat.Contains("Dolby Vision", StringComparison.OrdinalIgnoreCase))
        {
            var profile = System.Text.RegularExpressions.Regex.Match(hdrFormat, @"[Pp]rofile\s+([0-9.]+)");
            parts.Add(profile.Success ? $"DV {profile.Groups[1].Value}" : "DV");
        }
        if (hdrFormat.Contains("HDR10+", StringComparison.OrdinalIgnoreCase) || hdrFormat.Contains("SMPTE ST 2094", StringComparison.OrdinalIgnoreCase)) parts.Add("HDR10+");
        else if (hdrFormat.Contains("HDR10", StringComparison.OrdinalIgnoreCase) || hdrFormat.Contains("SMPTE ST 2086", StringComparison.OrdinalIgnoreCase)) parts.Add("HDR10");
        if (parts.Count == 0 && transfer.Contains("HLG", StringComparison.OrdinalIgnoreCase)) parts.Add("HLG");
        else if (parts.Count == 0 && transfer.Contains("PQ", StringComparison.Ordinal)) parts.Add("HDR");
        return string.Join(" ", parts);
    }
}
