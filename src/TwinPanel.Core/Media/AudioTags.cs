// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using ATL;

namespace TwinPanel.Core.Media;

/// <summary>
/// Tags d'un morceau (ou d'une vidéo MP4 / M4V pour titre, année, description, pochette) lus et écrits avec
/// ATL .NET (licence MIT). Un champ null dans <see cref="TagChanges"/> n'est pas touché, une chaîne vide l'efface.
/// </summary>
public sealed record TagValues
{
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string AlbumArtist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public string Year { get; init; } = string.Empty;
    public string TrackNumber { get; init; } = string.Empty;
    public string TrackTotal { get; init; } = string.Empty;
    public string DiscNumber { get; init; } = string.Empty;
    public string Genre { get; init; } = string.Empty;
    public string Composer { get; init; } = string.Empty;
    public string Comment { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public byte[]? Cover { get; init; }
    /// <summary>Débit, fréquence, durée… (lecture seule).</summary>
    public string Technical { get; init; } = string.Empty;
}

/// <summary>Ce qu'il faut changer (null : ne pas toucher ; "" : effacer).</summary>
public sealed record TagChanges
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Album { get; init; }
    public string? Year { get; init; }
    public string? TrackNumber { get; init; }
    public string? TrackTotal { get; init; }
    public string? DiscNumber { get; init; }
    public string? Genre { get; init; }
    public string? Composer { get; init; }
    public string? Comment { get; init; }
    public string? Description { get; init; }
    /// <summary>Nouvelle pochette (null : ne pas toucher).</summary>
    public byte[]? Cover { get; init; }
    public bool RemoveCover { get; init; }

    public bool IsEmpty => this == new TagChanges();
}

public static class AudioTags
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".mp2", ".flac", ".m4a", ".m4b", ".mp4", ".m4v", ".aac", ".ogg", ".oga", ".opus", ".spx", ".wma", ".asf",
        ".wav", ".aif", ".aiff", ".ape", ".wv", ".mpc", ".dsf", ".mka", ".tta", ".ac3", ".dts",
    };

    /// <summary>Fichiers dont ATL sait modifier les tags.</summary>
    public static bool CanEdit(string path) => Supported.Contains(Path.GetExtension(path));

    public static bool IsVideoContainer(string path) => Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".m4v";

    public static TagValues Read(string path)
    {
        var t = new Track(path);
        var cover = t.EmbeddedPictures.FirstOrDefault(p => p.PicType == PictureInfo.PIC_TYPE.Front) ?? t.EmbeddedPictures.FirstOrDefault();
        var tech = new List<string>();
        if (t.AudioFormat?.Name is { Length: > 0 } fmt) tech.Add(fmt);
        if (t.Bitrate > 0) tech.Add($"{t.Bitrate} kb/s{(t.IsVBR ? " (variable)" : string.Empty)}");
        if (t.SampleRate > 0) tech.Add($"{(t.SampleRate / 1000).ToString("0.#", CultureInfo.CurrentCulture)} kHz");
        if (t.BitDepth > 0) tech.Add($"{t.BitDepth} bits");
        if (t.ChannelsArrangement?.Description is { Length: > 0 } ch) tech.Add(ch);
        if (t.DurationMs > 0) tech.Add(TimeSpan.FromMilliseconds(t.DurationMs).ToString(t.DurationMs >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture));
        return new TagValues
        {
            Title = t.Title ?? string.Empty,
            Artist = t.Artist ?? string.Empty,
            AlbumArtist = t.AlbumArtist ?? string.Empty,
            Album = t.Album ?? string.Empty,
            Year = t.Year is > 0 ? t.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            TrackNumber = t.TrackNumberStr ?? string.Empty,
            TrackTotal = t.TrackTotal is > 0 ? t.TrackTotal.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            DiscNumber = t.DiscNumber is > 0 ? t.DiscNumber.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            Genre = t.Genre ?? string.Empty,
            Composer = t.Composer ?? string.Empty,
            Comment = t.Comment ?? string.Empty,
            Description = t.Description ?? string.Empty,
            Cover = cover?.PictureData,
            Technical = string.Join(" — ", tech),
        };
    }

    /// <summary>Écrit les changements ; IOException si ATL n'y arrive pas (fichier protégé, format abîmé…).</summary>
    public static void Write(string path, TagChanges c)
    {
        if (c.IsEmpty) return;
        var t = new Track(path);
        if (c.Title is { } title) t.Title = title;
        if (c.Artist is { } artist) t.Artist = artist;
        if (c.AlbumArtist is { } albumArtist) t.AlbumArtist = albumArtist;
        if (c.Album is { } album) t.Album = album;
        if (c.Year is { } year) t.Year = int.TryParse(year.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y > 0 ? y : null;
        if (c.TrackNumber is { } track) t.TrackNumberStr = track.Trim();
        if (c.TrackTotal is { } total) t.TrackTotal = int.TryParse(total.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
        if (c.DiscNumber is { } disc) t.DiscNumber = int.TryParse(disc.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var dn) && dn > 0 ? dn : null;
        if (c.Genre is { } genre) t.Genre = genre;
        if (c.Composer is { } composer) t.Composer = composer;
        if (c.Comment is { } comment) t.Comment = comment;
        if (c.Description is { } description) t.Description = description;
        if (c.RemoveCover || c.Cover is not null)
        {
            var keep = t.EmbeddedPictures.Where(p => p.PicType != PictureInfo.PIC_TYPE.Front).ToList();
            t.EmbeddedPictures.Clear();
            foreach (var p in keep) t.EmbeddedPictures.Add(p);
            if (c.Cover is { Length: > 0 } bytes) t.EmbeddedPictures.Add(PictureInfo.fromBinaryData(bytes, PictureInfo.PIC_TYPE.Front));
        }
        if (!t.Save()) throw new IOException(L.F("« {0} » : les tags n'ont pas pu être enregistrés (fichier protégé ou format non pris en charge).", Path.GetFileName(path)));
    }
}
