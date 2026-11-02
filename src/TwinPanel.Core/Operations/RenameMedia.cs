// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using TwinPanel.Core.Media;

namespace TwinPanel.Core.Operations;

/// <summary>Valeurs des balises médias de Ctrl+M ([Titre], [Artiste]…), lues dans les fichiers du PC.</summary>
public static class RenameMedia
{
    public const string Title = "Titre";
    public const string Artist = "Artiste";
    public const string Album = "Album";
    public const string Track = "Piste";
    public const string Year = "Année";
    public const string Genre = "Genre";
    public const string Resolution = "Résolution";
    public const string Duration = "Durée";
    public const string Taken = "Date de prise de vue";

    /// <summary>Nom français d'une balise, celle-ci pouvant être écrite en français ou en anglais.</summary>
    public static string Canonical(string tag) => tag switch
    {
        "Title" => Title,
        "Artist" => Artist,
        "Track" => Track,
        "Year" => Year,
        "Resolution" => Resolution,
        "Duration" => Duration,
        "Date taken" => Taken,
        _ => tag,
    };

    private const int TrackDigits = 2;
    private const string ForbiddenInNames = "/\\:*?\"<>|";

    /// <summary>Vide si le fichier n'a rien de lisible : les balises concernées seront signalées comme absentes.</summary>
    public static IReadOnlyDictionary<string, string> Read(string path, long size, DateTime modified)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        void Set(string key, string? value)
        {
            if (!values.ContainsKey(key) && Clean(value) is { Length: > 0 } clean) values[key] = clean;
        }

        if (AudioTags.CanEdit(path)) Guarded(() => ReadTags(path, Set));
        if (PhotoInfo.IsPhoto(path)) Guarded(() => ReadPhoto(path, Set));
        if (MediaCache.Get(path, size, modified) is { } report)
        {
            Set(Resolution, report.ResolutionText);
            if (report.Duration is { } duration) Set(Duration, FormatDuration(duration));
            Set(Title, General(report, L.T("Titre")));
            Set(Artist, General(report, L.T("Artiste")));
            Set(Album, General(report, "Album"));
        }
        return values;
    }

    /// <summary>Fichier protégé ou abîmé : ses balises seront signalées comme absentes, et les autres fichiers continuent.</summary>
    private static void Guarded(Action read)
    {
        try { read(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException) { }
    }

    private static void ReadTags(string path, Action<string, string?> set)
    {
        var tags = AudioTags.Read(path);
        set(Title, tags.Title);
        set(Artist, tags.Artist.Length > 0 ? tags.Artist : tags.AlbumArtist);
        set(Album, tags.Album);
        set(Track, FormatTrack(tags.TrackNumber));
        set(Year, tags.Year);
        set(Genre, tags.Genre);
    }

    private static void ReadPhoto(string path, Action<string, string?> set)
    {
        if (PhotoInfo.Read(path) is not { } photo) return;
        set(Artist, photo.Artist);
        if (photo.Taken is { } taken)
        {
            set(Taken, taken.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
            set(Year, taken.ToString("yyyy", CultureInfo.InvariantCulture));
        }
        set(Resolution, photo.Main.FirstOrDefault(f => f.Label == "Dimensions")?.Value.Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    private static string? General(MediaReport report, string label) => report.General.FirstOrDefault(f => f.Label == label)?.Value;

    /// <summary>« 3 » et « 3/12 » donnent « 03 », pour que les morceaux se suivent dans l'ordre ; « A1 » reste tel quel.</summary>
    public static string FormatTrack(string? track)
    {
        var first = (track ?? string.Empty).Split('/')[0].Trim();
        return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number.ToString(new string('0', TrackDigits), CultureInfo.InvariantCulture)
            : first;
    }

    /// <summary>« 3m07s » ou « 1h23m45s » : les deux-points sont interdits dans un nom de fichier.</summary>
    public static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}h{duration.Minutes:00}m{duration.Seconds:00}s")
        : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes}m{duration.Seconds:00}s");

    /// <summary>Un tag peut contenir n'importe quoi (« AC/DC », « Quoi ? ») : le nom doit rester valable sur tous les systèmes.</summary>
    public static string Clean(string? value)
    {
        var chars = (value ?? string.Empty).Select(c => char.IsControl(c) || ForbiddenInNames.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim().TrimEnd('.').TrimEnd();
    }
}
