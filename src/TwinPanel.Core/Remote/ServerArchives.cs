// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.Concurrent;
using TwinPanel.Core.Archives;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Copie temporaire d'une archive d'un serveur, ouverte dans un panneau. Le panneau la lit comme une archive du PC ;
/// toute modification va d'abord sur le serveur, et seulement si l'archive n'y a pas changé depuis l'ouverture.
/// </summary>
public sealed class ServerArchive
{
    private readonly Func<CancellationToken, Task<IRemoteFileSystem>> _connect;
    private readonly int _mode;
    private long _size;
    private DateTime _modified;

    internal ServerArchive(string localPath, string serverPath, string folderPanelPath, Func<CancellationToken, Task<IRemoteFileSystem>> connect, RemoteItem stamp)
    {
        LocalPath = localPath;
        ServerPath = serverPath;
        FolderPanelPath = folderPanelPath;
        _connect = connect;
        _mode = stamp.Mode;
        _size = stamp.Size;
        _modified = stamp.Modified;
    }

    public string LocalPath { get; }
    public string ServerPath { get; }
    /// <summary>Dossier du serveur où l'on revient en sortant de l'archive.</summary>
    public string FolderPanelPath { get; }
    public string Name => Path.GetFileName(LocalPath);

    /// <summary>Vrai tant que le panneau n'y est pas encore entré : la copie ne doit pas être effacée entre-temps.</summary>
    public bool IsOpening { get; set; } = true;

    public bool IsOn(string alias) =>
        PanelRemotePath.TryParse(FolderPanelPath, out var folder) && folder.Session.Equals(alias, StringComparison.OrdinalIgnoreCase);

    /// <summary>Avant un long travail : refuse tout de suite si l'archive a changé sur le serveur.</summary>
    public async Task EnsureUnchangedAsync(CancellationToken ct)
    {
        await using var fs = await _connect(ct).ConfigureAwait(false);
        await CheckAsync(fs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Remplace l'archive du serveur par ce fichier. Elle y garde ses droits : ils sont posés avant d'écrire le contenu,
    /// pour qu'une archive privée ne soit jamais lisible, même un instant. En cas d'échec, l'original reste intact.
    /// </summary>
    public async Task ReplaceAsync(string localFile, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await using var fs = await _connect(ct).ConfigureAwait(false);
        await CheckAsync(fs, ct).ConfigureAwait(false);
        progress?.Report(new TransferProgress(Name, 0, 0, 0, 0) { Notice = L.F("Envoi de « {0} » sur le serveur…", Name) });
        var part = ServerPath + RemoteTransfer.PartSuffix;
        try
        {
            if (_mode >= 0 && fs.SupportsPermissions)
            {
                using var empty = new MemoryStream();
                await fs.UploadAsync(empty, part, ct).ConfigureAwait(false);
                await fs.SetModeAsync(part, _mode & UnixModeBits, ct).ConfigureAwait(false);
            }
            await using (var input = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
                await fs.UploadAsync(input, part, ct).ConfigureAwait(false);
            await fs.RenameAsync(part, ServerPath, overwrite: true, ct).ConfigureAwait(false);
        }
        catch
        {
            // Si l'archive n'est plus là, ce fichier est sa seule copie complète : il reste.
            try { if (await fs.StatAsync(ServerPath, CancellationToken.None).ConfigureAwait(false) is not null) await fs.DeleteFileAsync(part, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException) { /* il reste à côté de l'archive, sans danger */ }
            throw;
        }
        var replaced = await fs.StatAsync(ServerPath, ct).ConfigureAwait(false);
        if (replaced is null) return;
        _size = replaced.Size;
        _modified = replaced.Modified;
    }

    private const int UnixModeBits = 0xFFF;

    private async Task CheckAsync(IRemoteFileSystem fs, CancellationToken ct)
    {
        var now = await fs.StatAsync(ServerPath, ct).ConfigureAwait(false);
        if (now is null || now.Size != _size || now.Modified != _modified)
            throw new FileOperationException(L.F("« {0} » a changé sur le serveur depuis son ouverture : rien n'a été remplacé. Sors de l'archive, puis rouvre-la.", Name));
    }
}

/// <summary>Les archives de serveurs ouvertes en ce moment. Leur copie disparaît quand plus aucun onglet ne les montre, et à la fermeture de TwinPanel.</summary>
public static class ServerArchives
{
    private static readonly ConcurrentDictionary<string, ServerArchive> Copies = new(PathText.Comparer);

    public static IReadOnlyList<ServerArchive> All => [.. Copies.Values];

    /// <summary>Archive de serveur qui contient ce chemin de panneau ; null si ce n'en est pas une.</summary>
    public static ServerArchive? Find(string path)
    {
        if (Copies.IsEmpty || !ArchivePath.TrySplit(path, out var archive, out _)) return null;
        return Copies.GetValueOrDefault(Path.GetFullPath(archive));
    }

    /// <summary>Télécharge l'archive. Sa date et sa taille sont lues avant : une modification pendant le téléchargement sera vue au remplacement.</summary>
    public static async Task<ServerArchive> OpenAsync(Func<CancellationToken, Task<IRemoteFileSystem>> connect, string serverPath, string folderPanelPath,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null)
    {
        var name = RemotePath.FileName(serverPath);
        if (!RemotePath.IsValidWindowsName(name)) throw new RemoteException(L.F("« {0} » n'est pas un nom de fichier possible sous Windows.", name));
        var folder = Path.Combine(Path.GetTempPath(), "TwinPanel", "serveur-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            await using var fs = await connect(ct).ConfigureAwait(false);
            var stamp = await fs.StatAsync(serverPath, ct).ConfigureAwait(false)
                        ?? throw new RemoteException(L.F("« {0} » n'existe plus sur le serveur.", name));
            stamp = stamp with { Name = name, FullPath = serverPath };
            await RemoteTransfer.DownloadAsync(fs, [stamp], folder, ConflictPolicy.Overwrite, progress, ct, pause).ConfigureAwait(false);
            var copy = new ServerArchive(Path.GetFullPath(Path.Combine(folder, name)), serverPath, folderPanelPath, connect, stamp);
            ArchivePasswords.TrackDecryptedCopy(folder);
            Copies[copy.LocalPath] = copy;
            return copy;
        }
        catch
        {
            DeleteFolder(folder);
            throw;
        }
    }

    public static void Close(ServerArchive copy)
    {
        Copies.TryRemove(copy.LocalPath, out _);
        ArchivePasswords.Forget(copy.LocalPath);
        DeleteFolder(Path.GetDirectoryName(copy.LocalPath)!);
    }

    private static void DeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* encore lue : ArchivePasswords.ForgetAll réessaie en quittant */ }
    }
}
