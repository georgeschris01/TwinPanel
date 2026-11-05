// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using System.Runtime.Versioning;

namespace TwinPanel.Core;

/// <summary>Que faire quand la destination existe déjà.</summary>
public enum ConflictPolicy
{
    Skip,
    Overwrite,
    /// <summary>Écrase seulement si la source est plus récente.</summary>
    OverwriteOlder,
    /// <summary>Garde les deux : la copie devient « nom (2).ext ».</summary>
    RenameNew,
}

/// <summary>Avancement d'une opération (copie, déplacement, suppression).</summary>
public sealed record TransferProgress(string CurrentItem, long BytesDone, long BytesTotal, int FilesDone, int FilesTotal)
{
    public double Fraction => BytesTotal > 0 ? (double)BytesDone / BytesTotal : FilesTotal > 0 ? (double)FilesDone / FilesTotal : 1;

    /// <summary>Message à montrer tel quel à la place des chiffres (connexion perdue…).</summary>
    public string? Notice { get; init; }
}

public sealed class FileOperationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Permet de mettre une opération en pause puis de la reprendre.</summary>
public sealed class PauseController
{
    private readonly ManualResetEventSlim _running = new(initialState: true);
    public bool IsPaused => !_running.IsSet;
    public void Pause() => _running.Reset();
    public void Resume() => _running.Set();
    public void WaitIfPaused(CancellationToken ct) => _running.Wait(ct);

    /// <summary>L'opération se met elle-même en pause, avec une raison à montrer (plus de place sur le disque).</summary>
    public event Action<string>? PauseRequested;

    public void PauseFor(string reason)
    {
        Pause();
        PauseRequested?.Invoke(reason);
    }
}

/// <summary>Opérations sur les fichiers (copie, déplacement, suppression, création).</summary>
public static class FileOperations
{
    private const int BufferSize = 1024 * 1024;
    private const string PartSuffix = ".twinpanel-part";

    /// <summary>Pour les tests : une erreur à la place de la copie de ce fichier (un disque plein ne se simule pas autrement).</summary>
    internal static Func<string, Exception?>? CopyFault { get; set; }

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Liste les chemins de destination qui existent déjà (pour demander confirmation avant).</summary>
    public static IReadOnlyList<string> FindConflicts(IEnumerable<string> sources, string destDir) =>
        sources.Select(s => Path.Combine(destDir, Path.GetFileName(TrimEnd(s))))
               .Where(p => File.Exists(p) || Directory.Exists(p))
               .ToList();

    /// <summary>Le détail n'est calculé que s'il semble manquer de la place : c'est rare, et il faut alors regarder chaque fichier.</summary>
    private static void CheckRoom(IReadOnlyList<string> sources, string destDir, ConflictPolicy policy, long bytes, bool move, PauseController? pause, CancellationToken ct)
    {
        if (DiskSpace.FreeBytes(destDir) is not { } free || free >= bytes) return;
        DiskSpace.CheckBeforeStart(destDir, NeededBytes(sources, destDir, policy, move, ct), pause, ct);
    }

    /// <summary>
    /// Place vraiment nécessaire : un fichier ignoré ne prend rien, un fichier remplacé rend la place de l'ancien.
    /// Mais il s'écrit à côté avant de le remplacer : le plus gros des fichiers remplacés doit tenir en entier.
    /// </summary>
    internal static long NeededBytes(IEnumerable<string> sources, string destDir, ConflictPolicy policy, bool move, CancellationToken ct)
    {
        long total = 0, largestReplaced = 0;
        void Count(FileInfo file, string target)
        {
            if (!File.Exists(target) || policy == ConflictPolicy.RenameNew) total += file.Length;
            else if (policy != ConflictPolicy.Skip)
            {
                total += Math.Max(0, file.Length - new FileInfo(target).Length);
                largestReplaced = Math.Max(largestReplaced, file.Length);
            }
        }
        var destRoot = Path.GetPathRoot(Path.GetFullPath(destDir));
        foreach (var source in sources)
        {
            var src = TrimEnd(source);
            if (move && string.Equals(Path.GetPathRoot(Path.GetFullPath(src)), destRoot, PathComparison)) continue;
            var dest = Path.Combine(destDir, Path.GetFileName(src));
            if (File.Exists(src)) Count(new FileInfo(src), dest);
            else if (Directory.Exists(src))
                foreach (var file in EnumerateFilesSafe(src, ct)) Count(file, Path.Combine(dest, Path.GetRelativePath(src, file.FullName)));
        }
        return Math.Max(total, largestReplaced);
    }

