// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;
using System.Text;

namespace TwinPanel.Core.Compare;

public enum HashKind { Md5, Sha1, Sha256 }

public enum VerifyStatus { Ok, Mismatch, Missing }

public sealed record VerifyResult(string FileName, VerifyStatus Status);

/// <summary>Fichiers de sommes de contrôle au format courant « empreinte *nom » (.md5, .sha1, .sha256).</summary>
public static class Checksums
{
    public static string Extension(HashKind kind) => kind switch
    {
        HashKind.Md5 => ".md5",
        HashKind.Sha1 => ".sha1",
        _ => ".sha256",
    };

    public static IncrementalHash NewHash(HashKind kind) => IncrementalHash.CreateHash(kind switch
    {
        HashKind.Md5 => HashAlgorithmName.MD5,
        HashKind.Sha1 => HashAlgorithmName.SHA1,
        _ => HashAlgorithmName.SHA256,
    });

    /// <summary>Ligne « empreinte *nom » (le nom avec des « / »).</summary>
    public static string FormatLine(string hash, string relativeName) => $"{hash} *{relativeName.Replace('\\', '/')}";

    /// <summary>Lignes utiles d'un fichier de sommes : (empreinte attendue, nom relatif).</summary>
    public static IEnumerable<(string Expected, string Name)> ParseLines(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            int space = line.IndexOf(' ');
            if (space <= 0) continue;
            yield return (line[..space], line[(space + 1)..].TrimStart(' ', '*'));
        }
    }

    public static async Task<string> ComputeAsync(string path, HashKind kind, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        byte[] hash = kind switch
        {
            HashKind.Md5 => await MD5.HashDataAsync(fs, ct),
            HashKind.Sha1 => await SHA1.HashDataAsync(fs, ct),
            _ => await SHA256.HashDataAsync(fs, ct),
        };
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Écrit un fichier de sommes pour les fichiers donnés (chemins relatifs au dossier du fichier de sommes).</summary>
    public static async Task CreateAsync(IReadOnlyList<string> files, HashKind kind, string outputPath,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        var sb = new StringBuilder();
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new TransferProgress(files[i], 0, 0, i, files.Count));
            var hash = await ComputeAsync(files[i], kind, ct);
            var rel = Path.GetRelativePath(baseDir, files[i]).Replace('\\', '/');
            sb.Append(hash).Append(" *").Append(rel).Append('\n');
        }
        await File.WriteAllTextAsync(outputPath, sb.ToString(), new UTF8Encoding(false), ct);
        progress?.Report(new TransferProgress(string.Empty, 0, 0, files.Count, files.Count));
    }

    /// <summary>Vérifie un fichier de sommes. Le type d'empreinte est déduit de l'extension, sinon de sa longueur.</summary>
    public static async Task<IReadOnlyList<VerifyResult>> VerifyAsync(string checksumFile, CancellationToken ct = default)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(checksumFile))!;
        var results = new List<VerifyResult>();
        foreach (var raw in await File.ReadAllLinesAsync(checksumFile, ct))
        {
            ct.ThrowIfCancellationRequested();
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            int space = line.IndexOf(' ');
            if (space <= 0) continue;
            var expected = line[..space];
            var name = line[(space + 1)..].TrimStart(' ', '*');
            var path = Path.Combine(baseDir, name.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(path)) { results.Add(new VerifyResult(name, VerifyStatus.Missing)); continue; }
            var kind = KindFor(checksumFile, expected);
            var actual = await ComputeAsync(path, kind, ct);
            results.Add(new VerifyResult(name, string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ? VerifyStatus.Ok : VerifyStatus.Mismatch));
        }
        return results;
    }

    public static HashKind KindFor(string file, string hash) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".md5" => HashKind.Md5,
        ".sha1" => HashKind.Sha1,
        ".sha256" => HashKind.Sha256,
        _ => hash.Length switch { 32 => HashKind.Md5, 40 => HashKind.Sha1, _ => HashKind.Sha256 },
    };
}
