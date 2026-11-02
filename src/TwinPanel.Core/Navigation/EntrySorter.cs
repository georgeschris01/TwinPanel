// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Navigation;

/// <summary>Colonne de tri. Folder : colonne « Dossier » de la vue à plat.</summary>
public enum SortColumn { Name, Extension, Size, Date, Mode, Owner, Group, Folder }

/// <summary>Tri façon Total Commander : ".." en premier, puis dossiers, puis fichiers.</summary>
public static class EntrySorter
{
    /// <param name="folderSize">Taille connue d'un dossier, pour le tri par taille ; un dossier sans taille passe pour le plus petit.</param>
    public static IReadOnlyList<FileEntry> Sort(IEnumerable<FileEntry> entries, SortColumn column, bool descending, Func<FileEntry, long?>? folderSize = null)
    {
        var list = entries.ToList();
        var parent = list.Where(e => e.IsParentLink);
        var dirs = list.Where(e => e.IsDirectory && !e.IsParentLink);
        var files = list.Where(e => !e.IsDirectory);

        var byName = StringComparer.CurrentCultureIgnoreCase;

        // Les dossiers n'ont ni taille (sauf calculée) ni extension : tri par date, sinon par nom
        // (ordre inversé seulement quand on trie explicitement par nom).
        var sortedDirs = column switch
        {
            SortColumn.Mode => Order(dirs, e => e.Mode, descending).ThenBy(e => e.Name, byName),
            SortColumn.Owner => Order(dirs, e => e.DisplayOwner, descending, byName).ThenBy(e => e.Name, byName),
            SortColumn.Group => Order(dirs, e => e.DisplayGroup, descending, byName).ThenBy(e => e.Name, byName),
            SortColumn.Date => Order(dirs, e => e.LastWriteTime, descending).ThenBy(e => e.Name, byName),
            SortColumn.Name => Order(dirs, e => e.Name, descending, byName),
            SortColumn.Size when folderSize is not null => Order(dirs, e => folderSize(e) ?? -1, descending).ThenBy(e => e.Name, byName),
            _ => Order(dirs, e => e.Name, false, byName),
        };

        var sortedFiles = column switch
        {
            SortColumn.Mode => Order(files, e => e.Mode, descending).ThenBy(e => e.Name, byName),
            SortColumn.Owner => Order(files, e => e.DisplayOwner, descending, byName).ThenBy(e => e.Name, byName),
            SortColumn.Group => Order(files, e => e.DisplayGroup, descending, byName).ThenBy(e => e.Name, byName),
            SortColumn.Extension => Order(files, e => e.Extension, descending, byName).ThenBy(e => e.Name, byName),
            SortColumn.Size => Order(files, e => e.Size, descending).ThenBy(e => e.Name, byName),
            SortColumn.Date => Order(files, e => e.LastWriteTime, descending).ThenBy(e => e.Name, byName),
            SortColumn.Folder => Order(files, e => FolderOf(e.FullPath), descending, byName).ThenBy(e => e.Name, byName),
            _ => Order(files, e => e.Name, descending, byName),
        };

        return parent.Concat(sortedDirs).Concat(sortedFiles).ToList();
    }

    private static string FolderOf(string path) => path[..Math.Max(0, path.LastIndexOfAny(['/', '\\']))];

    private static IOrderedEnumerable<FileEntry> Order<TKey>(IEnumerable<FileEntry> source, Func<FileEntry, TKey> key,
        bool descending, IComparer<TKey>? comparer = null) =>
        descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer);
}
