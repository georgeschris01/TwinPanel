// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.InteropServices;
using System.Text;

namespace TwinPanel.Core.Media;

/// <summary>Types de pistes de MediaInfo (même numérotation que la bibliothèque).</summary>
public enum MediaInfoStream { General = 0, Video = 1, Audio = 2, Text = 3, Other = 4, Image = 5, Menu = 6 }

/// <summary>
/// Accès à la bibliothèque native MediaInfo (MediaArea, licence BSD-2), fournie par le paquet MediaInfo.Core.Native
/// (libmediainfo.dll sous Windows, libmediainfo.so sous Linux). Interface C officielle, appelée directement.
/// Une instance = un fichier ouvert ; à utiliser depuis un seul fil à la fois.
/// </summary>
public sealed class MediaInfoLib : IDisposable
{
    private const string Lib = "libmediainfo";
    private IntPtr _handle;
    private static readonly bool Wide = OperatingSystem.IsWindows(); // Windows : chaînes UTF-16 ; ailleurs : UTF-8

    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern IntPtr MediaInfo_New();
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern void MediaInfo_Delete(IntPtr handle);
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern IntPtr MediaInfo_Open(IntPtr handle, string fileName);
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern void MediaInfo_Close(IntPtr handle);
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern IntPtr MediaInfo_Inform(IntPtr handle, IntPtr reserved);
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern IntPtr MediaInfo_Option(IntPtr handle, string option, string value);
    [DllImport(Lib, CharSet = CharSet.Unicode)]
    private static extern IntPtr MediaInfo_Get(IntPtr handle, IntPtr streamKind, IntPtr streamNumber, string parameter, IntPtr kindOfInfo, IntPtr kindOfSearch);
    [DllImport(Lib, CharSet = CharSet.Unicode)] private static extern IntPtr MediaInfo_Count_Get(IntPtr handle, IntPtr streamKind, IntPtr streamNumber);

    // Variantes « A » (UTF-8) pour Linux / macOS.
    [DllImport(Lib)] private static extern IntPtr MediaInfoA_New();
    [DllImport(Lib)] private static extern void MediaInfoA_Delete(IntPtr handle);
    [DllImport(Lib)] private static extern IntPtr MediaInfoA_Open(IntPtr handle, IntPtr fileName);
    [DllImport(Lib)] private static extern void MediaInfoA_Close(IntPtr handle);
    [DllImport(Lib)] private static extern IntPtr MediaInfoA_Inform(IntPtr handle, IntPtr reserved);
    [DllImport(Lib)] private static extern IntPtr MediaInfoA_Option(IntPtr handle, IntPtr option, IntPtr value);
    [DllImport(Lib)]
    private static extern IntPtr MediaInfoA_Get(IntPtr handle, IntPtr streamKind, IntPtr streamNumber, IntPtr parameter, IntPtr kindOfInfo, IntPtr kindOfSearch);
    [DllImport(Lib)] private static extern IntPtr MediaInfoA_Count_Get(IntPtr handle, IntPtr streamKind, IntPtr streamNumber);

    private static bool? _available;

    /// <summary>La bibliothèque native est-elle présente et utilisable ?</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_available is { } a) return a;
            try
            {
                using var probe = new MediaInfoLib();
                _available = true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                _available = false;
            }
            return _available.Value;
        }
    }

    public MediaInfoLib()
    {
        _handle = Wide ? MediaInfo_New() : MediaInfoA_New();
        if (_handle == IntPtr.Zero) throw new DllNotFoundException(L.T("MediaInfo n'a pas pu démarrer."));
        if (!Wide) OptionUtf8("CharSet", "UTF-8");
    }

    /// <summary>Ouvre un fichier ; faux si MediaInfo ne le reconnaît pas.</summary>
    public bool Open(string path)
    {
        if (Wide) return MediaInfo_Open(_handle, path) != IntPtr.Zero;
        var p = Utf8(path);
        try { return MediaInfoA_Open(_handle, p) != IntPtr.Zero; }
        finally { Marshal.FreeHGlobal(p); }
    }

    /// <summary>Rapport texte complet (comme le programme MediaInfo, en anglais).</summary>
    public string Inform() => Read(Wide ? MediaInfo_Inform(_handle, IntPtr.Zero) : MediaInfoA_Inform(_handle, IntPtr.Zero));

    public int Count(MediaInfoStream kind) =>
        (int)(Wide ? MediaInfo_Count_Get(_handle, (IntPtr)(int)kind, (IntPtr)(-1)) : MediaInfoA_Count_Get(_handle, (IntPtr)(int)kind, (IntPtr)(-1)));

    /// <summary>Valeur d'un champ (« Format », « Language/String »…) ; vide s'il n'existe pas.</summary>
    public string Get(MediaInfoStream kind, int number, string parameter)
    {
        if (Wide) return Read(MediaInfo_Get(_handle, (IntPtr)(int)kind, (IntPtr)number, parameter, (IntPtr)1, IntPtr.Zero)).Trim();
        var p = Utf8(parameter);
        try { return Read(MediaInfoA_Get(_handle, (IntPtr)(int)kind, (IntPtr)number, p, (IntPtr)1, IntPtr.Zero)).Trim(); }
        finally { Marshal.FreeHGlobal(p); }
    }

    public void Option(string option, string value)
    {
        if (Wide) MediaInfo_Option(_handle, option, value);
        else OptionUtf8(option, value);
    }

    private void OptionUtf8(string option, string value)
    {
        var o = Utf8(option);
        var v = Utf8(value);
        try { MediaInfoA_Option(_handle, o, v); }
        finally { Marshal.FreeHGlobal(o); Marshal.FreeHGlobal(v); }
    }

    private static string Read(IntPtr p) => p == IntPtr.Zero ? string.Empty : (Wide ? Marshal.PtrToStringUni(p) : Marshal.PtrToStringUTF8(p)) ?? string.Empty;

    private static IntPtr Utf8(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s + "\0");
        var p = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        return p;
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        if (Wide) { MediaInfo_Close(_handle); MediaInfo_Delete(_handle); }
        else { MediaInfoA_Close(_handle); MediaInfoA_Delete(_handle); }
        _handle = IntPtr.Zero;
    }
}
