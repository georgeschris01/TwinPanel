// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core;

/// <summary>Une ligne affichée dans un panneau (fichier, dossier ou lien "..").</summary>
public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastWriteTime,
    FileAttributes Attributes,
    bool IsParentLink = false)
{
    /// <summary>Extension sans le point. Un fichier « .gitignore » n'a pas d'extension (c'est son nom).</summary>
    public string Extension => IsDirectory || Path.GetFileNameWithoutExtension(Name).Length == 0
        ? string.Empty
        : Path.GetExtension(Name).TrimStart('.');

    /// <summary>Nom affiché : les dossiers entre crochets, comme dans Total Commander.</summary>
    public string DisplayName => IsParentLink ? "[..]" : IsDirectory ? $"[{Name}]" : Path.GetFileNameWithoutExtension(Name) is { Length: > 0 } n ? n : Name;

    public string DisplaySize => IsDirectory ? "<DIR>" : FormatListSize(Size);

    public string DisplayDate => IsParentLink ? string.Empty : FormatDate(LastWriteTime);

    // ---- Formats d'affichage choisis dans les Options (lot 7) ----

    /// <summary>Format des dates dans les panneaux (Options → Affichage).</summary>
    public static string DateFormat { get; set; } = "dd/MM/yyyy HH:mm";

    /// <summary>Façon d'afficher les tailles dans les panneaux.</summary>
    public static Settings.SizeDisplay SizeDisplay { get; set; } = Settings.SizeDisplay.Auto;

    public static string FormatDate(DateTime date)
    {
        try { return date.ToString(DateFormat); }
        catch (FormatException) { return date.ToString("dd/MM/yyyy HH:mm"); }
    }

    /// <summary>Taille dans la colonne « Taille » selon le réglage (auto, octets, Ko).</summary>
    public static string FormatListSize(long bytes) => SizeDisplay switch
    {
        Settings.SizeDisplay.Bytes => bytes.ToString("N0") + L.T(" o"),
        Settings.SizeDisplay.Kilobytes => Math.Ceiling(bytes / 1024.0).ToString("N0") + L.T(" Ko"),
        _ => FormatSize(bytes),
    };

    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;

    // ---- Droits Unix / propriétaire (serveurs, Linux) : colonnes facultatives ----

    /// <summary>Droits Unix sur 12 bits ; -1 : inconnus (disque Windows, WebDAV…).</summary>
    public int Mode { get; init; } = -1;
    public int UserId { get; init; } = -1;
    public int GroupId { get; init; } = -1;
    /// <summary>Noms donnés par le serveur (vides s'il ne les donne pas).</summary>
    public string Owner { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;

    /// <summary>Propriétaire / groupe affichés en numéro (uid / gid) plutôt qu'en nom (Options → Affichage).</summary>
    public static bool OwnerAsNumber { get; set; }

    /// <summary>« 755 », « 4755 » ; vide si inconnu.</summary>
    public string DisplayMode => IsParentLink || Mode < 0 ? string.Empty : Rights.UnixMode.ToOctal(Mode);
    public string DisplayOwner => IsParentLink ? string.Empty : Account(Owner, UserId);
    public string DisplayGroup => IsParentLink ? string.Empty : Account(Group, GroupId);

    /// <summary>Nom ou numéro selon le réglage ; à défaut, ce qui est connu.</summary>
    public static string Account(string name, int id)
    {
        var number = id >= 0 ? id.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        if (OwnerAsNumber) return number.Length > 0 ? number : name;
        return name.Length > 0 ? name : number;
    }

    /// <summary>Catégorie utilisée pour l'icône et la couleur.</summary>
    public FileCategory Category => IsParentLink ? FileCategory.Parent
        : IsDirectory ? FileCategory.Folder
        : FileCategories.FromExtension(Extension);

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => L.F("{0} o", bytes),
        < 1024L * 1024 => L.F("{0:0.#} Ko", bytes / 1024.0),
        < 1024L * 1024 * 1024 => L.F("{0:0.#} Mo", bytes / (1024.0 * 1024)),
        < 1024L * 1024 * 1024 * 1024 => L.F("{0:0.##} Go", bytes / (1024.0 * 1024 * 1024)),
        _ => L.F("{0:0.##} To", bytes / (1024.0 * 1024 * 1024 * 1024)),
    };
}
