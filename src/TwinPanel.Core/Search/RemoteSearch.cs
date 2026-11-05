// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.CompilerServices;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Selection;

namespace TwinPanel.Core.Search;

/// <summary>
/// Recherche (Alt+F7) sur un serveur : mêmes critères que sur le disque.
/// Le texte dans les fichiers oblige à télécharger chaque fichier candidat : ceux de plus de 16 Mo sont ignorés.
/// </summary>
public static class RemoteSearch
{
    public const long MaxContentSize = 16L * 1024 * 1024;

    public static async IAsyncEnumerable<FileEntry> SearchAsync(IRemoteFileSystem fs, PanelRemotePath root, SearchCriteria c,
        IProgress<string>? currentFolder = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var mask = new MaskMatcher(c.NameMask);
        var textMatcher = FileSearch.BuildTextMatcher(c);
        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((root.Path, 0));
        bool first = true;

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = pending.Pop();
            currentFolder?.Report(root.WithPath(dir).ToString());

            IReadOnlyList<RemoteItem> items;
            try { items = await fs.ListAsync(dir, ct).ConfigureAwait(false); }
            catch (RemoteException) when (!first) { continue; } // dossier interdit en cours de route : on passe
            first = false;

            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (c.SkipHidden && item.Name.StartsWith('.')) continue;
                // Les liens vers des dossiers ne sont pas suivis (risque de boucle sans fin).
                if (item.IsDirectory && !item.IsLink && (c.MaxDepth is null || depth < c.MaxDepth))
                    pending.Push((item.FullPath, depth + 1));

                if (item.IsDirectory ? !c.IncludeFolders : !c.IncludeFiles) continue;
                if (!mask.IsMatch(item.Name)) continue;
                if (c.ModifiedAfter is { } after && item.Modified < after) continue;
                if (c.ModifiedBefore is { } before && item.Modified > before) continue;
                if (!item.IsDirectory && c.MinSize is { } min && item.Size < min) continue;
                if (!item.IsDirectory && c.MaxSize is { } max && item.Size > max) continue;

                if (textMatcher is not null)
                {
                    if (item.IsDirectory || item.Size > MaxContentSize) continue;
                    byte[] data;
                    try
                    {
                        using var ms = new MemoryStream();
                        await fs.DownloadAsync(item.FullPath, ms, ct).ConfigureAwait(false);
                        data = ms.ToArray();
                    }
                    catch (RemoteException) { continue; } // illisible (droits) : on passe
                    if (!FileSearch.BytesContain(data, textMatcher)) continue;
                }

                var attrs = item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
                if (item.Name.StartsWith('.')) attrs |= FileAttributes.Hidden;
                if (item.IsLink) attrs |= FileAttributes.ReparsePoint;
                yield return new FileEntry(item.Name, root.WithPath(item.FullPath).ToString(), item.IsDirectory, item.Size, item.Modified, attrs)
                {
                    Mode = Rights.UnixMode.Of(item),
                    UserId = item.Uid,
                    GroupId = item.Gid,
                    Owner = item.Owner,
                    Group = item.Group,
                };
            }
        }
    }
}
