// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Compare;
using TwinPanel.Core.Search;

namespace TwinPanel.Core.Remote;

/// <summary>Résultat d'une recherche de doublons sur un serveur.</summary>
/// <param name="Unreadable">Fichiers qui n'ont pas pu être comparés (droits) : ne pas les voir en doublon ne prouve rien.</param>
public sealed record RemoteDuplicateResult(IReadOnlyList<DuplicateGroup> Groups, int Unreadable);

/// <summary>
/// Doublons sur un serveur : les tailles d'abord, puis l'empreinte SHA-256 de ceux qui ont une taille commune.
/// Le serveur la calcule lui-même quand il le peut ; sinon chaque fichier est lu par le réseau, avec l'accord de l'utilisateur.
/// Tout se fait en lecture seule, et rien n'est écrit sur le PC.
/// </summary>
public static class RemoteDuplicates
{
    /// <param name="confirmRead">Reçoit le nombre de fichiers et le volume à lire par le réseau ; faux : la recherche s'arrête.</param>
    public static async Task<RemoteDuplicateResult> FindAsync(IRemoteFileSystem fs, IEnumerable<FileEntry> files, Func<int, long, Task<bool>> confirmRead,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Un dossier cité deux fois ne doit pas faire d'un fichier son propre doublon, et l'un des deux supprimé avec lui.
        var sets = files.Where(IsComparable).DistinctBy(f => f.FullPath).GroupBy(f => f.Size).Where(g => g.Count() > 1).Select(g => g.ToList()).ToList();
        var toHash = new Queue<FileEntry>(sets.SelectMany(s => s));
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var server = fs as IServerSideMove;
        bool serverHashes = server is not null;
        bool confirmed = false;
        int unreadable = 0;

        while (toHash.TryDequeue(out var file))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(file.Name);
            var path = PanelRemotePath.TryParse(file.FullPath, out var remote) ? remote.Path : file.FullPath;
            string? hash = null;
            if (serverHashes)
            {
                var result = await server!.TryHashOnServerAsync(path, ct).ConfigureAwait(false);
                serverHashes = result.Available;
                hash = result.Hash;
            }
            if (!serverHashes)
            {
                if (!confirmed)
                {
                    if (!await confirmRead(toHash.Count + 1, toHash.Sum(f => f.Size) + file.Size).ConfigureAwait(false))
                        throw new OperationCanceledException();
                    confirmed = true;
                }
                hash = await ReadHashAsync(fs, path, ct).ConfigureAwait(false);
            }
            if (hash is null) unreadable++;
            else hashes[file.FullPath] = hash;
        }

        var groups = sets
            .SelectMany(set => set.Where(f => hashes.ContainsKey(f.FullPath)).GroupBy(f => hashes[f.FullPath], StringComparer.Ordinal))
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateGroup(g.First().Size, g.Key, g.OrderBy(f => f.FullPath, StringComparer.Ordinal).ToList()))
            .OrderByDescending(g => g.Wasted)
            .ToList();
        return new RemoteDuplicateResult(groups, unreadable);
    }

    /// <summary>Ni fichier vide, ni lien : un lien a le contenu de sa cible, et supprimer la cible comme « doublon » le casserait.</summary>
    private static bool IsComparable(FileEntry file) => !file.IsDirectory && file.Size > 0 && (file.Attributes & FileAttributes.ReparsePoint) == 0;

    private static async Task<string?> ReadHashAsync(IRemoteFileSystem fs, string path, CancellationToken ct)
    {
        try { return await RemoteChecksums.HashAsync(fs, path, HashKind.Sha256, ct).ConfigureAwait(false); }
        catch (RemoteException ex) when (!ConnectionFailure.IsLost(ex, fs)) { return null; }
    }
}
