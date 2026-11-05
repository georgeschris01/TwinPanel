// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core;

/// <summary>Comparaison de deux chemins locaux : Linux distingue les majuscules, pas Windows.</summary>
public static class PathText
{
    public static StringComparison Comparison { get; } = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

/// <summary>
/// Les disques à montrer. Sous Linux, le système liste aussi ses dizaines de montages internes (/proc, /sys, /run…),
/// qui ne sont pas des disques : seuls restent la racine et les vrais supports.
/// </summary>
public static class LocalDisks
{
    private static readonly string[] SystemFolders = ["/proc", "/sys", "/dev", "/run", "/snap", "/boot", "/var/lib/docker"];
    private static readonly string[] RemovableFolders = ["/media", "/run/media"];

    public static IReadOnlyList<DriveInfo> Usable()
    {
        var drives = DriveInfo.GetDrives();
        return OperatingSystem.IsWindows() ? drives : [.. drives.Where(d => IsForUser(d.Name, d.DriveType))];
    }

    internal static bool IsForUser(string mount, DriveType type)
    {
        if (mount == "/") return true;
        if (type is DriveType.Ram or DriveType.Unknown or DriveType.NoRootDirectory) return false;
        return IsRemovable(mount) || !SystemFolders.Any(folder => IsInside(mount, folder));
    }

    /// <summary>Monté par le bureau : clé USB, disque externe.</summary>
    public static bool IsRemovable(string mount) => RemovableFolders.Any(folder => IsInside(mount, folder));

    /// <summary>Nom court : celui du dossier de montage, la racine reste « / ».</summary>
    public static string ShortName(string mount)
    {
        var trimmed = mount.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : Path.GetFileName(trimmed);
    }

    /// <summary>Parmi ces dossiers, le plus précis qui contient le chemin.</summary>
    public static string? Containing(string path, IEnumerable<string> folders) =>
        folders.Where(folder => IsInside(path, folder)).OrderByDescending(folder => folder.Length).FirstOrDefault();

    private static bool IsInside(string path, string folder)
    {
        var parent = folder.TrimEnd('/');
        return path.Length == parent.Length ? string.Equals(path, parent, StringComparison.Ordinal)
            : path.StartsWith(parent + "/", StringComparison.Ordinal);
    }
}
