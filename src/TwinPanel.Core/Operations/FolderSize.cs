// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Operations;

public sealed record FolderSizeResult(long Bytes, int Files, int Folders);

public static class FolderSize
{
    /// <summary>Taille totale d'un dossier (sous-dossiers compris, éléments illisibles ignorés).</summary>
    public static Task<FolderSizeResult> CalculateAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            long bytes = 0;
            int files = 0, folders = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (info is FileInfo f) { files++; bytes += f.Length; }
                else folders++;
            }
            return new FolderSizeResult(bytes, files, folders);
        }, ct);
}
