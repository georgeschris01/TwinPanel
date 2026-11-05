// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;
using TwinPanel.Core.Selection;

namespace TwinPanel.Core.Compare;

public enum CompareState { Equal, LeftOnly, RightOnly, LeftNewer, RightNewer, Different }

public enum SyncAction { None, CopyToRight, CopyToLeft }

/// <summary>Sens de la synchronisation (actions proposées par défaut ; chaque ligne reste modifiable).</summary>
public enum SyncDirection { Both, ToRight, ToLeft }

public sealed record CompareOptions
{
    public bool Recursive { get; init; } = true;
    /// <summary>Compare aussi le contenu (empreinte) des fichiers de même taille.</summary>
    public bool CompareContent { get; init; }
    /// <summary>Ne regarde pas les dates (taille et éventuellement contenu seulement).</summary>
    public bool IgnoreDate { get; init; }
    /// <summary>Écart de date toléré (les clés USB en FAT arrondissent à 2 secondes).</summary>
    public TimeSpan DateTolerance { get; init; } = TimeSpan.FromSeconds(2);
    public string NameMask { get; init; } = "*";
    /// <summary>Faux : synchronisation à sens unique (gauche → droite).</summary>
    public bool Bidirectional { get; init; } = true;
}

/// <summary>Une ligne de comparaison : chemin relatif + les deux côtés.</summary>
public sealed record CompareItem(string RelativePath, bool IsDirectory, FileEntry? Left, FileEntry? Right, CompareState State)
{
    public SyncAction DefaultAction(bool bidirectional) => DefaultAction(bidirectional ? SyncDirection.Both : SyncDirection.ToRight);

    /// <summary>
    /// Deux sens : le plus récent (ou le seul présent) est copié de l'autre côté.
    /// Un seul sens : seuls les éléments absents ou plus récents du côté source sont copiés vers l'autre.
    /// « Différent » (même date, taille différente) : rien par défaut, c'est à l'utilisateur de choisir.
    /// </summary>
    public SyncAction DefaultAction(SyncDirection direction) => (State, direction) switch
    {
        (CompareState.LeftOnly or CompareState.LeftNewer, SyncDirection.Both or SyncDirection.ToRight) => SyncAction.CopyToRight,
        (CompareState.RightOnly or CompareState.RightNewer, SyncDirection.Both or SyncDirection.ToLeft) => SyncAction.CopyToLeft,
        _ => SyncAction.None,
    };
}

/// <summary>Comparaison de deux arborescences (base de la synchronisation de dossiers), sur le PC ou un serveur.</summary>
public static class FolderComparer
{
    private static readonly StringComparer Names =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static async Task<IReadOnlyList<CompareItem>> CompareAsync(string left, string right, CompareOptions o,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        await using var l = new LocalSyncSide(left);
        await using var r = new LocalSyncSide(right);
        return await Task.Run(() => CompareAsync(l, r, o, progress, ct), ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<CompareItem>> CompareAsync(SyncSide left, SyncSide right, CompareOptions o,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!await left.DirectoryExistsAsync(left.Root, ct).ConfigureAwait(false)) throw new DirectoryNotFoundException(L.F("Dossier introuvable : {0}", left.Root));
        if (!await right.DirectoryExistsAsync(right.Root, ct).ConfigureAwait(false)) throw new DirectoryNotFoundException(L.F("Dossier introuvable : {0}", right.Root));
        var result = new List<CompareItem>();
        await WalkAsync(left, right, left.Root, right.Root, string.Empty, o, new MaskMatcher(o.NameMask), result, progress, ct).ConfigureAwait(false);
        return result;
    }

    private static async Task WalkAsync(SyncSide ls, SyncSide rs, string? leftDir, string? rightDir, string rel, CompareOptions o, MaskMatcher mask,
        List<CompareItem> result, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Report(rel.Length == 0 ? "." : rel);
        var l = await ListAsync(ls, leftDir, ct).ConfigureAwait(false);
        var r = await ListAsync(rs, rightDir, ct).ConfigureAwait(false);

        foreach (var name in l.Keys.Union(r.Keys, Names).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
        {
            l.TryGetValue(name, out var le);
            r.TryGetValue(name, out var re);
            var relPath = rel.Length == 0 ? name : Path.Combine(rel, name);
            bool leftIsDir = le?.IsDirectory ?? false, rightIsDir = re?.IsDirectory ?? false;

            if (leftIsDir || rightIsDir)
            {
                // Un dossier d'un côté et un fichier de l'autre : on signale « différent » sans descendre.
                if (le is not null && re is not null && leftIsDir != rightIsDir)
                {
                    result.Add(new CompareItem(relPath, true, le, re, CompareState.Different));
                    continue;
                }
                if (!o.Recursive) continue;
                if (le is null || re is null)
                    result.Add(new CompareItem(relPath, true, le, re, le is null ? CompareState.RightOnly : CompareState.LeftOnly));
                await WalkAsync(ls, rs, le?.FullPath, re?.FullPath, relPath, o, mask, result, progress, ct).ConfigureAwait(false);
                continue;
            }

            if (!mask.IsMatch(name)) continue;
            var state = le is null ? CompareState.RightOnly
                : re is null ? CompareState.LeftOnly
                : await CompareFilesAsync(ls, rs, le, re, o, ct).ConfigureAwait(false);
            result.Add(new CompareItem(relPath, false, le, re, state));
        }
    }

    private static async Task<CompareState> CompareFilesAsync(SyncSide ls, SyncSide rs, FileEntry a, FileEntry b, CompareOptions o, CancellationToken ct)
    {
        bool sameSize = a.Size == b.Size;
        var diff = a.LastWriteTime - b.LastWriteTime;
        bool sameDate = o.IgnoreDate || diff.Duration() <= o.DateTolerance;

        if (sameSize && sameDate)
        {
            if (!o.CompareContent) return CompareState.Equal;
            bool same = !ls.IsRemote && !rs.IsRemote
                ? SameContent(a.FullPath, b.FullPath, ct)
                : string.Equals(await ls.HashAsync(a.FullPath, ct).ConfigureAwait(false), await rs.HashAsync(b.FullPath, ct).ConfigureAwait(false), StringComparison.Ordinal);
            return same ? CompareState.Equal : CompareState.Different;
        }
        if (o.IgnoreDate || diff.Duration() <= o.DateTolerance)
            return CompareState.Different; // même date mais taille différente : on ne sait pas lequel garder
        return diff > TimeSpan.Zero ? CompareState.LeftNewer : CompareState.RightNewer;
    }

    /// <summary>Comparaison octet par octet (s'arrête à la première différence).</summary>
    public static bool SameContent(string a, string b, CancellationToken ct = default)
    {
        using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
        if (fa.Length != fb.Length) return false;
        var ba = new byte[1024 * 1024];
        var bb = new byte[1024 * 1024];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int na = fa.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
            int nb = fb.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
            if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
            if (na == 0) return true;
        }
    }

    private static async Task<Dictionary<string, FileEntry>> ListAsync(SyncSide side, string? dir, CancellationToken ct)
    {
        var d = new Dictionary<string, FileEntry>(Names);
        if (dir is null) return d;
        try
        {
            foreach (var e in await side.ListAsync(dir, ct).ConfigureAwait(false)) d[e.Name] = e;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dossier illisible : vide */ }
        return d;
    }
}

/// <summary>Applique les actions choisies dans la fenêtre de synchronisation.</summary>
public static class FolderSync
{
    public static async Task ExecuteAsync(string left, string right, IReadOnlyList<(CompareItem Item, SyncAction Action)> plan,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null)
    {
        var copies = new List<(string Source, string Destination)>();
        var folders = new List<string>();
        foreach (var (item, action) in plan)
        {
            if (action == SyncAction.None) continue;
            var (from, to) = action == SyncAction.CopyToRight ? (left, right) : (right, left);
            var src = Path.Combine(from, item.RelativePath);
            var dst = Path.Combine(to, item.RelativePath);
            if (item.IsDirectory) folders.Add(dst);
            else copies.Add((src, dst));
        }
        foreach (var f in folders) Directory.CreateDirectory(f);
        await FileOperations.CopyFilesAsync(copies, progress, ct, pause).ConfigureAwait(false);
    }

