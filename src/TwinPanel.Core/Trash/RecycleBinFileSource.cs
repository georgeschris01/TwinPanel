// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Sources;

namespace TwinPanel.Core.Trash;

/// <summary>
/// Le panneau « Corbeille » : tous les éléments supprimés du compte, sur tous les disques, en une seule liste,
/// sous leur vrai nom et avec leur dossier d'origine. Pas de dossier parent : c'est une racine.
/// </summary>
public sealed class RecycleBinFileSource : IFileSource
{
    public string Id => "trash";
    public bool SupportsWatching => false;

    public bool CanHandle(string path) => RecycleBin.IsPanelPath(path);

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, bool showHidden, CancellationToken ct) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
        {
            if (!RecycleBin.IsSupported) throw new IOException(L.T("La corbeille n'est pas encore disponible sur ce système."));
            return RecycleBin.List(ct).Select(RecycleBin.ToEntry).ToList();
        }, ct);

    public string Normalize(string path) => RecycleBin.PanelPath;

    public string? GetParent(string path) => null;
}
