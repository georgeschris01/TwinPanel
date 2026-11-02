// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Sources;

namespace TwinPanel.Core.Archives;

/// <summary>
/// Affiche le contenu d'une archive dans un panneau, comme un dossier (Entrée pour entrer, Retour arrière pour sortir).
/// Les modifications passent par <see cref="ArchiveEditor"/>.
/// </summary>
public sealed class ArchiveFileSource : IFileSource
{
    public string Id => "archive";
    public bool SupportsWatching => false;

    public bool CanHandle(string path) => ArchivePath.TrySplit(path, out _, out _);

    public string Normalize(string path)
    {
        ArchivePath.TrySplit(path, out var archive, out var inner);
        return ArchivePath.Combine(Path.GetFullPath(archive), inner);
    }

    public string? GetParent(string path)
    {
        if (!ArchivePath.TrySplit(path, out var archive, out var inner)) return null;
        return inner.Length == 0
            ? Path.GetDirectoryName(archive) // à la racine de l'archive : on ressort dans son dossier
            : ArchivePath.Combine(archive, ArchiveIndex.ParentOf(inner));
    }

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, bool showHidden, CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
        {
            if (!ArchivePath.TrySplit(path, out var archive, out var inner))
                throw new DirectoryNotFoundException(L.F("Archive introuvable : {0}", path));

            var index = ArchiveIndex.Load(archive);
            if (index.IsEncrypted && ArchivePasswords.Get(archive) is null) throw new ArchivePasswordException(archive, wrong: false);
            if (!index.DirectoryExists(inner))
                throw new DirectoryNotFoundException(L.F("Dossier introuvable dans l'archive : {0}", inner));

            var result = new List<FileEntry>();
            if (GetParent(path) is { } parent)
                result.Add(new FileEntry("..", parent, true, 0, default, FileAttributes.Directory, IsParentLink: true));

            foreach (var item in index.ChildrenOf(inner))
            {
                ct.ThrowIfCancellationRequested();
                var name = item.Key[(item.Key.LastIndexOf('/') + 1)..];
                result.Add(new FileEntry(name, ArchivePath.Combine(archive, item.Key), item.IsDirectory,
                    item.Size, item.Modified, item.IsDirectory ? FileAttributes.Directory | FileAttributes.ReadOnly : FileAttributes.ReadOnly));
            }
            return result;
        }, ct);
}
