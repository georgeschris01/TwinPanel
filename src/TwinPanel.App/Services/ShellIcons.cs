// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TwinPanel.App.Services;

/// <summary>
/// Vraies icônes de Windows (celles de l'Explorateur), lues par Windows lui-même (SHGetFileInfo) sur un fil à part
/// (les extensions du Shell veulent un fil « STA »), converties en images et gardées en mémoire.
/// Par type de fichier (rapide, sans lire le disque) ; .exe, raccourcis, .ico… : l'icône du fichier lui-même.
/// Sous Linux / macOS : rien (les icônes dessinées de TwinPanel restent affichées).
/// </summary>
public static class ShellIcons
{
    /// <summary>Extensions dont chaque fichier a sa propre icône.</summary>
    private static readonly HashSet<string> PerFile = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".lnk", ".ico", ".cur", ".ani", ".url", ".scr", ".cpl", ".appref-ms", ".msc" };

    private const int MaxPerFileIcons = 3000;

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Action<Bitmap?>>> Pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<(string Key, string Path, bool IsDirectory, bool ByType, bool Large)> Jobs = new();
    private static Thread? _worker;
    private static int _perFileCount;

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Clé de cache et façon de lire l'icône d'un élément (local : fichier réel pour .exe / .lnk / .ico…).</summary>
    public static (string Key, string Path, bool ByType) Describe(string fullPath, string name, bool isDirectory, bool isLocal)
    {
        if (isDirectory) return ("<dossier>", "dossier", true);
        var ext = Path.GetExtension(name);
        if (isLocal && PerFile.Contains(ext)) return (fullPath, fullPath, false);
        return (ext.Length == 0 ? "<sans>" : ext, ext.Length == 0 ? "fichier" : "fichier" + ext, true);
    }

    public static bool TryGetCached(string key, bool large, out Bitmap? icon) => Cache.TryGetValue(Size(key, large), out icon);

    /// <summary>Demande une icône ; <paramref name="done"/> est appelé sur le fil de l'interface (null : pas d'icône).</summary>
    public static void Request(string key, string path, bool isDirectory, bool byType, bool large, Action<Bitmap?> done)
    {
        if (!IsSupported) { done(null); return; }
        var k = Size(key, large);
        if (Cache.TryGetValue(k, out var known)) { done(known); return; }
        lock (Pending)
        {
            if (Pending.TryGetValue(k, out var waiting)) { waiting.Add(done); return; }
            Pending[k] = [done];
        }
        EnsureWorker();
        Jobs.Add((k, path, isDirectory, byType, large));
    }

    /// <summary>Icône d'un programme (liste « Parcourir… »), en grand.</summary>
    public static void RequestProgram(string exePath, Action<Bitmap?> done) => Request(exePath, exePath, false, false, true, done);

    private static string Size(string key, bool large) => (large ? "L|" : "S|") + key;

    private static void EnsureWorker()
    {
        if (_worker is not null) return;
        lock (Jobs)
        {
            if (_worker is not null) return;
            var t = new Thread(Work) { IsBackground = true, Name = "TwinPanel icônes" };
            if (OperatingSystem.IsWindows()) t.SetApartmentState(ApartmentState.STA);
            t.Start();
            _worker = t;
        }
    }

    private static void Work()
    {
        foreach (var job in Jobs.GetConsumingEnumerable())
        {
            Bitmap? icon = null;
            try
            {
                if (OperatingSystem.IsWindows()) icon = Load(job.Path, job.IsDirectory, job.ByType, job.Large);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { icon = null; }

            if (!job.ByType)
            {
                // Une icône par fichier : on limite la mémoire utilisée (on recommence à zéro au-delà).
                if (Interlocked.Increment(ref _perFileCount) > MaxPerFileIcons)
                {
                    foreach (var k in Cache.Keys.Where(k => k.Contains('\\') || k.Contains('/')).ToList()) Cache.TryRemove(k, out _);
                    Interlocked.Exchange(ref _perFileCount, 0);
                }
            }
            Cache[job.Key] = icon;
            List<Action<Bitmap?>>? callbacks;
            lock (Pending)
            {
                Pending.Remove(job.Key, out callbacks);
            }
            if (callbacks is not null)
                Dispatcher.UIThread.Post(() => { foreach (var c in callbacks) c(icon); });
        }
    }

    // ---- Windows -------------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static Bitmap? Load(string path, bool isDirectory, bool byType, bool large)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        uint attributes = 0;
        if (byType)
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        }
        var r = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try { return ToBitmap(info.hIcon); }
        finally { DestroyIcon(info.hIcon); }
    }

    /// <summary>HICON → image BGRA (avec transparence ; masque utilisé pour les anciennes icônes sans canal alpha).</summary>
    [SupportedOSPlatform("windows")]
    private static Bitmap? ToBitmap(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var ii)) return null;
        try
        {
            if (ii.hbmColor == IntPtr.Zero) return null; // icône monochrome : rare, on garde l'icône dessinée
            var bmp = new BITMAP();
            if (GetObject(ii.hbmColor, Marshal.SizeOf<BITMAP>(), ref bmp) == 0) return null;
            int w = bmp.bmWidth, h = bmp.bmHeight;
            if (w <= 0 || h <= 0 || w > 256 || h > 256) return null;

            var color = ReadBits(ii.hbmColor, w, h);
            if (color is null) return null;
            bool hasAlpha = false;
            for (int i = 3; i < color.Length; i += 4) if (color[i] != 0) { hasAlpha = true; break; }
            if (!hasAlpha)
            {
                // Pas de canal alpha : le masque dit ce qui est transparent (blanc = transparent).
                var mask = ii.hbmMask != IntPtr.Zero ? ReadBits(ii.hbmMask, w, h) : null;
                for (int p = 0; p < w * h; p++)
                    color[p * 4 + 3] = mask is not null && mask[p * 4] != 0 ? (byte)0 : (byte)255;
            }

            var handle = GCHandle.Alloc(color, GCHandleType.Pinned);
            try
            {
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                    new PixelSize(w, h), new Vector(96, 96), w * 4);
            }
            finally { handle.Free(); }
        }
        finally
        {
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
        }
    }

    /// <summary>Largeur et hauteur d'une image Windows (vignettes).</summary>
    [SupportedOSPlatform("windows")]
    internal static bool BitmapSize(IntPtr hBitmap, out int width, out int height)
    {
        var bmp = new BITMAP();
        bool ok = GetObject(hBitmap, Marshal.SizeOf<BITMAP>(), ref bmp) != 0;
        width = bmp.bmWidth;
        height = Math.Abs(bmp.bmHeight);
        return ok;
    }

    [SupportedOSPlatform("windows")]
    internal static void DeleteBitmap(IntPtr hBitmap) => DeleteObject(hBitmap);

    [SupportedOSPlatform("windows")]
    internal static byte[]? ReadBits(IntPtr hBitmap, int w, int h)
    {
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // de haut en bas
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };
        var buffer = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            return GetDIBits(dc, hBitmap, 0, (uint)h, buffer, ref bi, 0) == 0 ? null : buffer;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    private const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr h, int c, ref BITMAP pv);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);
}
