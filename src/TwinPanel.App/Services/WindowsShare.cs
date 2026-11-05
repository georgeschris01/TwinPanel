// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TwinPanel.App.Services;

/// <summary>
/// Fenêtre « Partager » de Windows (partage à proximité, courrier, applications…), par le composant de partage
/// intégré à Windows (DataTransferManager), appelé directement, sans bibliothèque.
/// Identifiants et ordre des fonctions relevés dans les descriptions officielles de Windows (.winmd) ;
/// les identifiants des interfaces génériques sont calculés comme Windows le fait (vérifié sur le gestionnaire d'événement).
/// Tout se passe sur le fil de la fenêtre.
/// </summary>
[SupportedOSPlatform("windows")]
public static unsafe class WindowsShare
{
    private static readonly Guid IidDataTransferManagerInterop = new("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8");
    private static readonly Guid IidDataTransferManager = new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
    private static readonly Guid IidStorageFileStatics = new("5984C710-DAF2-43C8-8BB4-A4D3EACFD03F");
    private static readonly Guid IidStorageItem = new("4207A996-CA2F-42F7-BDE8-8B10457A7F30");
    private static readonly Guid IidAsyncInfo = new("00000036-0000-0000-C000-000000000046");

    // Place des fonctions dans chaque interface (0 à 5 : IUnknown puis IInspectable).
    private const int InteropGetForWindow = 3, InteropShowShareUi = 4;
    private const int ManagerAddDataRequested = 6, ManagerRemoveDataRequested = 7;
    private const int ArgsGetRequest = 6;
    private const int RequestGetData = 6;
    private const int PackageGetProperties = 7, PackageSetStorageItems = 22;
    private const int PropertiesPutTitle = 7;
    private const int StaticsGetFileFromPathAsync = 6;
    private const int AsyncInfoGetStatus = 7, AsyncInfoGetErrorCode = 8;
    private const int AsyncOperationGetResults = 8;
    private const int AsyncStarted = 0, AsyncCompleted = 1;

    // Gardés en vie : Windows demande les fichiers après le retour de l'appel, quand l'utilisateur choisit une destination.
    private static IntPtr _manager;
    private static long _token;
    private static IntPtr _handler;
    private static ShareHandler? _handlerObject;
    private static StorageItems? _items;