    /// <summary>Compte les fichiers et les octets à traiter (pour la barre de progression).</summary>
    public static (int Files, long Bytes) Measure(IEnumerable<string> sources, CancellationToken ct = default)
    {
        int files = 0;
        long bytes = 0;
        foreach (var s in sources)
        {
            ct.ThrowIfCancellationRequested();
            var p = TrimEnd(s);
            if (File.Exists(p)) { files++; bytes += new FileInfo(p).Length; }
            else if (Directory.Exists(p))
            {
                foreach (var f in EnumerateFilesSafe(p, ct)) { files++; bytes += f.Length; }
            }
        }
        return (files, bytes);
    }

    public static Task CopyAsync(IReadOnlyList<string> sources, string destDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null) =>
        Task.Run(() =>
        {
            ValidateTarget(sources, destDir);
            var totals = Measure(sources, ct);
            var ctx = new Context(progress, ct, pause, totals);
            CheckRoom(sources, destDir, policy, totals.Bytes, move: false, pause, ct);
            foreach (var s in sources)
            {
                var src = TrimEnd(s);
                CopyItem(src, Path.Combine(destDir, Path.GetFileName(src)), policy, ctx, topLevel: true);
            }
            ctx.Report(string.Empty, force: true);
        }, ct);

    public static Task MoveAsync(IReadOnlyList<string> sources, string destDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null) =>
        Task.Run(() =>
        {
            ValidateTarget(sources, destDir);
            var totals = Measure(sources, ct);
            var ctx = new Context(progress, ct, pause, totals);
            CheckRoom(sources, destDir, policy, totals.Bytes, move: true, pause, ct);
            foreach (var s in sources)
            {
                var src = TrimEnd(s);
                MoveItem(src, Path.Combine(destDir, Path.GetFileName(src)), policy, ctx, topLevel: true);
            }
            ctx.Report(string.Empty, force: true);
        }, ct);

    /// <summary>
    /// Copie une liste de fichiers vers des chemins précis (dossiers créés au besoin, cibles remplacées).
    /// Utilisé par la synchronisation de dossiers.
    /// </summary>
    public static Task CopyFilesAsync(IReadOnlyList<(string Source, string Destination)> pairs,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null) =>
        Task.Run(() =>
        {
            var ctx = new Context(progress, ct, pause, Measure(pairs.Select(p => p.Source), ct));
            foreach (var (src, dest) in pairs)
            {
                ctx.Checkpoint();
                if (!File.Exists(src)) throw new FileOperationException(L.F("Source introuvable : {0}", src));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyFileWithProgress(src, dest, ctx);
            }
            ctx.Report(string.Empty, force: true);
        }, ct);

    /// <summary>Crée un sous-dossier. Retourne son chemin complet.</summary>
    public static string CreateDirectory(string parentDir, string name)
    {
        var path = Path.Combine(parentDir, ValidateName(name));
        if (File.Exists(path) || Directory.Exists(path))
            throw new FileOperationException(L.F("« {0} » existe déjà.", name.Trim()));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Crée un fichier vide. Retourne son chemin complet.</summary>
    public static string CreateFile(string parentDir, string name)
    {
        var path = Path.Combine(parentDir, ValidateName(name));
        if (File.Exists(path) || Directory.Exists(path))
            throw new FileOperationException(L.F("« {0} » existe déjà.", name.Trim()));
        using (File.Create(path)) { }
        return path;
    }

    /// <summary>Renomme un fichier ou un dossier dans le même dossier. Retourne le nouveau chemin.</summary>
    public static string Rename(string path, string newName)
    {
        path = TrimEnd(path);
        newName = ValidateName(newName);
        var dest = Path.Combine(Path.GetDirectoryName(path)!, newName);
        if (string.Equals(path, dest, StringComparison.Ordinal)) return path;

        bool onlyCaseChanges = string.Equals(path, dest, PathComparison);
        if (!onlyCaseChanges && (File.Exists(dest) || Directory.Exists(dest)))
            throw new FileOperationException(L.F("« {0} » existe déjà.", newName));

        if (Directory.Exists(path))
        {
            if (onlyCaseChanges)
            {
                // Windows refuse "a" → "A" en un seul déplacement : on passe par un nom temporaire.
                var tmp = path + "." + Guid.NewGuid().ToString("N")[..8];
                Directory.Move(path, tmp);
                Directory.Move(tmp, dest);
            }
            else Directory.Move(path, dest);
        }
        else File.Move(path, dest);
        return dest;
    }

    /// <summary>Supprime les éléments, ou les met à la corbeille si demandé (Windows et Linux).</summary>
    public static Task DeleteAsync(IReadOnlyList<string> paths, bool useRecycleBin,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            for (int i = 0; i < paths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = TrimEnd(paths[i]);
                progress?.Report(new TransferProgress(p, 0, 0, i, paths.Count));

                if (useRecycleBin && OperatingSystem.IsWindows())
                    RecycleBin.Delete(p);
                else if (useRecycleBin && OperatingSystem.IsLinux())
                    DesktopTrash.Delete(p);
                else if (Directory.Exists(p))
                    Directory.Delete(p, recursive: true);
                else if (File.Exists(p))
                    File.Delete(p);
            }
            progress?.Report(new TransferProgress(string.Empty, 0, 0, paths.Count, paths.Count));
        }, ct);

