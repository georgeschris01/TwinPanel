// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Archives;

/// <summary>
/// Chemins « dans » une archive, façon Total Commander : D:\photos\vacances.zip\2024\plage.jpg
/// = fichier « 2024/plage.jpg » de l'archive D:\photos\vacances.zip.
/// </summary>
public static class ArchivePath
{
    private static readonly string[] Extensions =
        [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".xz", ".txz", ".jar", ".cbz", ".cbr", ".cb7"];

    public static bool IsArchiveFile(string path) =>
        Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Découpe un chemin en (archive, chemin intérieur avec « / »). Faux si le chemin n'est pas dans une archive.
    /// Le chemin de l'archive elle-même donne un chemin intérieur vide.
    /// </summary>
    public static bool TrySplit(string path, out string archive, out string inner)
    {
        archive = inner = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return false;

        var full = path.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        var candidate = full;
        while (!string.IsNullOrEmpty(candidate))
        {
            if (IsArchiveFile(candidate) && File.Exists(candidate))
            {
                archive = candidate;
                inner = full.Length > candidate.Length
                    ? full[(candidate.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/')
                    : string.Empty;
                return true;
            }
            // Un dossier réel qui existe ne peut pas être dans une archive : inutile de remonter plus.
            if (Directory.Exists(candidate)) return false;
            candidate = Path.GetDirectoryName(candidate);
        }
        return false;
    }

    /// <summary>Chemin affiché pour un élément intérieur (séparateurs du système).</summary>
    public static string Combine(string archive, string inner) =>
        inner.Length == 0 ? archive : archive + Path.DirectorySeparatorChar + inner.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Nettoie un nom d'entrée d'archive : « / » partout, pas de « ./ » ni de « / » au début ou à la fin.</summary>
    public static string NormalizeKey(string key)
    {
        var k = key.Replace('\\', '/');
        while (k.StartsWith("./", StringComparison.Ordinal)) k = k[2..];
        return k.Trim('/');
    }
}
