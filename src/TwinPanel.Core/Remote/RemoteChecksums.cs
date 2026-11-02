// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Compare;

namespace TwinPanel.Core.Remote;

/// <summary>Sommes de contrôle de fichiers d'un serveur : calculées pendant le téléchargement (rien n'est écrit sur le PC).</summary>
public static class RemoteChecksums
{
    internal static async Task<string> HashAsync(IRemoteFileSystem fs, string path, HashKind kind, CancellationToken ct)
    {
        using var hash = Checksums.NewHash(kind);
        var sink = new HashingStream(hash);
        await fs.DownloadAsync(path, sink, ct).ConfigureAwait(false);
        return sink.HexHash();
    }

    /// <summary>Crée sur le serveur un fichier de sommes pour les fichiers donnés (noms relatifs à son dossier).</summary>
    public static async Task CreateAsync(RemoteSession session, IReadOnlyList<RemoteItem> files, HashKind kind, string outputPath,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await using var fs = await session.OpenWorkerAsync(ct).ConfigureAwait(false);
        var baseDir = RemotePath.Parent(outputPath) ?? "/";
        long total = files.Sum(f => f.Size), done = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new TransferProgress(files[i].Name, done, total, i, files.Count));
            var hex = await HashAsync(fs, files[i].FullPath, kind, ct).ConfigureAwait(false);
            sb.Append(Checksums.FormatLine(hex, Relative(baseDir, files[i].FullPath))).Append('\n');
            done += files[i].Size;
        }
        using var content = new MemoryStream(new UTF8Encoding(false).GetBytes(sb.ToString()));
        await fs.UploadAsync(content, outputPath, ct).ConfigureAwait(false);
        progress?.Report(new TransferProgress(string.Empty, total, total, files.Count, files.Count));
    }

    /// <summary>Vérifie un fichier de sommes posé sur le serveur.</summary>
    public static async Task<IReadOnlyList<VerifyResult>> VerifyAsync(RemoteSession session, string checksumPath, CancellationToken ct)
    {
        await using var fs = await session.OpenWorkerAsync(ct).ConfigureAwait(false);
        using var text = new MemoryStream();
        await fs.DownloadAsync(checksumPath, text, ct).ConfigureAwait(false);
        var lines = Encoding.UTF8.GetString(text.ToArray()).Split('\n');
        var baseDir = RemotePath.Parent(checksumPath) ?? "/";
        var results = new List<VerifyResult>();
        foreach (var (expected, name) in Checksums.ParseLines(lines))
        {
            ct.ThrowIfCancellationRequested();
            var path = RemotePath.Normalize(RemotePath.Combine(baseDir, name.Replace('\\', '/')));
            if (await fs.StatAsync(path, ct).ConfigureAwait(false) is not { IsDirectory: false })
            {
                results.Add(new VerifyResult(name, VerifyStatus.Missing));
                continue;
            }
            var actual = await HashAsync(fs, path, Checksums.KindFor(checksumPath, expected), ct).ConfigureAwait(false);
            results.Add(new VerifyResult(name, string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ? VerifyStatus.Ok : VerifyStatus.Mismatch));
        }
        return results;
    }

    internal static string Relative(string baseDir, string path)
    {
        var b = RemotePath.Normalize(baseDir).TrimEnd('/') + "/";
        var p = RemotePath.Normalize(path);
        return p.StartsWith(b, StringComparison.Ordinal) ? p[b.Length..] : p.TrimStart('/');
    }
}
