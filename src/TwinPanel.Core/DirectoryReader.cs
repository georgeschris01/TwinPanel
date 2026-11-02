// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core;

public static class DirectoryReader
{
    /// <summary>Lire aussi le propriétaire de chaque élément (colonne « Propriétaire » affichée) : un peu plus lent.</summary>
    public static bool ReadOwners { get; set; }

    /// <summary>
    /// Lit le contenu d'un dossier : ".." d'abord, puis les dossiers, puis les fichiers,
    /// triés par nom sans tenir compte de la casse.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">Le dossier n'existe pas.</exception>
    /// <exception cref="UnauthorizedAccessException">Accès refusé au dossier.</exception>
    public static IReadOnlyList<FileEntry> Read(string path, bool showHidden = false)
    {
        var dir = new DirectoryInfo(path);
        if (!dir.Exists)
            throw new DirectoryNotFoundException(L.F("Dossier introuvable : {0}", path));

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,       // ignore les éléments illisibles au lieu d'échouer
            AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false,
        };

        var result = new List<FileEntry>();
        if (dir.Parent is { } parent)
            result.Add(new FileEntry("..", parent.FullName, true, 0, default, FileAttributes.Directory, IsParentLink: true));

        // Une seule énumération pour éviter deux allers-retours disque.
        var items = dir.EnumerateFileSystemInfos("*", options)
            .Select(ToEntry)
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);

        result.AddRange(items);
        return result;
    }

    private static FileEntry ToEntry(FileSystemInfo info)
    {
        var e = info switch
        {
            FileInfo f => new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime, f.Attributes),
            _ => new FileEntry(info.Name, info.FullName, true, 0, info.LastWriteTime, info.Attributes),
        };
        if (OperatingSystem.IsWindows())
        {
            // NTFS : seulement le propriétaire (compte Windows) ; pas de droits « 755 » ni de groupe.
            if (ReadOwners && Rights.WindowsSecurity.TryOwnerName(info.FullName) is { } owner) e = e with { Owner = owner };
        }
        else
        {
            try { e = e with { Mode = (int)info.UnixFileMode & Rights.UnixMode.Mask }; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return e;
    }
}
