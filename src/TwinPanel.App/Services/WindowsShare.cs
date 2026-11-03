// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TwinPanel.App.Services;

/// <summary>
/// Fenêtre « Partager » de Windows (partage à proximité, courrier, applications…), celle du clic droit de l'Explorateur.
/// On demande au menu contextuel de Windows d'exécuter son action « Windows.ModernShare » sur les fichiers.
/// Les fichiers doivent être dans un même dossier : c'est à l'appelant de les y réunir.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsShare
{
    private const string ShareVerb = "Windows.ModernShare";
    private const uint CmfNormal = 0;
    private const int CmicMaskUnicode = 0x4000;
    private const int SwShowNormal = 1;
    private const uint FirstCommand = 1;
    private const uint GcsVerbW = 4;

    /// <summary>
    /// Le menu de la dernière demande reste en vie : la fenêtre de partage s'ouvre après le retour de l'appel,
    /// et Windows l'abandonne si on libère le menu trop tôt.
    /// </summary>
    private static IContextMenu? _lastMenu;
    private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");

    /// <summary>Fenêtre principale de TwinPanel : la fenêtre de partage s'ouvre au-dessus d'elle.</summary>
    public static IntPtr MainWindowHandle() =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero
            : IntPtr.Zero;

    /// <summary>
    /// Ouvre la fenêtre de partage. Rend null si c'est fait, sinon la raison (code de Windows compris).
    /// L'action est lancée par sa place dans le menu préparé par Windows : par son nom, Windows la refuse
    /// (« aucune association », 0x80070483).
    /// </summary>
    public static string? Show(IReadOnlyList<string> files, IntPtr owner)
    {
        if (files.Count == 0) return "0 fichier";
        var absolute = new List<IntPtr>();
        IntPtr menu = IntPtr.Zero;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;
        try
        {
            foreach (var file in files)
            {
                int parsed = SHParseDisplayName(file, IntPtr.Zero, out var pidl, 0, out _);
                if (parsed != 0 || pidl == IntPtr.Zero) return $"SHParseDisplayName 0x{parsed:X8}";
                absolute.Add(pidl);
            }
            var iidFolder = IidShellFolder;
            int bound = SHBindToParent(absolute[0], ref iidFolder, out var folderPtr, out _);
            if (bound != 0) return $"SHBindToParent 0x{bound:X8}";
            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            Marshal.Release(folderPtr);
            // Les identifiants « enfants » : la dernière partie de chaque chemin, relative au dossier commun.
            var children = absolute.Select(ILFindLastID).ToArray();
            var iidMenu = IidContextMenu;
            int got = folder.GetUIObjectOf(owner, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out var menuPtr);
            if (got != 0) return $"GetUIObjectOf 0x{got:X8}";
            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);
            Marshal.Release(menuPtr);
            // Le menu doit être préparé : les extensions de Windows n'ajoutent leurs actions qu'à ce moment.
            menu = CreatePopupMenu();
            contextMenu.QueryContextMenu(menu, 0, FirstCommand, 0x7FFF, CmfNormal);
            if (FindVerb(contextMenu, menu, ShareVerb) is not { } id) return L.T("Windows ne propose pas « Partager » pour ces fichiers.");
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CmicMaskUnicode,
                hwnd = owner,
                lpVerb = (IntPtr)(id - FirstCommand),
                lpVerbW = (IntPtr)(id - FirstCommand),
                nShow = SwShowNormal,
            };
            int invoked = contextMenu.InvokeCommand(ref info);
            if (invoked != 0) return $"InvokeCommand 0x{invoked:X8}";
            if (_lastMenu is not null) Marshal.ReleaseComObject(_lastMenu);
            _lastMenu = contextMenu;
            contextMenu = null;
            return null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return ex.Message;
        }
        finally
        {
            if (menu != IntPtr.Zero) DestroyMenu(menu);
            if (contextMenu is not null) Marshal.ReleaseComObject(contextMenu);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            foreach (var pidl in absolute) Marshal.FreeCoTaskMem(pidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr folder, out IntPtr pidlLast);

    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);

    /// <summary>Cherche l'action dans le menu (et ses sous-menus) par son nom invariable ; rend son numéro.</summary>
    private static uint? FindVerb(IContextMenu contextMenu, IntPtr menu, string verb)
    {
        int count = GetMenuItemCount(menu);
        var buffer = Marshal.AllocHGlobal(1024);
        try
        {
            for (int i = 0; i < count; i++)
            {
                var sub = GetSubMenu(menu, i);
                if (sub != IntPtr.Zero)
                {
                    if (FindVerb(contextMenu, sub, verb) is { } found) return found;
                    continue;
                }
                uint id = GetMenuItemID(menu, i);
                if (id == uint.MaxValue || id < FirstCommand) continue;
                Marshal.WriteInt16(buffer, 0, 0);
                if (contextMenu.GetCommandString((UIntPtr)(id - FirstCommand), GcsVerbW, IntPtr.Zero, buffer, 512) == 0
                    && string.Equals(Marshal.PtrToStringUni(buffer), verb, StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(IntPtr menu, int position);

    [DllImport("user32.dll")]
    private static extern IntPtr GetSubMenu(IntPtr menu, int position);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    // Seule GetUIObjectOf sert ; les autres méthodes sont là pour garder l'ordre de la table de l'interface.
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumerator);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr result);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, ref Guid riid, IntPtr reserved, out IntPtr result);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint max);
    }
}
