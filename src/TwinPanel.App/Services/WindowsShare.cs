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
    private const int CmicMaskFlagNoUi = 0x400;
    private const int SwShowNormal = 1;
    private static readonly Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IidContextMenu = new("000214E4-0000-0000-C000-000000000046");

    /// <summary>Fenêtre principale de TwinPanel : la fenêtre de partage s'ouvre au-dessus d'elle.</summary>
    public static IntPtr MainWindowHandle() =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero
            : IntPtr.Zero;

    /// <summary>Ouvre la fenêtre de partage ; faux si Windows ne la propose pas pour ces fichiers.</summary>
    public static bool Show(IReadOnlyList<string> files, IntPtr owner)
    {
        if (files.Count == 0) return false;
        var absolute = new List<IntPtr>();
        IntPtr menu = IntPtr.Zero;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;
        try
        {
            foreach (var file in files)
            {
                if (SHParseDisplayName(file, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return false;
                absolute.Add(pidl);
            }
            var iidFolder = IidShellFolder;
            if (SHBindToParent(absolute[0], ref iidFolder, out var folderPtr, out _) != 0) return false;
            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            Marshal.Release(folderPtr);
            // Les identifiants « enfants » : la dernière partie de chaque chemin, relative au dossier commun.
            var children = absolute.Select(ILFindLastID).ToArray();
            var iidMenu = IidContextMenu;
            if (folder.GetUIObjectOf(owner, (uint)children.Length, children, ref iidMenu, IntPtr.Zero, out var menuPtr) != 0) return false;
            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);
            Marshal.Release(menuPtr);
            // Le menu doit être préparé : les extensions de Windows n'ajoutent leurs actions qu'à ce moment.
            menu = CreatePopupMenu();
            contextMenu.QueryContextMenu(menu, 0, 1, 0x7FFF, CmfNormal);
            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = CmicMaskUnicode | CmicMaskFlagNoUi,
                hwnd = owner,
                lpVerb = Marshal.StringToHGlobalAnsi(ShareVerb),
                lpVerbW = Marshal.StringToHGlobalUni(ShareVerb),
                nShow = SwShowNormal,
            };
            try { return contextMenu.InvokeCommand(ref info) == 0; }
            finally
            {
                Marshal.FreeHGlobal(info.lpVerb);
                Marshal.FreeHGlobal(info.lpVerbW);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return false;
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
