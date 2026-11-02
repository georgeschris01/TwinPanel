// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;

namespace TwinPanel.Core.Search;

public sealed record DuplicateGroup(long Size, string Hash, IReadOnlyList<FileEntry> Files)
{
    /// <summary>Place gagnée si on ne garde qu'un exemplaire.</summary>
    public long Wasted => Size * (Files.Count - 1);
}

/// <summary>
/// Doublons par contenu : regroupement par taille, puis empreinte des 64 premiers Ko,
/// puis empreinte SHA-256 complète (rapide : la plupart des fichiers sont écartés tôt).
/// </summary>
public static class DuplicateFinder
{
    private const int PartialBytes = 64 * 1024;

    public static Task<IReadOnlyList<DuplicateGroup>> FindAsync(IEnumerable<FileEntry> files,
        IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<DuplicateGroup>>(() =>
        {
            var result = new List<DuplicateGroup>();
            var bySize = files.Where(f => !f.IsDirectory && f.Size > 0)
                              .GroupBy(f => f.Size)
                              .Where(g => g.Count() > 1);

            foreach (var sizeGroup in bySize)
            {
                ct.ThrowIfCancellationRequested();
                var candidates = sizeGroup.ToList();
                if (sizeGroup.Key > PartialBytes)
                {
                    candidates = candidates
                        .GroupBy(f => Hash(f.FullPath, PartialBytes, progress, ct))
                        .Where(g => g.Key is not null && g.Count() > 1)
                        .SelectMany(g => g)
                        .ToList();
                }

                foreach (var g in candidates.GroupBy(f => Hash(f.FullPath, null, progress, ct)).Where(g => g.Key is not null && g.Count() > 1))
                    result.Add(new DuplicateGroup(sizeGroup.Key, g.Key!, g.OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).ToList()));
            }
            return result.OrderByDescending(g => g.Wasted).ToList();
        }, ct);

    /// <summary>SHA-256 du fichier (ou de ses premiers octets) ; null si illisible.</summary>
    private static string? Hash(string path, int? limit, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(path);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024, FileOptions.SequentialScan);
            if (limit is { } n)
            {
                var buffer = new byte[n];
                int read = fs.ReadAtLeast(buffer, n, throwOnEndOfStream: false);
                return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
            }
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