    /// <summary>« nom.ext » → « nom (2).ext », « nom (3).ext »… jusqu'à trouver un nom libre.</summary>
    public static string MakeUniqueName(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        bool isDir = Directory.Exists(path);
        var name = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var ext = isDir ? string.Empty : Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    // ---------------------------------------------------------------------

    private sealed class Context(IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause, (int Files, long Bytes) totals)
    {
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();
        public CancellationToken Token { get; } = ct;
        public PauseController? Pause { get; } = pause;
        public long BytesDone;
        public int FilesDone;

        public void Checkpoint()
        {
            Token.ThrowIfCancellationRequested();
            Pause?.WaitIfPaused(Token);
        }

        private bool _reportedOnce;

        /// <summary>Signale l'avancement au plus toutes les 100 ms (sauf le premier signal et force).</summary>
        public void Report(string item, bool force = false)
        {
            if (progress is null || (!force && _reportedOnce && _sinceReport.ElapsedMilliseconds < 100)) return;
            _reportedOnce = true;
            _sinceReport.Restart();
            progress.Report(new TransferProgress(item, BytesDone, totals.Bytes, FilesDone, totals.Files));
        }

        public void FileSkipped(long size, string item)
        {
            BytesDone += size;
            FilesDone++;
            Report(item);
        }
    }

    private static void ValidateTarget(IReadOnlyList<string> sources, string destDir)
    {
        if (!Directory.Exists(destDir))
            throw new FileOperationException(L.F("Dossier de destination introuvable : {0}", destDir));

        var destFull = WithSeparator(Path.GetFullPath(destDir));
        foreach (var s in sources)
        {
            var srcFull = Path.GetFullPath(TrimEnd(s));
            if (!File.Exists(srcFull) && !Directory.Exists(srcFull))
                throw new FileOperationException(L.F("Source introuvable : {0}", srcFull));
            if (Directory.Exists(srcFull) && destFull.StartsWith(WithSeparator(srcFull), PathComparison))
                throw new FileOperationException(L.F("Impossible de copier/déplacer « {0} » dans lui-même.", srcFull));
            if (string.Equals(Path.GetDirectoryName(srcFull), TrimEnd(destFull), PathComparison))
                throw new FileOperationException(L.T("La source et la destination sont le même dossier."));
        }
    }

    /// <summary>Décide du chemin final selon la politique ; null = ignorer ce fichier.</summary>
    private static string? ResolveFileConflict(string src, string dest, ConflictPolicy policy)
    {
        if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;
        if (Directory.Exists(dest))
            throw new FileOperationException(L.F("Un dossier porte déjà le nom « {0} ».", Path.GetFileName(dest)));

        return policy switch
        {
            ConflictPolicy.Skip => null,
            ConflictPolicy.Overwrite => dest,
            ConflictPolicy.OverwriteOlder => File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dest) ? dest : null,
            ConflictPolicy.RenameNew => MakeUniqueName(dest),
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
    }

    private static void CopyItem(string src, string dest, ConflictPolicy policy, Context ctx, bool topLevel)
    {
        ctx.Checkpoint();
        if (File.Exists(src))
        {
            var target = ResolveFileConflict(src, dest, policy);
            if (target is null) { ctx.FileSkipped(new FileInfo(src).Length, src); return; }
            CopyFileWithProgress(src, target, ctx);
        }
        else if (Directory.Exists(src))
        {
            if (File.Exists(dest))
                throw new FileOperationException(L.F("Un fichier porte déjà le nom « {0} ».", Path.GetFileName(dest)));
            if (topLevel && policy == ConflictPolicy.RenameNew && Directory.Exists(dest))
                dest = MakeUniqueName(dest);

            Directory.CreateDirectory(dest);
            foreach (var entry in new DirectoryInfo(src).EnumerateFileSystemInfos())
            {
                // On ne suit pas les liens symboliques de dossiers (évite les boucles infinies).
                if (entry is DirectoryInfo { LinkTarget: not null }) continue;
                CopyItem(entry.FullName, Path.Combine(dest, entry.Name), policy, ctx, topLevel: false);
            }
        }
        else
        {
            throw new FileOperationException(L.F("Source introuvable : {0}", src));
        }
    }

    /// <summary>
    /// Copie par blocs dans un fichier temporaire « .twinpanel-part », puis le renomme :
    /// un fichier existant n'est jamais abîmé par une copie interrompue.
    /// </summary>
    private static void CopyFileWithProgress(string src, string dest, Context ctx)
    {
        long before = ctx.BytesDone;
        while (true)
        {
            try
            {
                CopyFileOnce(src, dest, ctx);
                return;
            }
            catch (IOException ex) when (DiskSpace.IsFull(ex))
            {
                // Le fichier incomplet est déjà effacé. Une fois de la place libérée, on repart de ce fichier : rien n'est perdu.
                long size = new FileInfo(src).Length;
                if (ctx.Pause is null) throw new FileOperationException(DiskSpace.FullMessage(dest, size, canResume: false), ex);
                ctx.BytesDone = before;
                ctx.Report(src, force: true);
                ctx.Pause.PauseFor(DiskSpace.FullMessage(dest, size, canResume: true));
                ctx.Checkpoint();
            }
        }
    }

    private static void CopyFileOnce(string src, string dest, Context ctx)
    {
        var part = dest + PartSuffix;
        var info = new FileInfo(src);
        try
        {
            if (CopyFault?.Invoke(src) is { } fault) throw fault;
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                output.SetLength(info.Length); // réserve la place d'un coup (moins de fragmentation)
                var buffer = new byte[BufferSize];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ctx.Checkpoint();
                    output.Write(buffer, 0, read);
                    ctx.BytesDone += read;
                    ctx.Report(src);
                }
            }

            File.SetLastWriteTimeUtc(part, info.LastWriteTimeUtc);
            File.SetCreationTimeUtc(part, info.CreationTimeUtc);

            if (File.Exists(dest))
                File.SetAttributes(dest, FileAttributes.Normal); // une cible en lecture seule bloquerait le remplacement
            File.Move(part, dest, overwrite: true);
            File.SetAttributes(dest, info.Attributes);
        }
        catch
        {
            TryDelete(part);
            throw;
        }
        ctx.FilesDone++;
        ctx.Report(src);
    }

    private static void MoveItem(string src, string dest, ConflictPolicy policy, Context ctx, bool topLevel)
    {
        ctx.Checkpoint();
        bool sameVolume = string.Equals(Path.GetPathRoot(Path.GetFullPath(src)),
                                        Path.GetPathRoot(Path.GetFullPath(dest)), PathComparison);
        bool destExists = File.Exists(dest) || Directory.Exists(dest);

        if (File.Exists(src))
        {
            long size = new FileInfo(src).Length;
            var target = ResolveFileConflict(src, dest, policy);
            if (target is null) { ctx.FileSkipped(size, src); return; }

            if (sameVolume)
            {
                if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                File.Move(src, target, overwrite: true); // instantané sur le même disque
                ctx.BytesDone += size;
                ctx.FilesDone++;
                ctx.Report(src);
            }
            else
            {
                CopyFileWithProgress(src, target, ctx);
                File.SetAttributes(src, FileAttributes.Normal);
                File.Delete(src);
            }
            return;
        }

        if (!Directory.Exists(src)) throw new FileOperationException(L.F("Source introuvable : {0}", src));
        if (File.Exists(dest))
            throw new FileOperationException(L.F("Un fichier porte déjà le nom « {0} ».", Path.GetFileName(dest)));

        if (topLevel && policy == ConflictPolicy.RenameNew && destExists)
        {
            dest = MakeUniqueName(dest);
            destExists = false;
        }

        if (sameVolume && !destExists)
        {
            var (files, bytes) = Measure([src], ctx.Token);
            Directory.Move(src, dest);
            ctx.BytesDone += bytes;
            ctx.FilesDone += files;
            ctx.Report(src);
            return;
        }

        // Autre disque ou fusion avec un dossier existant : élément par élément.
        Directory.CreateDirectory(dest);
        foreach (var entry in new DirectoryInfo(src).EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo { LinkTarget: not null }) continue;
            MoveItem(entry.FullName, Path.Combine(dest, entry.Name), policy, ctx, topLevel: false);
        }
        // Le dossier source n'est supprimé que s'il est vide (fichiers ignorés = conservés).
        if (!Directory.EnumerateFileSystemEntries(src).Any()) Directory.Delete(src);
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(string dir, CancellationToken ct)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", options))
        {
            ct.ThrowIfCancellationRequested();
            yield return f;
        }
    }

    internal static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new FileOperationException(L.F("Nom invalide : « {0} »", name));
        return name;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* fichier temporaire : sera écrasé à la prochaine tentative */ }
        catch (UnauthorizedAccessException) { }
    }

    internal static string TrimEnd(string path) =>
        path.Length > 3 ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;

    private static string WithSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}

[SupportedOSPlatform("windows")]
internal static class RecycleBin
{
    public static void Delete(string path)
    {
        // Microsoft.VisualBasic fait partie du framework .NET sous Windows : pas de dépendance externe.
        if (Directory.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        else if (File.Exists(path))
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }
}
