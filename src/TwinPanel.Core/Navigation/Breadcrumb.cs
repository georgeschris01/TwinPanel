// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Navigation;

public sealed record BreadcrumbSegment(string Name, string FullPath);

public static class Breadcrumb
{
    /// <summary>Découpe "C:\Users\georg" en [C:\, Users, georg] avec le chemin complet de chaque niveau.</summary>
    public static IReadOnlyList<BreadcrumbSegment> Split(string path)
    {
        var result = new List<BreadcrumbSegment>();
        if (Trash.RecycleBin.IsPanelPath(path))
        {
            result.Add(new BreadcrumbSegment(L.T("Corbeille"), Trash.RecycleBin.PanelPath));
            return result;
        }
        if (Trash.RecycleBin.ItemRootOf(path) is { } trashed)
        {
            // Dans un dossier supprimé : « Corbeille › vrai nom › sous-dossiers », pas le nom interne (« $R… »).
            result.Add(new BreadcrumbSegment(L.T("Corbeille"), Trash.RecycleBin.PanelPath));
            result.Add(new BreadcrumbSegment(Trash.RecycleBin.RealNameOf(trashed), trashed));
            var below = trashed;
            foreach (var part in path.TrimEnd('/', '\\')[trashed.Length..].Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
            {
                below = System.IO.Path.Combine(below, part);
                result.Add(new BreadcrumbSegment(part, below));
            }
            return result;
        }
        if (Remote.ServerArchives.Find(path) is { } server)
        {
            // Copie d'une archive d'un serveur : on montre où elle est vraiment, pas le dossier temporaire.
            result.AddRange(Split(server.FolderPanelPath));
            result.Add(new BreadcrumbSegment(server.Name, server.LocalPath));
            Archives.ArchivePath.TrySplit(path, out _, out var inner);
            var key = string.Empty;
            foreach (var part in inner.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                key = key.Length == 0 ? part : key + "/" + part;
                result.Add(new BreadcrumbSegment(part, Archives.ArchivePath.Combine(server.LocalPath, key)));
            }
            return result;
        }
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
