// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Formats.Tar;
using System.IO.Compression;

namespace TwinPanel.Core.Archives;

public enum ArchiveKind { Zip, Tar, TarGz }

/// <summary>Extraire, tester et créer des archives.</summary>
public static class ArchiveService
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Format d'une archive à créer, d'après son nom (.zip, .tar, .tar.gz / .tgz). Null si non géré.</summary>
    public static ArchiveKind? KindFromName(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ArchiveKind.Zip
        : path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ? ArchiveKind.TarGz
        : path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? ArchiveKind.Tar
        : null;

    /// <summary>
    /// Extrait des éléments d'une archive. <paramref name="innerBase"/> est le dossier de l'archive affiché :
    /// « a/b/c.txt » extrait depuis « a » arrive dans « destination\b\c.txt ».
    /// Protection « zip slip » : une entrée qui sortirait du dossier de destination (« ../../x ») est refusée.
    /// </summary>
    public static Task ExtractAsync(string archivePath, IReadOnlyList<string> innerPaths, string innerBase, string destination,
        ConflictPolicy policy, IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null) =>
        Task.Run(() =>
        {
            Directory.CreateDirectory(destination);
            var destRoot = Path.GetFullPath(destination);
            var destPrefix = destRoot.EndsWith(Path.DirectorySeparatorChar) ? destRoot : destRoot + Path.DirectorySeparatorChar;
            innerBase = ArchivePath.NormalizeKey(innerBase);

            var index = ArchiveIndex.Load(archivePath);
            var selected = index.Expand(innerPaths).ToList();
            var wanted = new HashSet<string>(selected.Where(i => !i.IsDirectory).Select(i => i.Key), StringComparer.Ordinal);
            long totalBytes = selected.Where(i => !i.IsDirectory).Sum(i => i.Size);
            int totalFiles = wanted.Count;
            DiskSpace.CheckBeforeStart(destRoot, totalBytes, pause, ct);
            long doneBytes = 0;
            int doneFiles = 0;
            var lastReport = DateTime.MinValue;

            string Target(string key)
            {
                var rel = innerBase.Length > 0 && key.StartsWith(innerBase + "/", StringComparison.Ordinal) ? key[(innerBase.Length + 1)..] : key;
                var full = Path.GetFullPath(Path.Combine(destRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(destPrefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new FileOperationException(L.F("Entrée dangereuse refusée (elle sortirait du dossier de destination) : {0}", key));
                return full;
            }

            // Les dossiers d'abord (y compris vides).
            foreach (var dir in selected.Where(i => i.IsDirectory)) Directory.CreateDirectory(Target(dir.Key));

            void Report(string item, bool force = false)
            {
                if (progress is null || (!force && (DateTime.UtcNow - lastReport).TotalMilliseconds < 100)) return;
                lastReport = DateTime.UtcNow;
                progress.Report(new TransferProgress(item, doneBytes, totalBytes, doneFiles, totalFiles));
            }

            ArchiveErrors.Wrap(archivePath, () =>
            {
                var info = new FileInfo(archivePath);
                foreach (var entry in ArchiveReading.Read(info.FullName))
                {
                    if (wanted.Count == 0) break;
                    ct.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) continue;
                    var key = ArchivePath.NormalizeKey(entry.Key);
                    if (!wanted.Remove(key)) continue;

                    var target = Target(key);
                    var modified = entry.Modified;
                    var final = Resolve(target, modified, policy);
                    if (final is null) { doneBytes += entry.Size; doneFiles++; Report(key); continue; }

                    Directory.CreateDirectory(Path.GetDirectoryName(final)!);
                    var part = final + ".twinpanel-part";
                    try
                    {
                        using (var input = entry.Open())
                        using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                        {
                            var buffer = new byte[BufferSize];
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                ct.ThrowIfCancellationRequested();
                                pause?.WaitIfPaused(ct);
                                output.Write(buffer, 0, read);
                                doneBytes += read;
                                Report(key);
                            }
                        }
                        File.SetLastWriteTime(part, modified);
                        if (File.Exists(final)) File.SetAttributes(final, FileAttributes.Normal);
                        File.Move(part, final, overwrite: true);
                    }
                    catch (IOException ex) when (DiskSpace.IsFull(ex))
                    {
                        if (File.Exists(part)) File.Delete(part);
                        throw new FileOperationException(DiskSpace.FullMessage(final, entry.Size, canResume: false), ex);
                    }
                    catch
                    {
                        if (File.Exists(part)) File.Delete(part);
                        throw;
                    }
                    doneFiles++;
                    Report(key);
                }
            });
            Report(string.Empty, force: true);
        }, ct);

    /// <summary>Lit toute l'archive sans rien écrire : détecte les fichiers endommagés (contrôle CRC).</summary>
    public static Task TestAsync(string archivePath, IProgress<TransferProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var index = ArchiveIndex.Load(archivePath);
            long total = index.All.Where(i => !i.IsDirectory).Sum(i => i.Size);
            int files = index.All.Count(i => !i.IsDirectory);
            long done = 0;
            int doneFiles = 0;
            ArchiveErrors.Wrap(archivePath, () =>
            {
                var buffer = new byte[BufferSize];
                foreach (var entry in ArchiveReading.Read(archivePath))
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) continue;
                    using var s = entry.Open();
                    int read;
                    while ((read = s.Read(buffer, 0, buffer.Length)) > 0) done += read;
                    doneFiles++;
                    progress?.Report(new TransferProgress(entry.Key, done, total, doneFiles, files));
                }
            });
        }, ct);

    /// <summary>
    /// Crée une archive .zip, .tar ou .tar.gz avec les fichiers et dossiers donnés (chemins relatifs à leur dossier parent).
    /// Écrite d'abord dans un fichier temporaire : une archive interrompue ne remplace jamais une archive existante.
    /// </summary>
    /// <param name="password">Zip seulement : chiffré en AES-256.</param>
    public static Task CreateAsync(IReadOnlyList<string> sources, string outputPath, ArchiveKind kind,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null, string? password = null) =>
        Task.Run(() =>
        {
            if (password is not null && kind != ArchiveKind.Zip) throw new FileOperationException(L.T("Seul le format .zip peut être protégé par un mot de passe."));
            var outputFull = Path.GetFullPath(outputPath);
            var files = new List<(string Full, string Name)>();
            var dirs = new List<string>();
            foreach (var s in sources)
            {
                var src = Path.GetFullPath(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var baseDir = Path.GetDirectoryName(src)!;
                if (File.Exists(src)) files.Add((src, Path.GetFileName(src)));
                else if (Directory.Exists(src))
                {
                    dirs.Add(Path.GetFileName(src));
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
                    foreach (var info in new DirectoryInfo(src).EnumerateFileSystemInfos("*", options))
                    {
                        var rel = Path.GetRelativePath(baseDir, info.FullName).Replace('\\', '/');
                        if (info is DirectoryInfo) dirs.Add(rel);
                        else if (!string.Equals(info.FullName, outputFull, StringComparison.OrdinalIgnoreCase)) files.Add((info.FullName, rel));
                    }
                }
                else throw new FileOperationException(L.F("Source introuvable : {0}", src));
            }

            long total = files.Sum(f => new FileInfo(f.Full).Length);
            long done = 0;
            int doneFiles = 0;
            var part = outputFull + ".twinpanel-part";
            try
            {
                using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                {
                    if (kind == ArchiveKind.Zip && password is not null)
                    {
                        using var zip = EncryptedZip.Open(fs, password);
                        foreach (var d in dirs) EncryptedZip.AddFolder(zip, d, Directory.GetLastWriteTime(FolderOnDisk(sources, d)));
                        foreach (var (full, name) in files)
                        {
                            var info = new FileInfo(full);
                            EncryptedZip.BeginFile(zip, name, info.Length, info.LastWriteTime);
                            using (var input = info.OpenRead()) CopyWithProgress(input, zip, ref done, total, name, doneFiles, files.Count, progress, ct, pause);
                            zip.CloseEntry();
                            doneFiles++;
                        }
                    }
                    else if (kind == ArchiveKind.Zip)
                    {
                        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
                        foreach (var d in dirs) zip.CreateEntry(d + "/");
                        foreach (var (full, name) in files)
                        {
                            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                            entry.LastWriteTime = File.GetLastWriteTime(full);
                            using var input = File.OpenRead(full);
                            using var output = entry.Open();
                            CopyWithProgress(input, output, ref done, total, name, doneFiles, files.Count, progress, ct, pause);
                            doneFiles++;
                        }
                    }
                    else
                    {
                        using var stream = kind == ArchiveKind.TarGz ? new GZipStream(fs, CompressionLevel.Optimal) : (Stream)fs;
                        using var tar = new TarWriter(stream, TarEntryFormat.Gnu, leaveOpen: kind == ArchiveKind.Tar);
                        foreach (var d in dirs) tar.WriteEntry(new GnuTarEntry(TarEntryType.Directory, d + "/"));
                        foreach (var (full, name) in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            pause?.WaitIfPaused(ct);
                            tar.WriteEntry(full, name);
                            done += new FileInfo(full).Length;
                            doneFiles++;
                            progress?.Report(new TransferProgress(name, done, total, doneFiles, files.Count));
                        }
                    }
                }
                if (password is not null && !ArchivePasswords.Check(part, password))
                    throw new FileOperationException(L.T("L'archive chiffrée n'a pas pu être relue : elle n'a pas été gardée."));
                File.Move(part, outputFull, overwrite: true);
                ArchivePasswords.Forget(outputFull); // celui d'une ancienne archive du même nom
            }
            catch
            {
                if (File.Exists(part)) File.Delete(part);
                throw;
            }
            progress?.Report(new TransferProgress(string.Empty, total, total, files.Count, files.Count));
        }, ct);

    /// <summary>Le dossier du disque qui correspond à un dossier de l'archive à créer (« photos/2024 », dont « photos » est une des sources).</summary>
    private static string FolderOnDisk(IReadOnlyList<string> sources, string relative)
    {
        var first = relative.Split('/')[0];
        var source = sources.Select(s => Path.GetFullPath(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            .First(s => Directory.Exists(s) && Path.GetFileName(s) == first);
        return Path.Combine(Path.GetDirectoryName(source)!, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Extrait un seul fichier dans un dossier temporaire (pour F3, F4, vue rapide). Retourne son chemin.</summary>
    public static async Task<string> ExtractToTempAsync(string archivePath, string innerKey, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "TwinPanel", Guid.NewGuid().ToString("N")[..8]);
        if (ArchivePasswords.Get(archivePath) is not null) ArchivePasswords.TrackDecryptedCopy(dir);
        await ExtractAsync(archivePath, [innerKey], ArchiveIndex.ParentOf(ArchivePath.NormalizeKey(innerKey)), dir, ConflictPolicy.Overwrite, null, ct);
        return Path.Combine(dir, Path.GetFileName(innerKey.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string? Resolve(string target, DateTime modified, ConflictPolicy policy)
    {
        if (!File.Exists(target)) return target;
        return policy switch
        {
            ConflictPolicy.Skip => null,
            ConflictPolicy.Overwrite => target,
            ConflictPolicy.OverwriteOlder => modified > File.GetLastWriteTime(target).AddSeconds(2) ? target : null,
            _ => FileOperations.MakeUniqueName(target),
        };
    }

    private static void CopyWithProgress(Stream input, Stream output, ref long done, long total, string name, int doneFiles, int totalFiles,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause)
    {
        var buffer = new byte[BufferSize];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            pause?.WaitIfPaused(ct);
            output.Write(buffer, 0, read);
            done += read;
            progress?.Report(new TransferProgress(name, done, total, doneFiles, totalFiles));
        }
    }
}
