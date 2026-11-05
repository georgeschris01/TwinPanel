// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;

namespace TwinPanel.Core.Archives;

public enum ArchiveFormat { Zip, SevenZip, Tar, TarGz }

/// <summary>Fichier ou dossier du PC à placer dans le dossier <paramref name="TargetDir"/> de l'archive.</summary>
public sealed record ArchiveAddition(string LocalPath, string TargetDir);

/// <summary>Ce qu'il faut changer dans une archive. Les clés sont les chemins intérieurs (« photos/a.jpg »).</summary>
public sealed record ArchiveEdits
{
    public IReadOnlyList<string> Delete { get; init; } = [];
    /// <summary>Ancienne clé → nouvelle clé (fichier ou dossier, avec son contenu).</summary>
    public IReadOnlyDictionary<string, string> Rename { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> NewFolders { get; init; } = [];
    public IReadOnlyList<ArchiveAddition> Add { get; init; } = [];
    public ConflictPolicy Policy { get; init; } = ConflictPolicy.Overwrite;
    /// <summary>F6 : retire du PC ce qui est entré dans l'archive, une fois celle-ci remplacée.</summary>
    public bool MoveSources { get; init; }
}

/// <param name="SkippedFiles">Fichiers non ajoutés (nom déjà pris).</param>
/// <param name="SourcesLeft">Éléments déplacés qui n'ont pas pu être retirés du PC.</param>
public sealed record ArchiveEditResult(int SkippedFiles, int SourcesLeft);

/// <summary>
/// Modifie une archive sans jamais la mettre en danger : tout est réécrit dans un fichier à côté, relu en entier et comparé à ce qui
/// devait y entrer, et c'est seulement alors que ce fichier remplace l'original. Une erreur ou une panne laisse l'original intact.
/// Il faut assez de place libre, et une grosse archive prend du temps : tout est recompressé.
/// </summary>
public static class ArchiveEditor
{
    private const int BufferSize = 1 << 20;
    /// <summary>Deux blocs de zéros : la fin d'un tar.</summary>
    private const int EmptyTarBytes = 1024;
    private const string ForbiddenInNames = "/\\:*?\"<>|";

    public static ArchiveFormat? FormatOf(string path)
    {
        var name = Path.GetFileName(path);
        bool Is(string extension) => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        if (Is(".zip") || Is(".jar") || Is(".cbz")) return ArchiveFormat.Zip;
        if (Is(".7z") || Is(".cb7")) return ArchiveFormat.SevenZip;
        if (Is(".tar")) return ArchiveFormat.Tar;
        if (Is(".tar.gz") || Is(".tgz")) return ArchiveFormat.TarGz;
        return null;
    }

    public static bool CanEdit(string path) => FormatOf(path) is not null;

    /// <summary>
    /// Message de refus pour un 7z chiffré ; null sinon. TwinPanel ne sait pas écrire de 7z chiffré : le réécrire le laisserait en clair.
    /// </summary>
    public static string? EncryptedRefusal(string path)
    {
        if (FormatOf(path) != ArchiveFormat.SevenZip) return null;
        try { if (!ArchiveIndex.Load(path).IsEncrypted) return null; }
        catch (ArchivePasswordException) { /* noms cachés : chiffré lui aussi */ }
        return L.F("« {0} » est un 7z protégé par un mot de passe : TwinPanel peut l'ouvrir et l'extraire, mais pas le modifier.\n", Path.GetFileName(path)) +
               L.T("Extrais son contenu (F5), puis crée un .zip avec mot de passe (Alt+F5).");
    }

    /// <summary>Message d'erreur si ce nom ne peut pas être donné à un élément d'archive ; null s'il convient.</summary>
    public static string? CheckName(string name)
    {
        if (name.Length == 0 || name != name.Trim()) return L.T("Nom vide, ou qui commence ou finit par une espace.");
        if (name is "." or ".." || name.EndsWith('.')) return L.T("Un nom ne peut pas finir par un point.");
        return name.Any(c => char.IsControl(c) || ForbiddenInNames.Contains(c)) ? L.T("Caractère interdit dans le nom.") : null;
    }