    /// <summary>Fenêtre principale de TwinPanel : la fenêtre de partage s'ouvre au-dessus d'elle.</summary>
    public static IntPtr MainWindowHandle() =>
        Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }
            ? window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero
            : IntPtr.Zero;

    /// <summary>Ouvre la fenêtre de partage. Rend null si c'est fait, sinon la raison (avec le code de Windows).</summary>
    public static string? Show(IReadOnlyList<string> files, IntPtr owner)
    {
        if (files.Count == 0) return "0";
        if (owner == IntPtr.Zero) return "HWND";
        var items = new List<IntPtr>();
        IntPtr interop = IntPtr.Zero;
        try
        {
            if (LoadItems(files, items) is { } loadError) return loadError;
            int hr = ActivationFactory("Windows.ApplicationModel.DataTransfer.DataTransferManager", IidDataTransferManagerInterop, out interop);
            if (hr != 0) return $"RoGetActivationFactory 0x{hr:X8}";
            IntPtr manager;
            var iid = IidDataTransferManager;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(interop, InteropGetForWindow))(interop, owner, &iid, &manager);
            if (hr != 0) return $"GetForWindow 0x{hr:X8}";

            // Une seule inscription à la fois : la demande précédente est retirée.
            Unregister();
            _items = new StorageItems(items);
            items.Clear(); // appartiennent maintenant à _items
            _handlerObject = new ShareHandler(_items, files.Count == 1 ? Path.GetFileName(files[0]) : "TwinPanel");
            _handler = Marshal.GetComInterfaceForObject(_handlerObject, typeof(ITypedEventHandler));
            _manager = manager;
            long token;
            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, long*, int>)Slot(manager, ManagerAddDataRequested))(manager, _handler, &token);
            if (hr != 0) { Unregister(); return $"add_DataRequested 0x{hr:X8}"; }
            _token = token;

            hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(interop, InteropShowShareUi))(interop, owner);
            return hr == 0 ? null : $"ShowShareUIForWindow 0x{hr:X8}";
        }
        finally
        {
            foreach (var item in items) Marshal.Release(item);
            if (interop != IntPtr.Zero) Marshal.Release(interop);
        }
    }

    private static void Unregister()
    {
        if (_manager != IntPtr.Zero)
        {
            if (_token != 0) ((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Slot(_manager, ManagerRemoveDataRequested))(_manager, _token);
            Marshal.Release(_manager);
        }
        if (_handler != IntPtr.Zero) Marshal.Release(_handler);
        _items?.Dispose();
        _manager = _handler = IntPtr.Zero;
        _token = 0;
        _handlerObject = null;
        _items = null;
    }

    /// <summary>Un « StorageItem » de Windows par fichier (attente courte : fichiers de ce PC).</summary>
    private static string? LoadItems(IReadOnlyList<string> files, List<IntPtr> items)
    {
        int hr = ActivationFactory("Windows.Storage.StorageFile", IidStorageFileStatics, out var statics);
        if (hr != 0) return $"StorageFile 0x{hr:X8}";
        try
        {
            foreach (var file in files)
            {
                IntPtr operation;
                var path = HString.Create(file);
                try { hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(statics, StaticsGetFileFromPathAsync))(statics, path, &operation); }
                finally { HString.Delete(path); }
                if (hr != 0) return $"GetFileFromPathAsync 0x{hr:X8} — {file}";
                try
                {
                    var iidInfo = IidAsyncInfo;
                    if (Marshal.QueryInterface(operation, in iidInfo, out var info) != 0) return "IAsyncInfo";
                    try
                    {
                        int status = AsyncStarted;
                        var limit = Environment.TickCount64 + 15_000;
                        while (true)
                        {
                            hr = ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(info, AsyncInfoGetStatus))(info, &status);
                            if (hr != 0 || status != AsyncStarted || Environment.TickCount64 > limit) break;
                            Thread.Sleep(5);
                        }
                        if (status != AsyncCompleted)
                        {
                            int error = 0;
                            ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(info, AsyncInfoGetErrorCode))(info, &error);
                            return $"GetFileFromPathAsync 0x{error:X8} — {file}";
                        }
                    }
                    finally { Marshal.Release(info); }
                    IntPtr storageFile;
                    hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(operation, AsyncOperationGetResults))(operation, &storageFile);
                    if (hr != 0) return $"GetResults 0x{hr:X8}";
                    var iidItem = IidStorageItem;
                    hr = Marshal.QueryInterface(storageFile, in iidItem, out var item);
                    Marshal.Release(storageFile);
                    if (hr != 0) return $"IStorageItem 0x{hr:X8}";
                    items.Add(item);
                }
                finally { Marshal.Release(operation); }
            }
            return null;
        }
        finally { Marshal.Release(statics); }
    }

    private static int ActivationFactory(string className, Guid iid, out IntPtr factory)
    {
        var name = HString.Create(className);
        try { return RoGetActivationFactory(name, ref iid, out factory); }
        finally { HString.Delete(name); }
    }

    private static void* Slot(IntPtr instance, int index) => (*(void***)instance)[index];

    /// <summary>Appelé par Windows quand l'utilisateur a choisi où partager : on donne le titre et les fichiers.</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class ShareHandler(StorageItems items, string title) : ITypedEventHandler
    {
        public int Invoke(IntPtr sender, IntPtr args)
        {
            IntPtr request = IntPtr.Zero, package = IntPtr.Zero, properties = IntPtr.Zero, iterable = IntPtr.Zero;
            try
            {
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(args, ArgsGetRequest))(args, &request);
                if (hr != 0) return hr;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(request, RequestGetData))(request, &package);
                if (hr != 0) return hr;
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(package, PackageGetProperties))(package, &properties);
                if (hr != 0) return hr;
                var name = HString.Create(title);
                try { hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(properties, PropertiesPutTitle))(properties, name); }
                finally { HString.Delete(name); }
                if (hr != 0) return hr;
                iterable = Marshal.GetComInterfaceForObject(items, typeof(IIterableStorageItem));
                return ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(package, PackageSetStorageItems))(package, iterable);
            }
            catch (Exception ex) { return ex.HResult == 0 ? unchecked((int)0x80004005) : ex.HResult; }
            finally
            {
                foreach (var p in new[] { iterable, properties, package, request })
                    if (p != IntPtr.Zero) Marshal.Release(p);
            }
        }
    }

    /// <summary>La liste des fichiers, sous la forme que Windows attend (IIterable de IStorageItem).</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class StorageItems(List<IntPtr> items) : IIterableStorageItem, IInspectable, IDisposable
    {
        private readonly IntPtr[] _items = [.. items];

        public int GetIids(out uint count, out IntPtr iids) { count = 0; iids = IntPtr.Zero; return 0; }
        public int GetRuntimeClassName(out IntPtr name) { name = IntPtr.Zero; return 0; }
        public int GetTrustLevel(out int level) { level = 0; return 0; }

        public int First(out IntPtr iterator)
        {
            iterator = Marshal.GetComInterfaceForObject(new StorageItemIterator(_items), typeof(IIteratorStorageItem));
            return 0;
        }

        public void Dispose()
        {
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i] != IntPtr.Zero) Marshal.Release(_items[i]);
                _items[i] = IntPtr.Zero;
            }
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class StorageItemIterator(IntPtr[] items) : IIteratorStorageItem, IInspectable
    {
        private int _index;

        public int GetIids(out uint count, out IntPtr iids) { count = 0; iids = IntPtr.Zero; return 0; }
        public int GetRuntimeClassName(out IntPtr name) { name = IntPtr.Zero; return 0; }
        public int GetTrustLevel(out int level) { level = 0; return 0; }

        public int get_Current(out IntPtr current)
        {
            current = IntPtr.Zero;
            if (_index >= items.Length) return unchecked((int)0x8000000B); // E_BOUNDS
            Marshal.AddRef(items[_index]);
            current = items[_index];
            return 0;
        }

        public int get_HasCurrent(out byte hasCurrent) { hasCurrent = _index < items.Length ? (byte)1 : (byte)0; return 0; }

        public int MoveNext(out byte hasCurrent)
        {
            if (_index < items.Length) _index++;
            return get_HasCurrent(out hasCurrent);
        }

        public int GetMany(uint capacity, IntPtr destination, out uint actual)
        {
            actual = 0;
            while (actual < capacity && _index < items.Length)
            {
                Marshal.AddRef(items[_index]);
                Marshal.WriteIntPtr(destination, (int)actual * IntPtr.Size, items[_index]);
                _index++;
                actual++;
            }
            return 0;
        }
    }

    // TypedEventHandler<DataTransferManager, DataRequestedEventArgs> : un délégué Windows, fondé sur IUnknown.
    [ComImport, Guid("EC6F9CC8-46D0-5E0E-B4D2-7D7773AE37A0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITypedEventHandler
    {
        [PreserveSig] int Invoke(IntPtr sender, IntPtr args);
    }

    [ComImport, Guid("AF86E2E0-B12D-4C6A-9C5A-D7AA65101E90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInspectable
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
    }

    // IIterable<IStorageItem>
    [ComImport, Guid("BB8B8418-65D1-544B-B083-6D172F568C73"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IIterableStorageItem
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int First(out IntPtr iterator);
    }

    // IIterator<IStorageItem>
    [ComImport, Guid("05B487C2-3830-5D3C-98DA-25FA11542DBD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IIteratorStorageItem
    {
        [PreserveSig] int GetIids(out uint count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr name);
        [PreserveSig] int GetTrustLevel(out int level);
        [PreserveSig] int get_Current(out IntPtr current);
        [PreserveSig] int get_HasCurrent(out byte hasCurrent);
        [PreserveSig] int MoveNext(out byte hasCurrent);
        [PreserveSig] int GetMany(uint capacity, IntPtr items, out uint actual);
    }

    private static class HString
    {
        public static IntPtr Create(string text)
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(text, (uint)text.Length, out var h));
            return h;
        }

        public static void Delete(IntPtr h)
        {
            if (h != IntPtr.Zero) WindowsDeleteString(h);
        }

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string source, uint length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);
}
