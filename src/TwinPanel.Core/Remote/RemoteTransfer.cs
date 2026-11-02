// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using TwinPanel.Core.Operations;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Copies, déplacements et suppressions avec un serveur (dans les deux sens), dossiers compris.
/// Même règles que les copies locales : politique en cas de conflit, fichier « .twinpanel-part » puis remplacement,
/// date de modification conservée, pause et annulation.
/// </summary>
public static class RemoteTransfer
{
    public const string PartSuffix = ".twinpanel-part";
    /// <summary>Fichier remplacé, gardé à côté le temps de mettre le nouveau à sa place.</summary>
    public const string AsideSuffix = ".twinpanel-old";

    private sealed class Reporter(IProgress<TransferProgress>? progress, long bytesTotal, int filesTotal)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastReport = -1000;
        public long BytesDone;
        public int FilesDone;
        public string Current = string.Empty;

        public void Bytes(long n) { Interlocked.Add(ref BytesDone, n); Report(); }
        public void AddTotal(long n) => bytesTotal += n;
        public void FileDone(long sizeNotCounted = 0) { BytesDone += sizeNotCounted; FilesDone++; Report(force: true); }
        public void Start(string item) { Current = item; Report(force: true); }

        public void Report(bool force = false)
        {
            if (progress is null) return;
            var now = _clock.ElapsedMilliseconds;
            if (!force && now - _lastReport < 100) return;
            _lastReport = now;
            progress.Report(new TransferProgress(Current, Math.Min(BytesDone, bytesTotal), bytesTotal, FilesDone, filesTotal));
        }
    }

    // ---- Serveur → disque ------------------------------------------------------------------------------------------

    public static async Task DownloadAsync(RemoteSession session, IReadOnlyList<RemoteItem> items, string localDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null, bool deleteSource = false, TransferResume? resume = null)
    {
        if (resume is not null)
        {
            await resume.RunAsync(session.OpenWorkerAsync, fs => DownloadAsync(fs, items, localDir, policy, progress, ct, pause, deleteSource, resume), progress, ct).ConfigureAwait(false);
            return;
        }
        await using var worker = await session.OpenWorkerAsync(ct).ConfigureAwait(false);
        await DownloadAsync(worker, items, localDir, policy, progress, ct, pause, deleteSource).ConfigureAwait(false);
    }

    /// <param name="resume">Null : le morceau transféré est retiré à la moindre erreur. Sinon, il est gardé après une coupure.</param>
    public static async Task DownloadAsync(IRemoteFileSystem fs, IReadOnlyList<RemoteItem> items, string localDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null, bool deleteSource = false, TransferResume? resume = null)
    {
        var plan = resume?.Plan ?? await PlanDownloadAsync(fs, items, localDir, policy, ct).ConfigureAwait(false);
        if (resume is not null) resume.Plan = plan;

        var reporter = new Reporter(progress, plan.BytesTotal, plan.Files.Count) { BytesDone = plan.BytesDone, FilesDone = plan.FilesDone };
        foreach (var dir in plan.Dirs) Directory.CreateDirectory(dir.Path);
        if (pause is not null) await Task.Run(() => DiskSpace.CheckBeforeStart(localDir, plan.BytesTotal - plan.BytesDone, pause, ct), ct).ConfigureAwait(false);

        foreach (var file in plan.Files.Where(f => !f.Done))
        {
            ct.ThrowIfCancellationRequested();
            var name = RemotePath.FileName(file.Source);
            reporter.Start(name);
            if (!file.Resolved)
            {
                var target = ResolveLocalConflict(file.Target, new RemoteItem(name, file.Source, false, file.Size, file.Modified), policy);
                if (target is null)
                {
                    file.Done = true;
                    reporter.FileDone(file.Size);
                    plan.Broken.Add(file.Top);
                    continue;
                }
                file.Target = target;
                file.Resolved = true;
            }
            for (bool done = false; !done;)
            {
                long before = reporter.BytesDone;
                try
                {
                    await DownloadFileAsync(fs, file, reporter, resume, pause, ct).ConfigureAwait(false);
                    done = true;
                }
                catch (IOException ex) when (DiskSpace.IsFull(ex) && pause is not null)
                {
                    reporter.BytesDone = before;
                    pause.PauseFor(DiskSpace.FullMessage(file.Target, file.Size, canResume: true));
                    await Task.Run(() => pause.WaitIfPaused(ct), ct).ConfigureAwait(false);
                }
            }
            file.Done = true;
            reporter.FileDone();
        }

        if (deleteSource)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (plan.Broken.Contains(i) || plan.Deleted.Contains(i)) continue;
                await DeleteTreeAsync(fs, items[i], null, ct).ConfigureAwait(false);
                plan.Deleted.Add(i);
            }
        }
        ThrowIfSkipped(plan.Skipped);
    }

    private static async Task<TransferPlan> PlanDownloadAsync(IRemoteFileSystem fs, IReadOnlyList<RemoteItem> items, string localDir, ConflictPolicy policy, CancellationToken ct)
    {
        var plan = new TransferPlan();

        async Task Walk(RemoteItem item, string target, int top, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (!RemotePath.IsValidWindowsName(item.Name))
            {
                plan.Skipped.Add(L.F("{0} (nom impossible sous Windows)", item.FullPath));
                plan.Broken.Add(top);
                return;
            }
            if (!item.IsDirectory)
            {
                plan.Files.Add(new TransferPlanFile { Source = item.FullPath, Target = target, Size = item.Size, Modified = item.Modified, Top = top });
                return;
            }
            if (item.IsLink && depth > 0)
            {
                plan.Skipped.Add(L.F("{0} (lien vers un dossier, non suivi)", item.FullPath));
                plan.Broken.Add(top);
                return;
            }
            if (depth > 64) throw new RemoteException(L.F("Arborescence trop profonde : {0}", item.FullPath));
            plan.Dirs.Add(new TransferPlanDir { Path = target });
            foreach (var child in await fs.ListAsync(item.FullPath, ct).ConfigureAwait(false))
                await Walk(child, Path.Combine(target, child.Name), top, depth + 1).ConfigureAwait(false);
        }

        for (int i = 0; i < items.Count; i++)
        {
            var target = Path.Combine(localDir, items[i].Name);
            if (items[i].IsDirectory && policy == ConflictPolicy.RenameNew && (Directory.Exists(target) || File.Exists(target)))
                target = FileOperations.MakeUniqueName(target);
            if (items[i].IsDirectory && policy == ConflictPolicy.Skip && Directory.Exists(target)) { plan.Broken.Add(i); continue; }
            await Walk(items[i], target, i, 0).ConfigureAwait(false);
        }
        return plan;
    }

    private static async Task DownloadFileAsync(IRemoteFileSystem fs, TransferPlanFile file, Reporter reporter, TransferResume? resume, PauseController? pause, CancellationToken ct)
    {
        var part = file.Target + PartSuffix;
        long before = reporter.BytesDone;
        for (bool fromZero = false; ; fromZero = true)
        {
            long offset = fromZero ? 0 : await DownloadOffsetAsync(fs, file, part, resume, ct).ConfigureAwait(false);
            if (offset == 0 && file.Started) reporter.Start(L.F("{0} (repart de zéro)", RemotePath.FileName(file.Source)));
            if (offset == 0 && resume is not null && file.Size >= resume.MinBytes)
            {
                // Le point de comparaison vient du même appel qu'à la reprise : une liste et un Stat ne donnent pas toujours la même date.
                var now = await fs.StatAsync(file.Source, ct).ConfigureAwait(false);
                file.StampSize = now?.Size ?? file.Size;
                file.StampModified = now?.Modified ?? file.Modified;
                file.Started = true;
            }

            ResumeTailStream? tail = null;
            // Lu avant d'ouvrir le morceau en écriture : Windows refuserait de l'ouvrir deux fois.
            byte[] expected = offset > 0 ? ReadLocal(part, offset, Math.Min(offset, TransferResume.TailBytes)) : [];
            try
            {
                await using (var output = new FileStream(part, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                await using (var counted = new TransferStream(output, reporter.Bytes, ct, pause, leaveOpen: true))
                {
                    if (offset > 0)
                    {
                        output.SetLength(offset);
                        output.Position = offset;
                        reporter.Bytes(offset);
                        tail = new ResumeTailStream(counted, expected);
                        await fs.DownloadFromAsync(file.Source, tail, offset - expected.Length, ct).ConfigureAwait(false);
                        await output.FlushAsync(ct).ConfigureAwait(false);
                        if (!tail.Verified || output.Length != file.StampSize) throw new IOException(L.T("Le fichier reçu n'a pas la bonne taille : la reprise est abandonnée."));
                    }
                    else await fs.DownloadAsync(file.Source, counted, ct).ConfigureAwait(false);
                }
                File.Move(part, file.Target, overwrite: true);
                try { File.SetLastWriteTime(file.Target, file.Modified); } catch (IOException) { }
                return;
            }
            catch (IOException ex) when (DiskSpace.IsFull(ex))
            {
                // Rien à voir avec le serveur : ni reprise, ni nouvel essai tant que la place manque.
                TryDeleteLocal(part);
                file.Started = false;
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && offset > 0 && !fromZero
                                       && (tail?.Mismatch == true || !ConnectionFailure.IsLost(ex, fs)))
            {
                // Reprise refusée par le serveur, ou parties différentes : on recommence ce fichier.
                TryDeleteLocal(part);
                reporter.BytesDone = before;
            }
            catch (Exception ex) when (file.Started && ex is not OperationCanceledException && ConnectionFailure.IsLost(ex, fs))
            {
                throw; // coupure : le morceau reste pour la reprise
            }
            catch
            {
                TryDeleteLocal(part);
                file.Started = false;
                throw;
            }
        }
    }

    /// <summary>Position de reprise ; 0 si la source a changé, ou si le morceau n'est pas utilisable.</summary>
    private static async Task<long> DownloadOffsetAsync(IRemoteFileSystem fs, TransferPlanFile file, string part, TransferResume? resume, CancellationToken ct)
    {
        if (resume is null || !file.Started || !fs.CanResumeDownload) return 0;
        var local = new FileInfo(part);
        if (!local.Exists || local.Length == 0) return 0;
        var now = await fs.StatAsync(file.Source, ct).ConfigureAwait(false);
        if (now is null || now.Size != file.StampSize || (now.Modified - file.StampModified).Duration() > TimeSpan.FromSeconds(2)) return 0;
        return local.Length <= now.Size ? local.Length : 0;
    }

    private static byte[] ReadLocal(string path, long end, long count)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        input.Position = end - count;
        var buffer = new byte[count];
        input.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Chemin final selon la politique, ou null pour ignorer.</summary>
    private static string? ResolveLocalConflict(string target, RemoteItem item, ConflictPolicy policy)
    {
        if (!File.Exists(target))
        {
            if (Directory.Exists(target)) throw new FileOperationException(L.F("« {0} » existe déjà comme dossier.", Path.GetFileName(target)));
            return target;
        }
        return policy switch
        {
            ConflictPolicy.Skip => null,
            ConflictPolicy.OverwriteOlder => item.Modified - File.GetLastWriteTime(target) > TimeSpan.FromSeconds(2) ? target : null,
            ConflictPolicy.RenameNew => FileOperations.MakeUniqueName(target),
            _ => target,
        };
    }

    // ---- Disque → serveur -----------------------------------------------------------------------------------------

    public static async Task UploadAsync(RemoteSession session, IReadOnlyList<string> localPaths, string remoteDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null, bool deleteSource = false, TransferResume? resume = null)
    {
        if (resume is not null)
        {
            await resume.RunAsync(session.OpenWorkerAsync, fs => UploadAsync(fs, localPaths, remoteDir, policy, progress, ct, pause, deleteSource, resume), progress, ct).ConfigureAwait(false);
            return;
        }
        await using var worker = await session.OpenWorkerAsync(ct).ConfigureAwait(false);
        await UploadAsync(worker, localPaths, remoteDir, policy, progress, ct, pause, deleteSource).ConfigureAwait(false);
    }

    /// <param name="resume">Null : le morceau envoyé est retiré à la moindre erreur. Sinon, il est gardé après une coupure.</param>
    public static async Task UploadAsync(IRemoteFileSystem fs, IReadOnlyList<string> localPaths, string remoteDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null, bool deleteSource = false, TransferResume? resume = null)
    {
        // Contenu des dossiers distants concernés (lu une fois chacun).
        var listings = new Dictionary<string, Dictionary<string, RemoteItem>>(StringComparer.Ordinal);
        async Task<Dictionary<string, RemoteItem>> Listing(string dir)
        {
            if (!listings.TryGetValue(dir, out var map))
            {
                map = (await fs.ListAsync(dir, ct).ConfigureAwait(false)).ToDictionary(i => i.Name, StringComparer.Ordinal);
                listings[dir] = map;
            }
            return map;
        }

        bool again = resume?.Plan is not null;
        var plan = resume?.Plan ?? await PlanUploadAsync(localPaths, remoteDir, policy, Listing, ct).ConfigureAwait(false);
        if (resume is not null) resume.Plan = plan;

        var reporter = new Reporter(progress, plan.BytesTotal, plan.Files.Count) { BytesDone = plan.BytesDone, FilesDone = plan.FilesDone };
        foreach (var dir in plan.Dirs.Where(d => !d.Exists && !d.Done))
        {
            // Après une coupure, on ne sait pas si la dernière création a abouti.
            if (!again || await fs.StatAsync(dir.Path, ct).ConfigureAwait(false) is null)
            {
                await fs.CreateDirectoryAsync(dir.Path, ct).ConfigureAwait(false);
                listings[dir.Path] = new Dictionary<string, RemoteItem>(StringComparer.Ordinal);
                if (RemotePath.Parent(dir.Path) is { } parent && listings.TryGetValue(parent, out var map))
                    map[RemotePath.FileName(dir.Path)] = new RemoteItem(RemotePath.FileName(dir.Path), dir.Path, true, 0, DateTime.Now);
            }
            dir.Done = true;
        }

        foreach (var file in plan.Files.Where(f => !f.Done))
        {
            ct.ThrowIfCancellationRequested();
            var folder = RemotePath.Parent(file.Target) ?? "/";
            var name = RemotePath.FileName(file.Target);
            reporter.Start(name);
            var map = await Listing(folder).ConfigureAwait(false);
            if (!file.Resolved)
            {
                if (map.TryGetValue(name, out var existing))
                {
                    if (existing.IsDirectory) throw new FileOperationException(L.F("« {0} » existe déjà sur le serveur comme dossier.", name));
                    bool write = policy switch
                    {
                        ConflictPolicy.Skip => false,
                        ConflictPolicy.OverwriteOlder => file.Modified - existing.Modified > TimeSpan.FromSeconds(2),
                        _ => true,
                    };
                    if (policy == ConflictPolicy.RenameNew) name = UniqueRemoteName(name, map);
                    if (!write)
                    {
                        file.Done = true;
                        reporter.FileDone(file.Size);
                        plan.Broken.Add(file.Top);
                        continue;
                    }
                }
                file.Target = RemotePath.Combine(folder, name);
                file.Resolved = true;
            }
            await UploadFileAsync(fs, file, reporter, resume, pause, ct).ConfigureAwait(false);
            await fs.SetModifiedAsync(file.Target, file.Modified, ct).ConfigureAwait(false);
            map[name] = new RemoteItem(name, file.Target, false, file.Size, file.Modified);
            file.Done = true;
            reporter.FileDone();
        }

        if (deleteSource)
        {
            for (int i = 0; i < localPaths.Count; i++)
            {
                if (plan.Broken.Contains(i)) continue;
                if (Directory.Exists(localPaths[i])) Directory.Delete(localPaths[i], recursive: true);
                else if (File.Exists(localPaths[i])) File.Delete(localPaths[i]);
            }
        }
    }

    private static async Task<TransferPlan> PlanUploadAsync(IReadOnlyList<string> localPaths, string remoteDir, ConflictPolicy policy,
        Func<string, Task<Dictionary<string, RemoteItem>>> listing, CancellationToken ct)
    {
        var plan = new TransferPlan { IsUpload = true };
        var baseDir = RemotePath.Normalize(remoteDir);

        async Task Walk(string local, string parentRemote, string name, int top, bool parentIsNew)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(local))
            {
                var existing = parentIsNew ? null : (await listing(parentRemote).ConfigureAwait(false)).GetValueOrDefault(name);
                if (existing is { IsDirectory: false }) throw new FileOperationException(L.F("« {0} » existe déjà sur le serveur comme fichier.", name));
                if (existing is not null && top >= 0 && parentRemote == baseDir)
                {
                    if (policy == ConflictPolicy.Skip) { plan.Broken.Add(top); return; }
                    if (policy == ConflictPolicy.RenameNew)
                    {
                        name = UniqueRemoteName(name, await listing(parentRemote).ConfigureAwait(false));
                        existing = null;
                    }
                }
                var remote = RemotePath.Combine(parentRemote, name);
                plan.Dirs.Add(new TransferPlanDir { Path = remote, Exists = existing is not null });
                var info = new DirectoryInfo(local);
                foreach (var child in info.EnumerateFileSystemInfos())
                {
                    if ((child.Attributes & FileAttributes.ReparsePoint) != 0 && child is DirectoryInfo) continue; // jonctions : non suivies
                    await Walk(child.FullName, remote, child.Name, top, existing is null).ConfigureAwait(false);
                }
            }
            else
            {
                var fi = new FileInfo(local);
                if (!fi.Exists) throw new FileOperationException(L.F("Introuvable : {0}", local));
                plan.Files.Add(new TransferPlanFile { Source = local, Target = RemotePath.Combine(parentRemote, name), Size = fi.Length, Modified = fi.LastWriteTime, Top = top });
            }
        }

        for (int i = 0; i < localPaths.Count; i++)
        {
            var name = Path.GetFileName(localPaths[i].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            await Walk(localPaths[i], baseDir, name, i, parentIsNew: false).ConfigureAwait(false);
        }
        return plan;
    }

    private static async Task UploadFileAsync(IRemoteFileSystem fs, TransferPlanFile file, Reporter reporter, TransferResume? resume, PauseController? pause, CancellationToken ct)
    {
        var part = file.Target + PartSuffix;
        long before = reporter.BytesDone;
        for (bool fromZero = false; ; fromZero = true)
        {
            long offset = fromZero ? 0 : await UploadOffsetAsync(fs, file, part, resume, ct).ConfigureAwait(false);
            if (offset == 0 && file.Started) reporter.Start(L.F("{0} (repart de zéro)", RemotePath.FileName(file.Target)));
            if (offset == 0 && resume is not null && file.Size >= resume.MinBytes) file.Started = true;
            try
            {
                if (offset > 0 && offset == file.Size) reporter.Bytes(offset); // tout était arrivé : il ne manquait que le renommage
                else
                {
                    await using var input = new FileStream(file.Source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    await using var counted = new TransferStream(input, reporter.Bytes, ct, pause, leaveOpen: true);
                    if (offset > 0)
                    {
                        input.Position = offset;
                        reporter.Bytes(offset);
                        await fs.AppendAsync(counted, part, offset, ct).ConfigureAwait(false);
                    }
                    else await fs.UploadAsync(counted, part, ct).ConfigureAwait(false);
                }
                await fs.RenameAsync(part, file.Target, overwrite: true, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && offset > 0 && !fromZero && !ConnectionFailure.IsLost(ex, fs))
            {
                reporter.BytesDone = before; // reprise refusée par le serveur : on recommence ce fichier
            }
            catch (Exception ex) when (file.Started && ex is not OperationCanceledException && ConnectionFailure.IsLost(ex, fs))
            {
                throw; // coupure : le morceau reste sur le serveur pour la reprise
            }
            catch
            {
                try { await fs.DeleteFileAsync(part, CancellationToken.None).ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
                file.Started = false;
                throw;
            }
        }
    }

    /// <summary>
    /// Position de reprise ; 0 si le fichier du PC a changé, ou si la fin du morceau reçu par le serveur n'est pas identique à la sienne.
    /// </summary>
    private static async Task<long> UploadOffsetAsync(IRemoteFileSystem fs, TransferPlanFile file, string part, TransferResume? resume, CancellationToken ct)
    {
        if (resume is null || !file.Started || !fs.CanResumeUpload) return 0;
        var local = new FileInfo(file.Source);
        if (!local.Exists || local.Length != file.Size || local.LastWriteTime != file.Modified) return 0;
        if (await fs.StatAsync(part, ct).ConfigureAwait(false) is not { Size: > 0 } received || received.Size > file.Size) return 0;

        long count = Math.Min(received.Size, TransferResume.TailBytes);
        try
        {
            using var tail = new MemoryStream();
            await fs.DownloadFromAsync(part, tail, received.Size - count, ct).ConfigureAwait(false);
            return tail.ToArray().AsSpan().SequenceEqual(ReadLocal(file.Source, received.Size, count)) ? received.Size : 0;
        }
        catch (Exception ex) when (ex is RemoteException && !ConnectionFailure.IsLost(ex, fs)) { return 0; } // relecture refusée par le serveur
    }

    private static string UniqueRemoteName(string name, IReadOnlyDictionary<string, RemoteItem> existing)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        if (stem.Length == 0) { stem = name; ext = string.Empty; }
        for (int i = 2; ; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (!existing.ContainsKey(candidate)) return candidate;
        }
    }

    // ---- Sur le serveur ------------------------------------------------------------------------------------------

    /// <summary>Supprime définitivement (il n'y a pas de corbeille sur un serveur).</summary>
    public static async Task DeleteAsync(RemoteSession session, IReadOnlyList<RemoteItem> items, IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        await using var fs = await session.OpenWorkerAsync(ct).ConfigureAwait(false);
        var reporter = new Reporter(progress, 0, items.Count);
        foreach (var item in items)
        {
            reporter.Start(item.Name);
            await DeleteTreeAsync(fs, item, reporter, ct).ConfigureAwait(false);
            reporter.FileDone();
        }
    }

    private static async Task DeleteTreeAsync(IRemoteFileSystem fs, RemoteItem item, Reporter? reporter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (item.IsDirectory && !item.IsLink)
        {
            foreach (var child in await fs.ListAsync(item.FullPath, ct).ConfigureAwait(false))
            {
                reporter?.Start(child.Name);
                await DeleteTreeAsync(fs, child, reporter, ct).ConfigureAwait(false);
            }
            await fs.DeleteDirectoryAsync(item.FullPath, ct).ConfigureAwait(false);
        }
        else await fs.DeleteFileAsync(item.FullPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Déplacement sur le même serveur : simple renommage côté serveur (instantané). Si le serveur refuse le renommage
    /// (le plus souvent : destination sur un autre disque du serveur), « mv » exécuté par le serveur quand c'est possible,
    /// sinon copie puis suppression de l'original (en passant par le PC).
    /// </summary>
    public static async Task MoveOnServerAsync(RemoteSession session, IReadOnlyList<RemoteItem> items, string remoteDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null, int retries = 0, ServerCommandState? state = null)
    {
        var copyInstead = new List<RemoteItem>();
        state ??= new ServerCommandState();
        await new TransferResume { MaxRetries = retries }.RunAsync(session.OpenWorkerAsync, fs =>
        {
            copyInstead.Clear();
            return RenameIntoAsync(fs, items, remoteDir, policy, progress, copyInstead, ct, state: state);
        }, progress, ct).ConfigureAwait(false);
        if (copyInstead.Count > 0)
            await CopyBetweenAsync(session, copyInstead, session, remoteDir, policy, progress, ct, pause, deleteSource: true, retries: retries).ConfigureAwait(false);
    }

    internal static async Task RenameIntoAsync(IRemoteFileSystem fs, IReadOnlyList<RemoteItem> items, string remoteDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, List<RemoteItem> copyInstead, CancellationToken ct, TimeSpan? watchEvery = null, ServerCommandState? state = null)
    {
        var reporter = new Reporter(progress, items.Where(i => !i.IsDirectory).Sum(i => i.Size), items.Count);
        var existing = (await fs.ListAsync(remoteDir, ct).ConfigureAwait(false)).ToDictionary(i => i.Name, StringComparer.Ordinal);
        foreach (var item in items)
        {
            reporter.Start(item.Name);
            var name = item.Name;
            bool overwrite = false;
            var ours = state?.Started.FirstOrDefault(t => RemotePath.FileName(t) == item.Name && state.Started.Contains(t));
            if (state is not null && ours is not null)
            {
                // Coupure pendant « mv » : on regarde où il en est avant de faire quoi que ce soit.
                if (state.Finished.ContainsKey(ours) || await MoveFinishedAsync(fs, item, ours, state, ct).ConfigureAwait(false))
                {
                    state.Finished[ours] = FileBytes(item);
                    reporter.FileDone(FileBytes(item));
                    continue;
                }
                await fs.DeleteFileAsync(ours, ct).ConfigureAwait(false); // « mv » arrêté : l'original est intact, le morceau repart
                state.Started.Remove(ours);
                existing.Remove(name);
            }
            if (existing.TryGetValue(name, out var other))
            {
                if (policy == ConflictPolicy.Skip || (policy == ConflictPolicy.OverwriteOlder && item.Modified - other.Modified <= TimeSpan.FromSeconds(2)))
                { reporter.FileDone(FileBytes(item)); continue; }
                if (policy == ConflictPolicy.RenameNew) name = UniqueRemoteName(name, existing);
                else if (other.IsDirectory || item.IsDirectory) throw new FileOperationException(L.F("« {0} » existe déjà dans la destination (dossier) : déplacement impossible.", name));
                else overwrite = true;
            }
            var target = RemotePath.Combine(remoteDir, name);
            if (RemotePath.Normalize(target) == RemotePath.Normalize(item.FullPath)) { reporter.FileDone(FileBytes(item)); continue; }
            long moved = FileBytes(item);
            try
            {
                await fs.RenameAsync(item.FullPath, target, overwrite, ct).ConfigureAwait(false);
            }
            catch (RemoteException)
            {
                // Renommage refusé (autre disque du serveur…) : « mv » par le serveur, sinon copie + suppression.
                ServerMoveResult r;
                state?.Started.Add(target);
                if (fs is IServerSideMove m)
                    (r, moved) = await WatchServerCommandAsync(fs, m, item, target, reporter, () => m.TryMoveOnServerAsync(item.FullPath, target, ct), watchEvery, ct).ConfigureAwait(false);
                else r = ServerMoveResult.NotAvailable;
                if (r.Error is { } error) throw new RemoteException(L.F("Déplacement de {0} : {1}", item.Name, error));
                if (!r.Moved)
                {
                    state?.Started.Remove(target);
                    copyInstead.Add(item);
                    reporter.FileDone(moved);
                    continue;
                }
            }
            state?.Finished.TryAdd(target, moved);
            existing[name] = item with { Name = name, FullPath = target };
            reporter.FileDone(moved);
        }
    }

    private static long FileBytes(RemoteItem item) => item.IsDirectory ? 0 : item.Size;

    /// <summary>
    /// « mv » coupé : vrai s'il a fini (original parti, cible là), faux s'il s'est arrêté. Un dossier peut être à moitié retiré
    /// des deux côtés : on ne touche à rien et on demande de regarder, plutôt que de risquer une perte.
    /// </summary>
    private static async Task<bool> MoveFinishedAsync(IRemoteFileSystem fs, RemoteItem item, string target, ServerCommandState state, CancellationToken ct)
    {
        if (item.IsDirectory) throw new RemoteException(L.F("Déplacement de {0} interrompu par une coupure. Rien n'a été retiré : vérifie la source et la destination.", item.Name));
        var settled = Stopwatch.StartNew();
        long last = -1;
        while (true)
        {
            var source = await fs.StatAsync(item.FullPath, ct).ConfigureAwait(false);
            var now = await fs.StatAsync(target, ct).ConfigureAwait(false);
            if (source is null && now is not null) return true;
            if (source is null) throw new RemoteException(L.F("« {0} » est introuvable après la coupure, à l'origine comme à la destination.", item.Name));
            long size = now?.Size ?? 0;
            if (size != last) { last = size; settled.Restart(); }
            else if (settled.Elapsed >= state.Settle) return false;
            await Task.Delay(state.CheckEvery, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// « cp » coupé : attend que le serveur ait fini ou se soit arrêté. « cp -a » pose la date d'origine tout à la fin :
    /// même date (et même taille pour un fichier) veut dire terminé.
    /// </summary>
    private static async Task<bool> CopyFinishedAsync(IRemoteFileSystem fs, IServerSideMove server, RemoteItem item, string target, long size, long start,
        Reporter reporter, ServerCommandState state, CancellationToken ct)
    {
        reporter.Start(L.F("{0} (le serveur continue)", item.Name));
        var settled = Stopwatch.StartNew();
        long last = -1;
        while (true)
        {
            var source = await fs.StatAsync(item.FullPath, ct).ConfigureAwait(false)
                         ?? throw new RemoteException(L.F("« {0} » est introuvable après la coupure.", item.Name));
            if (await fs.StatAsync(target, ct).ConfigureAwait(false) is not { } now) return false;
            if ((now.Modified - source.Modified).Duration() <= TimeSpan.FromSeconds(2) && (item.IsDirectory || now.Size == source.Size)) return true;
            long done = item.IsDirectory ? await server.MeasureOnServerAsync(target, ct).ConfigureAwait(false) ?? 0 : now.Size;
            if (done != last)
            {
                last = done;
                settled.Restart();
                reporter.BytesDone = start + Math.Min(done, size);
                reporter.Report(force: true);
            }
            else if (settled.Elapsed >= state.Settle) return false;
            await Task.Delay(state.CheckEvery, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// « cp » et « mv » du serveur ne disent rien avant d'avoir fini : pendant qu'ils tournent, on mesure la cible, pour que la barre
    /// et la vitesse soient réelles. Mesure impossible : la commande se termine quand même, la barre avance simplement à la fin.
    /// Rend aussi la taille de l'élément, à compter une fois terminé.
    /// </summary>
    private static async Task<(ServerMoveResult Result, long Size)> WatchServerCommandAsync(IRemoteFileSystem fs, IServerSideMove server, RemoteItem item, string target,
        Reporter reporter, Func<Task<ServerMoveResult>> command, TimeSpan? every, CancellationToken ct)
    {
        long size = FileBytes(item);
        if (item.IsDirectory && await server.MeasureOnServerAsync(item.FullPath, ct).ConfigureAwait(false) is { } measured)
        {
            size = measured;
            reporter.AddTotal(measured);
        }
        long start = reporter.BytesDone;
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watching = size > 0
            ? WatchTargetAsync(fs, server, item.IsDirectory, target, start, size, reporter, every ?? TimeSpan.FromSeconds(item.IsDirectory ? 3 : 1), watch.Token)
            : Task.CompletedTask;
        try
        {
            return (await command().ConfigureAwait(false), size);
        }
        finally
        {
            await watch.CancelAsync().ConfigureAwait(false);
            await watching.ConfigureAwait(false);
            reporter.BytesDone = start;
        }
    }

    private static async Task WatchTargetAsync(IRemoteFileSystem fs, IServerSideMove server, bool isDirectory, string target, long start, long size,
        Reporter reporter, TimeSpan every, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(every, ct).ConfigureAwait(false);
                long? now = isDirectory
                    ? await server.MeasureOnServerAsync(target, ct).ConfigureAwait(false)
                    : (await fs.StatAsync(target, ct).ConfigureAwait(false))?.Size;
                if (now is not { } done) continue;
                reporter.BytesDone = start + Math.Min(done, size);
                reporter.Report(force: true);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or ObjectDisposedException) { /* simple mesure : la copie continue */ }
    }

    /// <summary>Première colonne de « du -sb » : la taille en octets.</summary>
    public static long? ParseDiskUsage(string? output)
    {
        var first = (output ?? string.Empty).Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return long.TryParse(first, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size) ? size : null;
    }

    /// <summary>Deux étapes (réception puis envoi) montrées comme une seule barre : la 1re remplit la moitié, la 2e le reste.</summary>
    private sealed class PhaseProgress(IProgress<TransferProgress> inner, bool second) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress p) => inner.Report(new TransferProgress(
            (second ? L.T("Envoi : ") : L.T("Réception : ")) + p.CurrentItem,
            (second ? p.BytesTotal : 0) + p.BytesDone, p.BytesTotal * 2,
            (second ? p.FilesTotal : 0) + p.FilesDone, p.FilesTotal * 2) { Notice = p.Notice });
    }

    private static IProgress<TransferProgress>? Phase(IProgress<TransferProgress>? inner, bool second) =>
        inner is null ? null : new PhaseProgress(inner, second);

    /// <summary>
    /// Copie sur le même serveur par le serveur lui-même (« cp -a »), pour les éléments sans conflit de nom.
    /// Retourne ce qui reste à copier en passant par le PC (conflits, ou serveur sans commandes).
    /// </summary>
    internal static async Task<IReadOnlyList<RemoteItem>> CopyOnServerAsync(IRemoteFileSystem fs, IReadOnlyList<RemoteItem> items, string remoteDir,
        IProgress<TransferProgress>? progress, CancellationToken ct, TimeSpan? watchEvery = null, ServerCommandState? state = null)
    {
        if (fs is not IServerSideMove server) return items;
        state ??= new ServerCommandState();
        var existing = (await fs.ListAsync(remoteDir, ct).ConfigureAwait(false)).Select(i => i.Name).ToHashSet(StringComparer.Ordinal);
        var rest = new List<RemoteItem>();
        var reporter = new Reporter(progress, items.Where(i => !i.IsDirectory).Sum(i => i.Size), items.Count);
        for (int k = 0; k < items.Count; k++)
        {
            var item = items[k];
            var target = RemotePath.Combine(remoteDir, item.Name);
            if (state.Finished.TryGetValue(target, out var finished))
            {
                if (item.IsDirectory) reporter.AddTotal(finished);
                reporter.FileDone(finished);
                continue;
            }
            bool ours = state.Started.Contains(target);
            // Nom déjà pris (ou copie sur lui-même) : la conduite choisie (écraser, ignorer, garder les deux) passe par le chemin habituel.
            if (!ours && (existing.Contains(item.Name) || RemotePath.Normalize(target) == RemotePath.Normalize(item.FullPath))) { rest.Add(item); continue; }
            reporter.Start(item.Name);
            if (ours)
            {
                // Après une coupure : le serveur a peut-être continué seul. On le laisse finir, sans rien repasser par le PC.
                long total = item.IsDirectory ? await server.MeasureOnServerAsync(item.FullPath, ct).ConfigureAwait(false) ?? 0 : item.Size;
                if (item.IsDirectory) reporter.AddTotal(total);
                long from = reporter.BytesDone;
                bool done;
                try { done = await CopyFinishedAsync(fs, server, item, target, total, from, reporter, state, ct).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    await RemoveTargetAsync(fs, item, target).ConfigureAwait(false);
                    throw;
                }
                reporter.BytesDone = from;
                if (done)
                {
                    state.Finished[target] = total;
                    reporter.FileDone(total);
                    continue;
                }
                // « cp » s'est arrêté avec la connexion : son morceau est retiré (la cible n'existait pas), puis le serveur recommence.
                if (item.IsDirectory) reporter.AddTotal(-total);
                await RemoveTargetAsync(fs, item, target).ConfigureAwait(false);
            }
            state.Started.Add(target);
            var (r, size) = await WatchServerCommandAsync(fs, server, item, target, reporter, () => server.TryCopyOnServerAsync(item.FullPath, target, ct), watchEvery, ct).ConfigureAwait(false);
            if (r.Error is { } error) throw new RemoteException(L.F("Copie de {0} : {1}", item.Name, error));
            if (!r.Moved)
            {
                // Pas de commandes sur ce serveur : tout le reste passe par le PC.
                state.Started.Remove(target);
                rest.AddRange(items.Skip(k));
                break;
            }
            state.Finished[target] = size;
            existing.Add(item.Name);
            reporter.FileDone(size);
        }
        return rest;
    }

    private static async Task RemoveTargetAsync(IRemoteFileSystem fs, RemoteItem item, string target)
    {
        try { await DeleteTreeAsync(fs, item with { FullPath = target }, null, CancellationToken.None).ConfigureAwait(false); }
        catch (IOException) { /* déjà retirée, ou serveur de nouveau injoignable */ }
    }

    /// <summary>
    /// D'un serveur à un autre (ou copie sur le même) : passage par un dossier temporaire local, avec UNE barre de
    /// progression pour les deux étapes (réception puis envoi). Copie sur le même serveur : faite par le serveur
    /// lui-même (« cp -a ») quand il accepte les commandes ; seuls les éléments en conflit passent par le PC.
    /// </summary>
    /// <param name="retries">Tentatives après une coupure, pour chacune des deux étapes.</param>
    /// <param name="state">Commandes déjà lancées sur le serveur, pour un transfert rétabli après un redémarrage.</param>
    public static async Task CopyBetweenAsync(RemoteSession from, IReadOnlyList<RemoteItem> items, RemoteSession to, string remoteDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause = null, bool deleteSource = false, int retries = 0, ServerCommandState? state = null)
    {
        if (ReferenceEquals(from, to) && !deleteSource)
        {
            // Coupure : on se reconnecte pour retrouver la commande du serveur ; on ne recommence jamais par le PC.
            state ??= new ServerCommandState();
            var chosen = items;
            await new TransferResume { MaxRetries = retries }.RunAsync(from.OpenWorkerAsync,
                async fs => items = await CopyOnServerAsync(fs, chosen, remoteDir, progress, ct, state: state).ConfigureAwait(false), progress, ct).ConfigureAwait(false);
            if (items.Count == 0) return;
        }
        // Dans le dossier temporaire, deux fichiers de même nom (vue à plat) n'en feraient qu'un, et l'autre serait perdu.
        foreach (var group in Search.FlatListing.SplitUniqueNames(items, i => i.Name))
            await CopyThroughPcAsync(from, group, to, remoteDir, policy, progress, ct, pause, deleteSource, retries).ConfigureAwait(false);
    }

    private static async Task CopyThroughPcAsync(RemoteSession from, IReadOnlyList<RemoteItem> items, RemoteSession to, string remoteDir, ConflictPolicy policy,
        IProgress<TransferProgress>? progress, CancellationToken ct, PauseController? pause, bool deleteSource, int retries)
    {
        var temp = Path.Combine(Path.GetTempPath(), "TwinPanel", "transfert-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        try
        {
            await DownloadAsync(from, items, temp, ConflictPolicy.Overwrite, Phase(progress, second: false), ct, pause, resume: new TransferResume { MaxRetries = retries }).ConfigureAwait(false);
            var locals = items.Select(i => Path.Combine(temp, i.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            await UploadAsync(to, locals, remoteDir, policy, Phase(progress, second: true), ct, pause, resume: new TransferResume { MaxRetries = retries }).ConfigureAwait(false);
            if (deleteSource)
            {
                await using var src = await from.OpenWorkerAsync(ct).ConfigureAwait(false);
                foreach (var item in items) await DeleteTreeAsync(src, item, null, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Télécharge un seul fichier dans un dossier temporaire (F3, F4, Entrée). Retourne le chemin local.</summary>
    public static async Task<string> DownloadToTempAsync(RemoteSession session, RemoteItem item, CancellationToken ct)
    {
        if (!RemotePath.IsValidWindowsName(item.Name)) throw new RemoteException(L.F("« {0} » n'est pas un nom de fichier possible sous Windows.", item.Name));
        var dir = Path.Combine(Path.GetTempPath(), "TwinPanel", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, item.Name);
        var part = target + PartSuffix;
        try
        {
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                await session.UseAsync(fs =>
                {
                    // En cas de reconnexion automatique, on repart d'un fichier vide.
                    output.SetLength(0);
                    output.Position = 0;
                    return fs.DownloadAsync(item.FullPath, output, ct);
                }, ct).ConfigureAwait(false);
            File.Move(part, target, overwrite: true);
            try { File.SetLastWriteTime(target, item.Modified); } catch (IOException) { }
            return target;
        }
        catch
        {
            TryDeleteLocal(part);
            throw;
        }
    }

    /// <summary>Renvoie un fichier modifié localement à sa place sur le serveur (édition avec F4).</summary>
    public static async Task UploadReplaceAsync(RemoteSession session, string localFile, string remotePath, CancellationToken ct)
    {
        await using var fs = await session.OpenWorkerAsync(ct).ConfigureAwait(false);
        var part = remotePath + PartSuffix;
        await using (var input = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true))
            await fs.UploadAsync(input, part, ct).ConfigureAwait(false);
        await fs.RenameAsync(part, remotePath, overwrite: true, ct).ConfigureAwait(false);
    }

    private static void TryDeleteLocal(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void ThrowIfSkipped(List<string> skipped)
    {
        if (skipped.Count == 0) return;
        var list = string.Join(Environment.NewLine, skipped.Take(10));
        if (skipped.Count > 10) list += L.F("{0}… et {1} autre(s)", Environment.NewLine, skipped.Count - 10);
        throw new FileOperationException(L.F("Terminé, mais {0} élément(s) n'ont pas été copiés :{1}{2}", skipped.Count, Environment.NewLine, list));
    }
}
