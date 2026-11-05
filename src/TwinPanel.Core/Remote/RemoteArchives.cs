// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Archives;
using TwinPanel.Core.Operations;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Archives quand la source ou la destination est sur un serveur : on passe par un dossier temporaire du PC
/// (téléchargement → création / extraction → envoi), supprimé à la fin, même en cas d'erreur ou d'annulation.
/// </summary>
public static class RemoteArchives
{
    private static string NewTempDir(string kind)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TwinPanel", kind + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Windows nettoiera le dossier temporaire */ }
    }

    /// <summary>
    /// Crée une archive. Sources : éléments d'un serveur (source non nulle) ou chemins du PC.
    /// Destination : chemin distant (destination non nulle) ou chemin du PC.
    /// </summary>
    public static async Task CreateAsync(RemoteSession? source, IReadOnlyList<RemoteItem> remoteItems, IReadOnlyList<string> localSources,
        RemoteSession? destination, string outputPath, ArchiveKind kind,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null, string? password = null)
    {
        var temp = NewTempDir("archive");
        try
        {
            var sources = localSources;
            if (source is not null)
            {
                var src = Path.Combine(temp, "contenu");
                Directory.CreateDirectory(src);
                await RemoteTransfer.DownloadAsync(source, remoteItems, src, ConflictPolicy.Overwrite, progress, ct, pause).ConfigureAwait(false);
                sources = remoteItems.Select(i => Path.Combine(src, i.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            }
            var local = destination is null ? outputPath : Path.Combine(temp, RemotePath.FileName(outputPath));
            await ArchiveService.CreateAsync(sources, local, kind, progress, ct, pause, password).ConfigureAwait(false);
            if (destination is not null)
                await RemoteTransfer.UploadAsync(destination, [local], RemotePath.Parent(outputPath) ?? "/", ConflictPolicy.Overwrite, progress, ct, pause)
                    .ConfigureAwait(false);
        }
        finally { TryDeleteDir(temp); }
    }

    /// <summary>
    /// Extrait toute une archive. Archive : sur un serveur (archiveSession non nul, archivePath = chemin distant) ou sur le PC.
    /// Destination : dossier distant (destination non nulle) ou dossier du PC.
    /// </summary>
    public static async Task ExtractAsync(RemoteSession? archiveSession, RemoteItem? remoteArchive, string localArchive,
        RemoteSession? destination, string destDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null)
    {
        var temp = NewTempDir("extraction");
        try
        {
            var archive = localArchive;
            if (archiveSession is not null && remoteArchive is not null)
                archive = await RemoteTransfer.DownloadToTempAsync(archiveSession, remoteArchive, ct).ConfigureAwait(false);
            try
            {
                if (destination is null)
                {
                    await ArchiveService.ExtractAsync(archive, [""], "", destDir, policy, progress, ct, pause).ConfigureAwait(false);
                    return;
                }
                // Vers un serveur : extraction dans un dossier temporaire, puis envoi de son contenu.
                var outDir = Path.Combine(temp, "contenu");
                Directory.CreateDirectory(outDir);
                await ArchiveService.ExtractAsync(archive, [""], "", outDir, ConflictPolicy.Overwrite, progress, ct, pause).ConfigureAwait(false);
                await using (var fs = await destination.OpenWorkerAsync(ct).ConfigureAwait(false))
                {
                    if (await fs.StatAsync(destDir, ct).ConfigureAwait(false) is null) await fs.CreateDirectoryAsync(destDir, ct).ConfigureAwait(false);
                    var children = Directory.EnumerateFileSystemEntries(outDir).ToList();
                    await RemoteTransfer.UploadAsync(fs, children, destDir, policy, progress, ct, pause).ConfigureAwait(false);
                }
            }
            catch (ArchivePasswordException) when (!ReferenceEquals(archive, localArchive)) { throw OnServerOnly(archive); }
            finally
            {
                if (!ReferenceEquals(archive, localArchive)) TryDeleteDir(Path.GetDirectoryName(archive)!);
            }
        }
        finally { TryDeleteDir(temp); }
    }

    /// <summary>F5 / F6 d'une archive vers un serveur.</summary>
    public static async Task ExtractToServerAsync(RemoteSession destination, string archive, IReadOnlyList<string> keys, string innerBase, string destDir,
        ConflictPolicy policy, bool move, IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null)
    {
        await using var fs = await destination.OpenWorkerAsync(ct).ConfigureAwait(false);
        await ExtractToServerAsync(fs, archive, keys, innerBase, destDir, policy, move, progress, ct, pause).ConfigureAwait(false);
    }

    /// <summary>
    /// Extraction dans un dossier temporaire, puis envoi. F6 ne retire de l'archive que ce qui a bien pris sa place sur le serveur :
    /// un élément dont la cible existait, et qui n'a peut-être pas été envoyé, reste dans l'archive.
    /// </summary>
    public static async Task ExtractToServerAsync(IRemoteFileSystem fs, string archive, IReadOnlyList<string> keys, string innerBase, string destDir,
        ConflictPolicy policy, bool move, IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null)
    {
        var temp = NewTempDir("extraction");
        try
        {
            var removable = keys;
            if (await fs.StatAsync(destDir, ct).ConfigureAwait(false) is null) await fs.CreateDirectoryAsync(destDir, ct).ConfigureAwait(false);
            else if (move && policy is not (ConflictPolicy.Overwrite or ConflictPolicy.RenameNew))
            {
                var taken = (await fs.ListAsync(destDir, ct).ConfigureAwait(false)).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
                removable = keys.Where(k => !taken.Contains(k[(k.LastIndexOf('/') + 1)..])).ToList();
            }
            await ArchiveService.ExtractAsync(archive, keys, innerBase, temp, ConflictPolicy.Overwrite, progress, ct, pause).ConfigureAwait(false);
            await RemoteTransfer.UploadAsync(fs, Directory.EnumerateFileSystemEntries(temp).ToList(), destDir, policy, progress, ct, pause).ConfigureAwait(false);
            if (move && removable.Count > 0)
                await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = removable }, progress, ct, pause).ConfigureAwait(false);
        }
        finally { TryDeleteDir(temp); }
    }

    /// <summary>F5 / F6 d'un serveur vers une archive. F6 ne retire les fichiers du serveur que s'ils sont tous entrés dans l'archive.</summary>
    public static async Task AddFromServerAsync(RemoteSession source, IReadOnlyList<RemoteItem> items, string archive, string inner, ConflictPolicy policy, bool move,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null)
    {
        var temp = NewTempDir("archive");
        try
        {
            await RemoteTransfer.DownloadAsync(source, items, temp, ConflictPolicy.Overwrite, progress, ct, pause).ConfigureAwait(false);
            var added = items.Select(i => Path.Combine(temp, i.Name)).Where(p => File.Exists(p) || Directory.Exists(p))
                             .Select(p => new ArchiveAddition(p, inner)).ToList();
            var result = await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Add = added, Policy = policy }, progress, ct, pause).ConfigureAwait(false);
            if (!move) return;
            if (added.Count != items.Count || result.SkippedFiles > 0)
                throw new FileOperationException(L.T("Archive à jour, mais certains fichiers n'y sont pas entrés : rien n'a été retiré du serveur."));
            await RemoteTransfer.DeleteAsync(source, items, progress, ct).ConfigureAwait(false);
        }
        finally { TryDeleteDir(temp); }
    }

    /// <summary>Teste une archive d'un serveur (téléchargée dans un dossier temporaire).</summary>
    public static async Task TestAsync(RemoteSession session, RemoteItem archive, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        var local = await RemoteTransfer.DownloadToTempAsync(session, archive, ct).ConfigureAwait(false);
        try { await ArchiveService.TestAsync(local, progress, ct).ConfigureAwait(false); }
        catch (ArchivePasswordException) { throw OnServerOnly(local); }
        finally { TryDeleteDir(Path.GetDirectoryName(local)!); }
    }

    private static FileOperationException OnServerOnly(string archive) =>
        new(L.F("« {0} » est protégée par un mot de passe : ouvre-la avec Entrée pour le saisir, puis copie ce qu'il te faut (F5).", Path.GetFileName(archive)));
}
