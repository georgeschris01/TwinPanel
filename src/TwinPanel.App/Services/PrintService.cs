// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using TwinPanel.Core.Viewer;

namespace TwinPanel.App.Services;

/// <summary>Comment un fichier a été imprimé (ou pas).</summary>
public enum PrintOutcome { Printed, OpenedForManualPrint, Failed }

/// <summary>
/// Impression sous Windows, sur l'imprimante choisie :
/// textes par le Bloc-notes (« /pt »), images par Paint (« /pt »), autres fichiers par l'action « Imprimer vers »
/// de leur application ; si elle n'existe pas, le fichier est ouvert dans son application pour Ctrl+P.
/// Le Bloc-notes et Paint sont utilisés même si les .txt / .jpg s'ouvrent avec un autre programme (Sublime Text, Photos…),
/// qui souvent ne sait pas imprimer sans être ouvert.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrintService
{
    private static readonly HashSet<string> PaintImages = new(StringComparer.OrdinalIgnoreCase) { ".bmp", ".jpg", ".jpeg", ".png", ".gif", ".tif", ".tiff", ".ico" };

    /// <summary>Imprimantes installées (liste de Windows) et celle par défaut.</summary>
    public static (IReadOnlyList<string> Printers, string? Default) Printers()
    {
        var list = new List<string>();
        string? def = null;
        try
        {
            using (var devices = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Devices"))
                if (devices is not null) list.AddRange(devices.GetValueNames().Where(n => n.Length > 0));
            using var windows = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
            if (windows?.GetValue("Device") is string device && device.Split(',')[0] is { Length: > 0 } name) def = name;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        list.Sort(StringComparer.CurrentCultureIgnoreCase);
        if (def is not null && !list.Contains(def)) list.Insert(0, def);
        return (list, def);
    }

    /// <summary>Le fichier est-il du texte (les 8 premiers Ko ne ressemblent pas à un fichier binaire) ?</summary>
    public static bool IsText(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var buffer = new byte[8192];
            int n = f.Read(buffer, 0, buffer.Length);
            return !TextDecoding.LooksBinary(buffer.AsSpan(0, n));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static PrintOutcome Print(string path, string printer)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (PaintImages.Contains(Path.GetExtension(path)) && Run(Path.Combine(system, "mspaint.exe"), "/pt", path, printer))
            return PrintOutcome.Printed;
        if (IsText(path) && Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe"), "/pt", path, printer))
            return PrintOutcome.Printed;

        // Autres fichiers : l'application associée, si elle sait « imprimer vers » / « imprimer ».
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "printto", Arguments = Quote(printer), WindowStyle = ProcessWindowStyle.Minimized })?.Dispose();
            return PrintOutcome.Printed;
        }
        catch (System.ComponentModel.Win32Exception) { }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print", WindowStyle = ProcessWindowStyle.Minimized })?.Dispose();
            return PrintOutcome.Printed;
        }
        catch (System.ComponentModel.Win32Exception) { }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            return PrintOutcome.OpenedForManualPrint;
        }
        catch (System.ComponentModel.Win32Exception) { return PrintOutcome.Failed; }
    }

    private static bool Run(string exe, params string[] args)
    {
        if (!File.Exists(exe)) return false;
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Minimized };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", string.Empty) + "\"";
}
