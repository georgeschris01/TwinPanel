// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Search;

/// <summary>Vue à plat (Ctrl+B) : tous les fichiers des sous-dossiers dans une seule liste.</summary>
public static class FlatListing
{
    /// <summary>Au-delà, la lecture s'arrête : la mémoire et la liste restent raisonnables.</summary>
    public const int DefaultLimit = 200_000;

    /// <summary>Les fichiers seulement ; sans les cachés, leurs dossiers ne sont pas parcourus non plus (.git…).</summary>
    public static SearchCriteria Criteria(string root, bool showHidden) =>
        new() { Root = root, IncludeFolders = false, SkipHidden = !showHidden };

    /// <summary>Colonne « Dossier » : chemin du dossier du fichier depuis le dossier de départ (vide : le départ lui-même).</summary>
    public static string RelativeFolder(string root, string fullPath)
    {
        int cut = fullPath.LastIndexOfAny(['/', '\\']);
        if (cut < 0) return string.Empty;
        var folder = fullPath[..cut];
        var start = root.TrimEnd('/', '\\');
        return folder.StartsWith(start, PathText.Comparison)
            ? folder[start.Length..].TrimStart('/', '\\')
            : folder;
    }

    /// <summary>Noms présents plusieurs fois (majuscules ignorées : ils iraient dans un seul dossier, peut-être sous Windows).</summary>
    public static IReadOnlyList<string> DuplicateNames(IEnumerable<string> names) =>
        names.GroupBy(n => n, PathText.Comparer).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

    /// <summary>Répartit les éléments en groupes où chaque nom n'apparaît qu'une fois, dans l'ordre d'origine.</summary>
    public static IReadOnlyList<IReadOnlyList<T>> SplitUniqueNames<T>(IEnumerable<T> items, Func<T, string> name)
    {
        var groups = new List<List<T>>();
        var seen = new Dictionary<string, int>(PathText.Comparer);
        foreach (var item in items)
        {
            int round = seen.GetValueOrDefault(name(item));
            seen[name(item)] = round + 1;
            if (round == groups.Count) groups.Add([]);
            groups[round].Add(item);
        }
        return groups;
    }
}
