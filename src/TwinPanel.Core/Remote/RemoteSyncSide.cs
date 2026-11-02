// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Compare;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Dossier d'un serveur comme côté de synchronisation. Une connexion à part est ouverte (la navigation reste libre)
/// et fermée avec <see cref="DisposeAsync"/>.
/// </summary>
public sealed class RemoteSyncSide(RemoteSession session, string root) : SyncSide
{
    private IRemoteFileSystem? _fs;
    private readonly HashSet<string> _knownDirs = new(StringComparer.Ordinal);

    public override string Root { get; } = RemotePath.Normalize(root);
    public override bool IsRemote => true;
    public override string Label => $"{session.Profile.Name}:{Root}";
    public RemoteSession Session => session;

    private async Task<IRemoteFileSystem> FsAsync(CancellationToken ct) => _fs ??= await session.OpenWorkerAsync(ct).ConfigureAwait(false);

    public override string PathOf(string relativePath) =>
        relativePath.Length == 0 ? Root : RemotePath.Normalize(RemotePath.Combine(Root, relativePath.Replace('\\', '/')));

    public override async Task<bool> DirectoryExistsAsync(string path, CancellationToken ct) =>
        await (await FsAsync(ct).ConfigureAwait(false)).StatAsync(path, ct).ConfigureAwait(false) is { IsDirectory: true };

    public override async Task<IReadOnlyList<FileEntry>> ListAsync(string dir, CancellationToken ct)
    {
        var fs = await FsAsync(ct).ConfigureAwait(false);
        var list = new List<FileEntry>();
        foreach (var i in await fs.ListAsync(dir, ct).ConfigureAwait(false))
        {
            if (i.IsDirectory && i.IsLink) continue; // lien vers un dossier : ignoré (boucles possibles)
            list.Add(new FileEntry(i.Name, i.FullPath, i.IsDirectory, i.Size, i.Modified,
                i.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal));
        }
        return list;
    }

    public override async Task<string> HashAsync(string path, CancellationToken ct)
    {
        var fs = await FsAsync(ct).ConfigureAwait(false);
        using var hash = Checksums.NewHash(HashKind.Sha256);
        var sink = new HashingStream(hash);
        await fs.DownloadAsync(path, sink, ct).ConfigureAwait(false);
        return sink.HexHash();
    }

    public override async Task CreateDirectoriesAsync(string path, CancellationToken ct)
    {
        var fs = await FsAsync(ct).ConfigureAwait(false);
        var current = "/";
        foreach (var part in RemotePath.Normalize(path).Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = RemotePath.Combine(current, part);
            if (_knownDirs.Contains(current)) continue;
            if (await fs.StatAsync(current, ct).ConfigureAwait(false) is null) await fs.CreateDirectoryAsync(current, ct).ConfigureAwait(false);
            _knownDirs.Add(current);
        }
    }

    public override async Task ReadIntoAsync(string path, Stream target, CancellationToken ct) =>
        await (await FsAsync(ct).ConfigureAwait(false)).DownloadAsync(path, target, ct).ConfigureAwait(false);

    public override async Task WriteFromAsync(string path, Stream source, DateTime modified, CancellationToken ct)
    {
        var fs = await FsAsync(ct).ConfigureAwait(false);
        if (RemotePath.Parent(path) is { } parent) await CreateDirectoriesAsync(parent, ct).ConfigureAwait(false);
        var part = path + RemoteTransfer.PartSuffix;
        try
        {
            await fs.UploadAsync(source, part, ct).ConfigureAwait(false);
            await fs.RenameAsync(part, path, overwrite: true, ct).ConfigureAwait(false);
        }
        catch
        {
            try { await fs.DeleteFileAsync(part, CancellationToken.None).ConfigureAwait(false); } catch (RemoteException) { }
            throw;
        }
        // Même date que la source : sinon la prochaine comparaison verrait ce fichier « plus récent ».
        await fs.SetModifiedAsync(path, modified, ct).ConfigureAwait(false);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_fs is not null)
        {
            try { await _fs.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) when (RemoteErrors.IsTranslatable(ex) || ex is ObjectDisposedException) { }
            _fs = null;
        }
    }
}
