// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using TwinPanel.Core;
using TwinPanel.Core.Media;

namespace TwinPanel.App.Services;

/// <summary>
/// Vignettes de la vue Miniatures, lues hors de l'interface : seulement ce qui est à l'écran, les plus anciennes
/// demandes d'une vue étant remplacées par les nouvelles quand on fait défiler.
/// PC : Windows les fournit (images et vidéos, avec son propre cache) ; ailleurs, images décodées par TwinPanel.
/// Serveur : images téléchargées une à une sur une connexion à part. Les vignettes restent en mémoire, rien sur le disque.
/// </summary>
public static class ThumbnailLoader
{
    /// <summary>Au plus 256 Mo d'images en mémoire : les moins récemment vues sont retirées.</summary>
    private const long MaxCacheBytes = 256L * 1024 * 1024;
    /// <summary>Sans demande depuis ce temps, la connexion réservée aux vignettes est refermée.</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(20);

    public sealed record Job(string Key, FileEntry Entry, int Pixels, ThumbnailSourceKind Kind);

    private static readonly ThumbnailCache<Bitmap> Cache = new(MaxCacheBytes);
    private static readonly HashSet<string> Failed = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Running = new(StringComparer.Ordinal);
    private static readonly Dictionary<object, List<Job>> Pending = [];
    private static readonly AutoResetEvent LocalWake = new(false), RemoteWake = new(false);
    private static Thread? _localWorker, _remoteWorker;

    /// <summary>Lit un fichier d'un serveur (branché par la fenêtre principale). Null : connexion fermée.</summary>
    public static Func<FileEntry, CancellationToken, Task<byte[]?>>? RemoteReader { get; set; }

    /// <summary>Plus de demande depuis un moment : la connexion des vignettes peut être refermée.</summary>
    public static Func<Task>? RemoteIdle { get; set; }

    /// <summary>Une vignette vient d'arriver (sur le fil de l'interface).</summary>
    public static event EventHandler? ThumbnailReady;

    /// <summary>Vignette déjà en mémoire (fil de l'interface).</summary>
    public static Bitmap? Peek(string key) => Cache.Get(key);

    /// <summary>Remplace les demandes de cette vue par ce qu'elle montre maintenant (fil de l'interface).</summary>
    public static void Request(object owner, IReadOnlyList<Job> jobs)
    {
        List<Job> todo;
        lock (Pending)
        {
            todo = jobs.Where(j => !Failed.Contains(j.Key) && !Running.Contains(j.Key)).ToList();
            Pending[owner] = todo;
        }
        if (todo.Exists(j => j.Kind == ThumbnailSourceKind.Local)) { EnsureWorker(ref _localWorker, LocalWork, "TwinPanel vignettes"); LocalWake.Set(); }
        if (todo.Exists(j => j.Kind == ThumbnailSourceKind.Remote)) { EnsureWorker(ref _remoteWorker, RemoteWork, "TwinPanel vignettes serveur"); RemoteWake.Set(); }
    }

    private static void EnsureWorker(ref Thread? worker, ThreadStart work, string name)
    {
        if (worker is not null) return;
        var thread = new Thread(work) { IsBackground = true, Name = name };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA); // les extensions du Shell le veulent
        thread.Start();
        worker = thread;
    }

    private static Job? Take(ThumbnailSourceKind kind)
    {
        lock (Pending)
        {
            foreach (var list in Pending.Values)
            {
                int i = list.FindIndex(j => j.Kind == kind);
                if (i < 0) continue;
                var job = list[i];
                list.RemoveAt(i);
                Running.Add(job.Key);
                return job;
            }
        }
        return null;
    }

    private static void LocalWork()
    {
        while (true)
        {
            if (Take(ThumbnailSourceKind.Local) is { } job) Finish(job, LoadLocal(job.Entry.FullPath, job.Pixels), failed: true);
            else LocalWake.WaitOne();
        }
    }

    private static void RemoteWork()
    {
        bool connected = false;
        while (true)
        {
            if (Take(ThumbnailSourceKind.Remote) is { } job)
            {
                connected = true;
                try
                {
                    var bytes = RemoteReader?.Invoke(job.Entry, CancellationToken.None).GetAwaiter().GetResult();
                    // Pas de données (connexion fermée) : on pourra réessayer ; données illisibles : icône pour de bon.
                    Finish(job, bytes is null ? null : Decode(new MemoryStream(bytes), job.Pixels), failed: bytes is not null);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Finish(job, null, failed: false); // SSH, FTP, WebDAV : chacun a ses erreurs ; l'icône reste
                }
            }
            else if (!RemoteWake.WaitOne(IdleDelay) && connected)
            {
                connected = false;
                try { RemoteIdle?.Invoke().GetAwaiter().GetResult(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { /* connexion déjà perdue */ }
            }
        }
    }

    private static void Finish(Job job, Bitmap? bitmap, bool failed) => Dispatcher.UIThread.Post(() =>
    {
        lock (Pending)
        {
            Running.Remove(job.Key);
            if (bitmap is null && failed) Failed.Add(job.Key);
        }
        if (bitmap is null) return;
        var size = bitmap.PixelSize;
        foreach (var old in Cache.Add(job.Key, bitmap, size.Width * size.Height * 4L)) old.Dispose();
        ThumbnailReady?.Invoke(null, EventArgs.Empty);
    });

    private static Bitmap? LoadLocal(string path, int pixels)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return LoadFromShell(Path.GetFullPath(path), pixels); // Windows refuse les « / » ici
            using var file = File.OpenRead(path);
            return Decode(file, pixels);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; } // format inconnu, fichier disparu… : l'icône reste
    }

    private static Bitmap? Decode(Stream data, int pixels)
    {
        try { return Bitmap.DecodeToWidth(data, pixels, BitmapInterpolationMode.MediumQuality); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    // ---- Windows ---------------------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static Bitmap? LoadFromShell(string path, int pixels)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        try
        {
            if (factory.GetImage(new NativeSize { Width = pixels, Height = pixels }, SIIGBF_THUMBNAILONLY, out var hBitmap) != 0 || hBitmap == IntPtr.Zero) return null;
            try { return ToBitmap(hBitmap); }
            finally { ShellIcons.DeleteBitmap(hBitmap); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    [SupportedOSPlatform("windows")]
    private static Bitmap? ToBitmap(IntPtr hBitmap)
    {
        if (!ShellIcons.BitmapSize(hBitmap, out int w, out int h) || w <= 0 || h <= 0) return null;
        var pixels = ShellIcons.ReadBits(hBitmap, w, h);
        if (pixels is null) return null;
        // Windows rend les photos sans canal alpha (tout à zéro) : elles seraient invisibles.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), new PixelSize(w, h), new Vector(96, 96), w * 4);
        }
        finally { handle.Free(); }
    }

    private const int SIIGBF_THUMBNAILONLY = 0x08; // pas d'icône à la place : TwinPanel garde la sienne

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);
}
