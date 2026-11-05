// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetaDirectory = MetadataExtractor.Directory;

namespace TwinPanel.Core.Media;

/// <summary>EXIF / XMP d'une photo, lus avec MetadataExtractor (Apache-2.0) : appareil, réglages, date, GPS, auteur.</summary>
public sealed class PhotoInfo
{
    public IReadOnlyList<MediaField> Main { get; init; } = [];
    /// <summary>Toutes les balises, groupées (« Exif IFD0 », « GPS », « XMP »…).</summary>
    public IReadOnlyList<(string Group, MediaField Field)> All { get; init; } = [];
    public string Artist { get; init; } = string.Empty;
    public string Copyright { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime? Taken { get; init; }
    public bool HasGps { get; init; }

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".heic", ".heif", ".webp", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".raf",
    };

    public static bool IsPhoto(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>null si le fichier n'a pas de métadonnées lisibles.</summary>
    public static PhotoInfo? Read(string path)
    {
        IReadOnlyList<MetaDirectory> dirs;
        try { dirs = ImageMetadataReader.ReadMetadata(path); }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or UnauthorizedAccessException) { return null; }
        if (dirs.Count == 0) return null;

        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = dirs.OfType<GpsDirectory>().FirstOrDefault();

        string D(MetaDirectory? d, int tag) => d?.GetDescription(tag)?.Trim() ?? string.Empty;
        var main = new List<MediaField>();
        void Add(string label, string value) { if (value.Length > 0) main.Add(new MediaField(label, value)); }

        Add(L.T("Appareil"), $"{D(ifd0, ExifDirectoryBase.TagMake)} {D(ifd0, ExifDirectoryBase.TagModel)}".Trim());
        Add(L.T("Objectif"), D(sub, ExifDirectoryBase.TagLensModel));
        Add(L.T("Prise de vue"), D(sub, ExifDirectoryBase.TagDateTimeOriginal));
        Add(L.T("Exposition"), D(sub, ExifDirectoryBase.TagExposureTime));
        Add(L.T("Ouverture"), D(sub, ExifDirectoryBase.TagFNumber));
        Add("ISO", D(sub, ExifDirectoryBase.TagIsoEquivalent));
        Add(L.T("Focale"), D(sub, ExifDirectoryBase.TagFocalLength));
        Add("Flash", D(sub, ExifDirectoryBase.TagFlash));
        var w = D(sub, ExifDirectoryBase.TagExifImageWidth);
        var h = D(sub, ExifDirectoryBase.TagExifImageHeight);
        if (w.Length > 0 && h.Length > 0) Add("Dimensions", $"{w} × {h}");
        Add("Orientation", D(ifd0, ExifDirectoryBase.TagOrientation));
        bool hasGps = false;
        if (gps is not null && gps.TryGetGeoLocation(out var geo) && !geo.IsZero)
        {
            hasGps = true;
            Add("GPS", string.Create(CultureInfo.InvariantCulture, $"{geo.Latitude:0.000000}, {geo.Longitude:0.000000}"));
        }
        var artist = D(ifd0, ExifDirectoryBase.TagArtist);
        var copyright = D(ifd0, ExifDirectoryBase.TagCopyright);
        var description = D(ifd0, ExifDirectoryBase.TagImageDescription);
        Add(L.T("Auteur"), artist);
        Add("Copyright", copyright);
        Add("Description", description);
        Add(L.T("Logiciel"), D(ifd0, ExifDirectoryBase.TagSoftware));

        DateTime? taken = null;
        if (sub is not null && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt)) taken = dt;

        var all = new List<(string, MediaField)>();
        foreach (var d in dirs)
            foreach (var t in d.Tags)
                if (!string.IsNullOrWhiteSpace(t.Description)) all.Add((d.Name, new MediaField(t.Name, t.Description!.Trim())));

        return new PhotoInfo
        {
            Main = main, All = all, Artist = artist, Copyright = copyright, Description = description, Taken = taken, HasGps = hasGps,
        };
    }
}
