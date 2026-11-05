// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Compare;

/// <summary>
/// Un côté d'une comparaison / synchronisation de dossiers : un dossier du PC (<see cref="LocalSyncSide"/>)
/// ou d'un serveur (RemoteSyncSide, dans TwinPanel.Core.Remote).
/// Les chemins « complets » sont ceux du côté (« D:\… » ou « /var/www/… »).
/// </summary>
public abstract class SyncSide : IAsyncDisposable
{
    public abstract string Root { get; }
    public abstract bool IsRemote { get; }
    /// <summary>Nom affiché (titre de la tâche).</summary>
    public abstract string Label { get; }

    /// <summary>Chemin complet d'un chemin relatif (séparateurs « \ » ou « / » acceptés).</summary>
    public abstract string PathOf(string relativePath);
    public abstract Task<bool> DirectoryExistsAsync(string path, CancellationToken ct);
    /// <summary>Contenu d'un dossier (les liens vers des dossiers sont ignorés : pas de boucle).</summary>
    public abstract Task<IReadOnlyList<FileEntry>> ListAsync(string dir, CancellationToken ct);
    public abstract Task<string> HashAsync(string path, CancellationToken ct);
    /// <summary>Crée le dossier et ses parents manquants.</summary>
    public abstract Task CreateDirectoriesAsync(string path, CancellationToken ct);
    /// <summary>Écrit le contenu du fichier dans « target ».</summary>
    public abstract Task ReadIntoAsync(string path, Stream target, CancellationToken ct);
    /// <summary>Écrit « source » dans le fichier (par un fichier « .twinpanel-part » puis remplacement) et lui donne cette date.</summary>
    public abstract Task WriteFromAsync(string path, Stream source, DateTime modified, CancellationToken ct);

    public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Dossier du PC.</summary>
public sealed class LocalSyncSide(string root) : SyncSide
{
    public override string Root { get; } = root;
    public override bool IsRemote => false;
    public override string Label => Path.GetFileName(Root.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Root;

    public override string PathOf(string relativePath) =>
        relativePath.Length == 0 ? Root : Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public override Task<bool> DirectoryExistsAsync(string path, CancellationToken ct) => Task.FromResult(Directory.Exists(path));

    public override Task<IReadOnlyList<FileEntry>> ListAsync(string dir, CancellationToken ct)
    {
        var list = new List<FileEntry>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
        try
        {
            foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", options))
            {
                if (info is DirectoryInfo { LinkTarget: not null }) continue;
                list.Add(info is FileInfo f
                    ? new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime, f.Attributes)
                    : new FileEntry(info.Name, info.FullName, true, 0, info.LastWriteTime, info.Attributes));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dossier illisible : vide */ }
        return Task.FromResult<IReadOnlyList<FileEntry>>(list);
    }

    public override Task<string> HashAsync(string path, CancellationToken ct) => Checksums.ComputeAsync(path, HashKind.Sha256, ct);

    public override Task CreateDirectoriesAsync(string path, CancellationToken ct)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public override async Task ReadIntoAsync(string path, Stream target, CancellationToken ct)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        await input.CopyToAsync(target, 1 << 16, ct).ConfigureAwait(false);
    }

    public override async Task WriteFromAsync(string path, Stream source, DateTime modified, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var part = path + ".twinpanel-part";
        try
        {
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                await source.CopyToAsync(output, 1 << 16, ct).ConfigureAwait(false);
            File.Move(part, path, overwrite: true);
            File.SetLastWriteTime(path, modified);
        }
        catch
        {
            try { File.Delete(part); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
