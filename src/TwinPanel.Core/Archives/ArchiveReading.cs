// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace TwinPanel.Core.Archives;

/// <summary>Une entrée lue dans une archive ; Open() donne son contenu (à lire avant de passer à la suivante).</summary>
internal readonly record struct RawEntry(string Key, bool IsDirectory, long Size, DateTime Modified, Func<Stream> Open, bool Encrypted = false);

/// <summary>
/// Lecture des archives : .tar / .tar.gz / .tgz avec le lecteur intégré à .NET (System.Formats.Tar, fiable),
/// tout le reste (zip, 7z, rar, gz seul, bz2, xz…) avec SharpCompress ; un .zip que SharpCompress ne sait pas lire
/// (ceux de Nextcloud, par exemple) passe par le lecteur de .NET.
/// </summary>
internal static class ArchiveReading
{
    public static bool IsTar(string path) =>
        path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

    /// <param name="password">Sans mot de passe donné : celui qui est connu pour cette archive, s'il y en a un.</param>
    public static IEnumerable<RawEntry> Read(string archivePath, string? password = null)
    {
        var info = new FileInfo(archivePath);
        if (IsTar(info.FullName)) return ReadTar(info);
        password ??= ArchivePasswords.Get(info.FullName);
        return IsZip(info) && !SharpCompressReadsZip(info, password) ? ReadWithDotNetZip(info) : ReadWithSharpCompress(info, password);
    }

    /// <summary>Fichier .zip, reconnu à sa signature (quel que soit son nom).</summary>
    private static bool IsZip(FileInfo info)
    {
        try
        {
            using var fs = info.OpenRead();
            Span<byte> head = stackalloc byte[4];
            return fs.ReadAtLeast(head, 4, throwOnEndOfStream: false) == 4
                && head[0] == (byte)'P' && head[1] == (byte)'K' && head[2] is 3 or 5 && head[3] is 4 or 6;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// SharpCompress sait-il lire la table des matières de ce .zip ? Pas celle des .zip fabriqués à la volée par
    /// Nextcloud (plusieurs fichiers téléchargés ensemble) : numéro de disque 65535, le vrai étant dans le champ Zip64.
    /// </summary>
    private static bool SharpCompressReadsZip(FileInfo info, string? password)
    {
        try
        {
            using var archive = password is null
                ? ArchiveFactory.OpenArchive(info.FullName)
                : ArchiveFactory.OpenArchive(info, new ReaderOptions { Password = password, LeaveStreamOpen = false });
            foreach (var e in archive.Entries) _ = e.Key;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>Lecteur de .zip intégré à .NET, quand SharpCompress n'y arrive pas (zip sans chiffrement seulement).</summary>
    private static IEnumerable<RawEntry> ReadWithDotNetZip(FileInfo info)
    {
        using var zip = ZipFile.OpenRead(info.FullName);
        foreach (var e in zip.Entries)
        {
            var entry = e;
            bool isDir = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            yield return new RawEntry(entry.FullName, isDir, isDir ? 0 : entry.Length, entry.LastWriteTime.LocalDateTime,
                entry.Open, entry.IsEncrypted);
        }
    }

    private static IEnumerable<RawEntry> ReadTar(FileInfo info)
    {
        using var fs = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using Stream stream = info.Name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
            ? fs
            : new GZipStream(fs, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new TarReader(stream, leaveOpen: true);
        TarEntry? e;
        while ((e = reader.GetNextEntry()) is not null)
        {
            bool isDir = e.EntryType == TarEntryType.Directory;
            bool isFile = e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;
            if (!isDir && !isFile) continue; // liens symboliques, périphériques… : ignorés (sécurité)
            var entry = e;
            yield return new RawEntry(entry.Name, isDir, isDir ? 0 : entry.Length, entry.ModificationTime.LocalDateTime,
                () => entry.DataStream ?? Stream.Null);
        }
    }

    private static IEnumerable<RawEntry> ReadWithSharpCompress(FileInfo info, string? password)
    {
        using var archive = password is null
            ? ArchiveFactory.OpenArchive(info.FullName)
            : ArchiveFactory.OpenArchive(info, new ReaderOptions { Password = password, LeaveStreamOpen = false });
        // Lecture continue pour les archives « solides » (7z, certains rar) ; sinon fichier par fichier.
        // (La bibliothèque refuse la lecture continue sur les archives non solides.)
        if (archive.IsSolid || archive.Type == SharpCompress.Common.ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                var e = reader.Entry;
                yield return new RawEntry(e.Key ?? ArchiveIndex.SingleFileName(info), e.IsDirectory, e.IsDirectory ? 0 : e.Size,
                    e.LastModifiedTime ?? info.LastWriteTime, reader.OpenEntryStream, e.IsEncrypted);
            }
        }
        else
        {
            foreach (var e in archive.Entries)
                yield return new RawEntry(e.Key ?? ArchiveIndex.SingleFileName(info), e.IsDirectory, e.IsDirectory ? 0 : e.Size,
                    e.LastModifiedTime ?? info.LastWriteTime, e.OpenEntryStream, e.IsEncrypted);
        }
    }
}
