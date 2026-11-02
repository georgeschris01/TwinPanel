// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Navigation;

public sealed record BreadcrumbSegment(string Name, string FullPath);

public static class Breadcrumb
{
    /// <summary>Découpe "C:\Users\georg" en [C:\, Users, georg] avec le chemin complet de chaque niveau.</summary>
    public static IReadOnlyList<BreadcrumbSegment> Split(string path)
    {
        var result = new List<BreadcrumbSegment>();
        if (Remote.PanelRemotePath.TryParse(path, out var remote))
        {
            // sftp://chrypox/home/x → [chrypox, home, x]
            result.Add(new BreadcrumbSegment(remote.Session, remote.WithPath("/").ToString()));
            var current = "/";
            foreach (var part in remote.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                current = Remote.RemotePath.Combine(current, part);
                result.Add(new BreadcrumbSegment(part, remote.WithPath(current).ToString()));
            }
            return result;
        }
        var dir = new DirectoryInfo(path);
        while (dir is not null)
        {
            var name = dir.Parent is null ? dir.FullName.TrimEnd('\\', '/') : dir.Name;
            if (name.Length == 0) name = dir.FullName; // racine Unix "/"
            result.Add(new BreadcrumbSegment(name, dir.FullName));
            dir = dir.Parent;
        }
        result.Reverse();
        return result;
    }
}