    /// <summary>
    /// Même chose entre deux côtés quelconques (PC, serveur). Fichiers écrits par « .twinpanel-part » puis remplacement,
    /// date d'origine conservée. Serveur → serveur : par un fichier temporaire du PC.
    /// </summary>
    public static async Task ExecuteAsync(SyncSide left, SyncSide right, IReadOnlyList<(CompareItem Item, SyncAction Action)> plan,
        IProgress<TransferProgress>? progress = null, CancellationToken ct = default, PauseController? pause = null)
    {
        if (!left.IsRemote && !right.IsRemote)
        {
            await ExecuteAsync(left.Root, right.Root, plan, progress, ct, pause).ConfigureAwait(false);
            return;
        }
        var files = plan.Where(p => p.Action != SyncAction.None && !p.Item.IsDirectory).ToList();
        long total = files.Sum(p => (p.Action == SyncAction.CopyToRight ? p.Item.Left : p.Item.Right)?.Size ?? 0);
        long done = 0;
        int count = 0;

        foreach (var (item, action) in plan.Where(p => p.Action != SyncAction.None && p.Item.IsDirectory))
        {
            var to = action == SyncAction.CopyToRight ? right : left;
            await to.CreateDirectoriesAsync(to.PathOf(item.RelativePath), ct).ConfigureAwait(false);
        }

        foreach (var (item, action) in files)
        {
            ct.ThrowIfCancellationRequested();
            var (from, to, entry) = action == SyncAction.CopyToRight ? (left, right, item.Left) : (right, left, item.Right);
            if (entry is null) continue;
            var src = from.PathOf(item.RelativePath);
            var dst = to.PathOf(item.RelativePath);
            long before = done;
            void Bytes(long n)
            {
                done += n;
                progress?.Report(new TransferProgress(item.RelativePath, Math.Min(done, total), total, count, files.Count));
            }
            progress?.Report(new TransferProgress(item.RelativePath, done, total, count, files.Count));

            if (!from.IsRemote)
            {
                await using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
                await using var counted = new Remote.TransferStream(input, Bytes, ct, pause, leaveOpen: true);
                await to.WriteFromAsync(dst, counted, entry.LastWriteTime, ct).ConfigureAwait(false);
            }
            else
            {
                // Depuis un serveur : copie temporaire sur le PC, puis écriture de l'autre côté.
                var temp = Path.Combine(Path.GetTempPath(), "TwinPanel", "sync-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(temp);
                var tempFile = Path.Combine(temp, "fichier");
                try
                {
                    await using (var output = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                    await using (var counted = new Remote.TransferStream(output, Bytes, ct, pause, leaveOpen: true))
                        await from.ReadIntoAsync(src, counted, ct).ConfigureAwait(false);
                    await using var input = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    await to.WriteFromAsync(dst, input, entry.LastWriteTime, ct).ConfigureAwait(false);
                }
                finally
                {
                    try { Directory.Delete(temp, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            done = before + entry.Size;
            count++;
        }
        progress?.Report(new TransferProgress(string.Empty, total, total, files.Count, files.Count));
    }
}