    public static Task<ArchiveEditResult> ApplyAsync(string archivePath, ArchiveEdits edits, IProgress<TransferProgress>? progress = null,
        CancellationToken ct = default, PauseController? pause = null) => Task.Run(() =>
    {
        var format = FormatOf(archivePath) ?? throw new FileOperationException(
            L.F("« {0} » ne peut pas être modifiée : ce format reste en lecture seule (rar, gz, bz2, xz). Formats modifiables : zip, 7z, tar, tar.gz.", Path.GetFileName(archivePath)));
        if (EncryptedRefusal(archivePath) is { } refusal) throw new FileOperationException(refusal);
        var info = new FileInfo(archivePath);
        var (length, stamp) = (info.Length, info.LastWriteTimeUtc);
        var server = Remote.ServerArchives.Find(info.FullName);
        server?.EnsureUnchangedAsync(ct).GetAwaiter().GetResult();
        var index = ArchiveIndex.Load(info.FullName);
        var password = !index.IsEncrypted ? null
            : ArchivePasswords.Get(info.FullName) ?? throw new ArchivePasswordException(info.FullName, wrong: false);
        var plan = EditPlan.Build(index, edits);
        CheckFreeSpace(info, plan);

        var part = Path.Combine(info.DirectoryName!, $"twinpanel-part-{Guid.NewGuid():N}-{info.Name}");
        try
        {
            ArchiveErrors.Wrap(info.FullName, () =>
            {
                var rewrite = new Rewrite(plan, password, progress, ct, pause);
                rewrite.Write(format, info.FullName, part);
                rewrite.Verify(part);
            });
            info.Refresh();
            if (info.Length != length || info.LastWriteTimeUtc != stamp)
                throw new FileOperationException(L.F("« {0} » a changé pendant l'opération : rien n'a été remplacé.", info.Name));
            // Archive d'un serveur : le serveur d'abord. S'il refuse, la copie reste identique à ce qu'il contient.
            server?.ReplaceAsync(part, progress, ct).GetAwaiter().GetResult();
            File.Move(part, info.FullName, overwrite: true);
        }
        finally
        {
            try { File.Delete(part); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* il reste à côté de l'archive, sans danger */ }
        }
        return new ArchiveEditResult(plan.SkippedFiles, edits.MoveSources ? RemoveSources(plan) : 0);
    }, ct);

    /// <summary>
    /// F6 depuis une archive : extrait, puis retire de l'archive. Un élément dont la cible existait déjà n'est retiré qu'en cas
    /// d'écrasement : sinon il n'a peut-être pas été extrait, et il ne doit pas disparaître.
    /// </summary>
    public static async Task MoveOutAsync(string archivePath, IReadOnlyList<string> keys, string innerBase, string destination, ConflictPolicy policy,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null)
    {
        var index = ArchiveIndex.Load(archivePath);
        var removable = keys.Where(key => policy == ConflictPolicy.Overwrite || !index.Expand([key]).Any(item => Exists(item, innerBase, destination))).ToList();
        await ArchiveService.ExtractAsync(archivePath, keys, innerBase, destination, policy, progress, ct, pause).ConfigureAwait(false);
        if (removable.Count > 0)
            await ApplyAsync(archivePath, new ArchiveEdits { Delete = removable }, progress, ct, pause).ConfigureAwait(false);
    }

    private static bool Exists(ArchiveItem item, string innerBase, string destination)
    {
        var inner = ArchivePath.NormalizeKey(innerBase);
        var relative = inner.Length > 0 && item.Key.StartsWith(inner + "/", StringComparison.Ordinal) ? item.Key[(inner.Length + 1)..] : item.Key;
        var target = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
        return !item.IsDirectory && File.Exists(target);
    }

    private static void CheckFreeSpace(FileInfo archive, EditPlan plan)
    {
        long needed = archive.Length + plan.Added.Sum(f => f.Size);
        try
        {
            if (new DriveInfo(archive.FullName).AvailableFreeSpace < needed)
                throw new FileOperationException(L.F("Pas assez de place sur le disque : il faut {0} libres pour réécrire « {1} ».", FileEntry.FormatSize(needed), archive.Name));
        }
        catch (ArgumentException) { /* partage réseau : la taille libre n'est pas connue, l'écriture dira si ça manque */ }
    }

    private static int RemoveSources(EditPlan plan)
    {
        int left = 0;
        foreach (var source in plan.CompleteSources)
        {
            try
            {
                if (Directory.Exists(source)) Directory.Delete(source, recursive: true);
                else File.Delete(source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { left++; }
        }
        return left;
    }

    /// <summary>Vérifie une archive réécrite : chaque fichier doit y être, avec exactement le contenu voulu, et rien de plus.</summary>
    internal static void Verify(string path, Dictionary<string, Queue<byte[]>> expected, Action<string, int>? advance = null, string? password = null)
    {
        foreach (var entry in ArchiveReading.Read(path, password))
        {
            if (entry.IsDirectory) continue;
            var key = ArchivePath.NormalizeKey(entry.Key);
            using var data = entry.Open();
            var tracked = new TrackedStream(data, read => advance?.Invoke(key, read));
            tracked.CopyTo(Stream.Null, BufferSize);
            if (!expected.TryGetValue(key, out var wanted) || !wanted.TryDequeue(out var hash) || !tracked.Finish().SequenceEqual(hash)) throw Failed(key);
        }
        if (expected.FirstOrDefault(p => p.Value.Count > 0) is { Key: not null } missing) throw Failed(missing.Key);
    }

    private static FileOperationException Failed(string key) =>
        new(L.F("La vérification de l'archive réécrite a échoué sur « {0} » : l'original n'a pas été touché.", key));

    private sealed record AddedFile(string Source, string Key, long Size, DateTime Modified);

    /// <summary>Calcule le résultat voulu avant d'écrire quoi que ce soit : une demande impossible s'arrête ici.</summary>
    private sealed class EditPlan
    {
        private readonly List<string> _delete;
        private readonly Dictionary<string, string> _rename;
        private readonly HashSet<string> _replaced = new(StringComparer.Ordinal);

        private EditPlan(ArchiveEdits edits)
        {
            _delete = edits.Delete.Select(ArchivePath.NormalizeKey).ToList();
            _rename = edits.Rename.ToDictionary(p => ArchivePath.NormalizeKey(p.Key), p => ArchivePath.NormalizeKey(p.Value), StringComparer.Ordinal);
        }

        public List<AddedFile> Added { get; } = [];
        public HashSet<string> Folders { get; } = new(StringComparer.Ordinal);
        public List<string> CompleteSources { get; } = [];
        public int SkippedFiles { get; private set; }
        public long KeptBytes { get; private set; }

        /// <summary>Nouvelle clé d'une entrée de l'archive ; null si elle n'en fait plus partie.</summary>
        public string? NewKey(string key)
        {
            if (_replaced.Contains(key) || _delete.Any(d => Inside(key, d))) return null;
            foreach (var (from, to) in _rename)
                if (Inside(key, from)) return to + key[from.Length..];
            return key;
        }

        private static bool Inside(string key, string parent) => key == parent || key.StartsWith(parent + "/", StringComparison.Ordinal);

        public static EditPlan Build(ArchiveIndex index, ArchiveEdits edits)
        {
            var plan = new EditPlan(edits);
            var existing = index.All.ToDictionary(i => i.Key, StringComparer.Ordinal);
            foreach (var (from, to) in plan._rename)
            {
                if (!existing.ContainsKey(from)) throw new FileOperationException(L.F("« {0} » n'est plus dans l'archive.", from));
                if (existing.ContainsKey(to)) throw new FileOperationException(L.F("« {0} » existe déjà dans l'archive.", to));
            }
            foreach (var folder in edits.NewFolders.Select(ArchivePath.NormalizeKey))
            {
                if (existing.ContainsKey(folder)) throw new FileOperationException(L.F("« {0} » existe déjà dans l'archive.", folder));
                plan.Folders.Add(folder);
            }
            foreach (var add in edits.Add) plan.AddSource(add, existing, edits.Policy);
            plan.KeptBytes = index.All.Where(i => !i.IsDirectory && plan.NewKey(i.Key) is not null).Sum(i => i.Size);
            plan.KeepEmptiedFolders(index);
            return plan;
        }

        private void AddSource(ArchiveAddition add, Dictionary<string, ArchiveItem> existing, ConflictPolicy policy)
        {
            var source = Path.GetFullPath(add.LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var name = Path.GetFileName(source);
            var dir = ArchivePath.NormalizeKey(add.TargetDir);
            string Key(string relative) => (dir.Length > 0 ? dir + "/" : string.Empty) + relative.Replace('\\', '/');
            int skippedBefore = SkippedFiles;

            if (File.Exists(source)) AddFile(new FileInfo(source), Key(name), existing, policy);
            else if (Directory.Exists(source))
            {
                Folders.Add(Key(name));
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = 0 };
                foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", options))
                {
                    var relative = Path.Combine(name, Path.GetRelativePath(source, entry.FullName));
                    if (entry is FileInfo file) AddFile(file, Key(relative), existing, policy);
                    else Folders.Add(Key(relative));
                }
            }
            else throw new FileOperationException(L.F("Source introuvable : {0}", source));

            if (SkippedFiles == skippedBefore) CompleteSources.Add(source);
        }

        private void AddFile(FileInfo file, string key, Dictionary<string, ArchiveItem> existing, ConflictPolicy policy)
        {
            if (existing.TryGetValue(key, out var other))
            {
                if (other.IsDirectory) throw new FileOperationException(L.F("« {0} » existe déjà dans l'archive comme dossier.", key));
                bool skip = policy == ConflictPolicy.Skip || (policy == ConflictPolicy.OverwriteOlder && file.LastWriteTime <= other.Modified.AddSeconds(2));
                if (skip) { SkippedFiles++; return; }
                if (policy == ConflictPolicy.RenameNew) key = UniqueKey(key, existing);
                else _replaced.Add(key);
            }
            Added.Add(new AddedFile(file.FullName, key, file.Length, file.LastWriteTime));
        }

        /// <summary>« a.txt » devient « a (2).txt ».</summary>
        private string UniqueKey(string key, Dictionary<string, ArchiveItem> existing)
        {
            var parent = ArchiveIndex.ParentOf(key);
            var name = parent.Length > 0 ? key[(parent.Length + 1)..] : key;
            var (stem, extension) = (Path.GetFileNameWithoutExtension(name), Path.GetExtension(name));
            for (int n = 2; ; n++)
            {
                var candidate = (parent.Length > 0 ? parent + "/" : string.Empty) + $"{stem} ({n}){extension}";
                if (!existing.ContainsKey(candidate) && Added.TrueForAll(a => a.Key != candidate)) return candidate;
            }
        }

        /// <summary>Un dossier sous-entendu par ses fichiers disparaîtrait avec le dernier d'entre eux : le panneau qui le montre perdrait pied.</summary>
        private void KeepEmptiedFolders(ArchiveIndex index)
        {
            var filled = new HashSet<string>(StringComparer.Ordinal);
            void Fill(string key)
            {
                for (var parent = ArchiveIndex.ParentOf(key); parent.Length > 0; parent = ArchiveIndex.ParentOf(parent)) filled.Add(parent);
            }
            foreach (var key in index.All.Where(i => !i.IsDirectory).Select(i => NewKey(i.Key)).OfType<string>()) Fill(key);
            foreach (var file in Added) Fill(file.Key);

            foreach (var key in _delete.Concat(_rename.Keys).Concat(_replaced))
            {
                var parent = ArchiveIndex.ParentOf(key);
                if (parent.Length > 0 && NewKey(parent) is { } folder && !filled.Contains(folder)) Folders.Add(folder);
            }
        }
    }

    /// <summary>Lit en comptant : l'empreinte de ce qui entre dans l'archive, l'avancement, la pause et l'annulation.</summary>
    private sealed class TrackedStream(Stream inner, Action<int> onRead) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public byte[] Finish()
        {
            var value = _hash.GetHashAndReset();
            _hash.Dispose();
            return value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int read = inner.Read(buffer);
            _hash.AppendData(buffer[..read]);
            onRead(read);
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Une réécriture : la moitié de la barre pour écrire, l'autre pour vérifier.</summary>
    private sealed class Rewrite(EditPlan plan, string? password, IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause)
    {
        private readonly Dictionary<string, Queue<byte[]>> _expected = new(StringComparer.Ordinal);
        private readonly HashSet<string> _writtenFolders = new(StringComparer.Ordinal);
        private readonly long _total = (plan.KeptBytes + plan.Added.Sum(f => f.Size)) * 2;
        private long _done;
        private int _files;

        public void Advance(string name, int bytes)
        {
            ct.ThrowIfCancellationRequested();
            pause?.WaitIfPaused(ct);
            _done += bytes;
            progress?.Report(new TransferProgress(name, _done, _total, _files, _expected.Count));
        }

        public void Write(ArchiveFormat format, string source, string target)
        {
            switch (format)
            {
                case ArchiveFormat.Zip when password is not null: WriteEncryptedZip(source, target, password); break;
                case ArchiveFormat.Zip: WriteZip(source, target); break;
                case ArchiveFormat.SevenZip: WriteSevenZip(source, target); break;
                default: WriteTar(source, target, format == ArchiveFormat.TarGz); break;
            }
        }

        public void Verify(string part) => ArchiveEditor.Verify(part, _expected, (key, read) => Advance(L.F("Vérification : {0}", key), read), password);

        private void Copy(string key, Stream data, Action<Stream> write)
        {
            var tracked = new TrackedStream(data, read => Advance(key, read));
            write(tracked);
            if (!_expected.TryGetValue(key, out var hashes)) _expected[key] = hashes = new Queue<byte[]>();
            hashes.Enqueue(tracked.Finish());
            _files++;
        }

        private IEnumerable<string> RemainingFolders() => plan.Folders.Where(_writtenFolders.Add);

        private void WriteZip(string source, string target)
        {
            using var input = ZipFile.OpenRead(source);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize);
            using var zip = new ZipArchive(output, ZipArchiveMode.Create);
            zip.Comment = input.Comment;

            foreach (var entry in input.Entries)
            {
                bool isFolder = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
                if (plan.NewKey(ArchivePath.NormalizeKey(entry.FullName)) is not { Length: > 0 } key) continue;
                if (isFolder && !_writtenFolders.Add(key)) continue;
                var copy = zip.CreateEntry(isFolder ? key + "/" : key, CompressionLevel.Optimal);
                copy.LastWriteTime = entry.LastWriteTime;
                copy.ExternalAttributes = entry.ExternalAttributes;
                copy.Comment = entry.Comment;
                if (isFolder) continue;
                using var data = entry.Open();
                Copy(key, data, tracked => { using var written = copy.Open(); tracked.CopyTo(written, BufferSize); });
            }
            foreach (var file in plan.Added)
            {
                var copy = zip.CreateEntry(file.Key, CompressionLevel.Optimal);
                copy.LastWriteTime = file.Modified;
                using var data = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                Copy(file.Key, data, tracked => { using var written = copy.Open(); tracked.CopyTo(written, BufferSize); });
            }
            foreach (var folder in RemainingFolders()) zip.CreateEntry(folder + "/");
        }

        /// <summary>
        /// Toujours en AES-256 avec le même mot de passe, même si l'archive d'origine utilisait l'ancien chiffrement du zip.
        /// .NET ne sait pas déchiffrer : il donne seulement les attributs et les commentaires.
        /// </summary>
        private void WriteEncryptedZip(string source, string target, string zipPassword)
        {
            using var details = ZipFile.OpenRead(source);
            var known = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in details.Entries) known.TryAdd(ArchivePath.NormalizeKey(entry.FullName), entry);

            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize);
            using var zip = EncryptedZip.Open(output, zipPassword);
            zip.SetComment(details.Comment);

            foreach (var entry in ArchiveReading.Read(source, zipPassword))
            {
                var oldKey = ArchivePath.NormalizeKey(entry.Key);
                if (plan.NewKey(oldKey) is not { Length: > 0 } key) continue;
                known.TryGetValue(oldKey, out var detail);
                if (entry.IsDirectory)
                {
                    if (_writtenFolders.Add(key)) EncryptedZip.AddFolder(zip, key, entry.Modified, detail?.ExternalAttributes ?? 0);
                    continue;
                }
                using var data = entry.Open();
                EncryptedZip.BeginFile(zip, key, entry.Size, entry.Modified, detail?.ExternalAttributes ?? 0, detail?.Comment);
                Copy(key, data, tracked => tracked.CopyTo(zip, BufferSize));
                zip.CloseEntry();
            }
            foreach (var file in plan.Added)
            {
                using var data = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                EncryptedZip.BeginFile(zip, file.Key, file.Size, file.Modified);
                Copy(file.Key, data, tracked => tracked.CopyTo(zip, BufferSize));
                zip.CloseEntry();
            }
            foreach (var folder in RemainingFolders()) EncryptedZip.AddFolder(zip, folder, DateTime.Now);
        }

        private void WriteSevenZip(string source, string target)
        {
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize);
            using var writer = new SevenZipWriter(output, new SevenZipWriterOptions(CompressionType.LZMA));
            foreach (var entry in ArchiveReading.Read(source))
            {
                if (plan.NewKey(ArchivePath.NormalizeKey(entry.Key)) is not { Length: > 0 } key) continue;
                if (entry.IsDirectory)
                {
                    if (_writtenFolders.Add(key)) writer.WriteDirectory(key, entry.Modified);
                    continue;
                }
                using var data = entry.Open();
                Copy(key, data, tracked => writer.Write(key, tracked, entry.Modified));
            }
            foreach (var file in plan.Added)
            {
                using var data = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                Copy(file.Key, data, tracked => writer.Write(file.Key, tracked, file.Modified));
            }
            foreach (var folder in RemainingFolders()) writer.WriteDirectory(folder, DateTime.Now);
        }

        /// <summary>Les entrées sont recopiées telles quelles : liens, droits et propriétaires ne doivent pas se perdre.</summary>
        private void WriteTar(string source, string target, bool gzip)
        {
            using var input = OpenSeekableTar(source, gzip);
            using var reader = new TarReader(input);
            using var outputFile = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize);
            using Stream output = gzip ? new GZipStream(outputFile, CompressionLevel.Optimal) : outputFile;
            using var writer = new TarWriter(output, TarEntryFormat.Gnu, leaveOpen: true);
            int entries = 0;

            while (reader.GetNextEntry() is { } entry)
            {
                bool isFolder = entry.EntryType == TarEntryType.Directory;
                if (entry.EntryType is TarEntryType.GlobalExtendedAttributes)
                {
                    writer.WriteEntry(entry);
                    continue; // réglage de l'archive : il ne compte pas comme contenu
                }
                if (plan.NewKey(ArchivePath.NormalizeKey(entry.Name)) is not { Length: > 0 } key) continue;
                var name = isFolder ? key + "/" : key;
                if (name != entry.Name) entry.Name = name;
                if (isFolder) _writtenFolders.Add(key);
                if (entry.DataStream is { } data && entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile)
                {
                    // Remplacer ce flux le fermerait : on le lit pour l'empreinte, puis on revient au début pour l'écriture.
                    Copy(key, data, tracked => tracked.CopyTo(Stream.Null, BufferSize));
                    data.Position = 0;
                }
                writer.WriteEntry(entry);
                entries++;
            }
            foreach (var file in plan.Added)
            {
                using var data = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
                Copy(file.Key, data, tracked => writer.WriteEntry(new GnuTarEntry(TarEntryType.RegularFile, file.Key) { DataStream = tracked, ModificationTime = file.Modified }));
                entries++;
            }
            foreach (var folder in RemainingFolders())
            {
                writer.WriteEntry(new GnuTarEntry(TarEntryType.Directory, folder + "/"));
                entries++;
            }
            // Sans aucune entrée, rien ne serait écrit : un fichier de 0 octet n'est pas une archive valable.
            if (entries == 0) output.Write(new byte[EmptyTarBytes]);
        }

        /// <summary>Le .tar.gz est d'abord décompressé dans un fichier temporaire : la lecture a besoin de revenir en arrière.</summary>
        private Stream OpenSeekableTar(string source, bool gzip)
        {
            var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
            if (!gzip) return file;
            using (file)
            using (var unpacked = new GZipStream(file, CompressionMode.Decompress))
            {
                var temp = new FileStream(Path.GetTempFileName(), FileMode.Open, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.DeleteOnClose);
                try
                {
                    var buffer = new byte[BufferSize];
                    int read;
                    while ((read = unpacked.Read(buffer)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        temp.Write(buffer, 0, read);
                    }
                    temp.Position = 0;
                    return temp;
                }
                catch
                {
                    temp.Dispose();
                    throw;
                }
            }
        }
    }
}
