// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;

namespace TwinPanel.App.Services;

/// <summary>Ouverture de fichiers avec les applications du système.</summary>
public static class ShellService
{
    /// <summary>Ouvre un fichier avec l'application associée (comme un double-clic dans l'Explorateur).</summary>
    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>Imprime avec l'application associée (action « Imprimer » de Windows) ; faux si ce type de fichier ne sait pas s'imprimer.</summary>
    public static bool Print(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print", WindowStyle = ProcessWindowStyle.Minimized })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // aucune application associée pour imprimer
    }

    /// <summary>Ouvre un fichier dans l'éditeur de texte (provisoire : Bloc-notes sous Windows).</summary>
    public static void Edit(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("notepad.exe") { UseShellExecute = false };
            psi.ArgumentList.Add(path);   // ArgumentList échappe correctement les espaces
            Process.Start(psi)?.Dispose();
        }
        else
        {
            Open(path);
        }
    }
}
