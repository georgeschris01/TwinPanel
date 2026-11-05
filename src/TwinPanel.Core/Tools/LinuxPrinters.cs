// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.ComponentModel;
using System.Diagnostics;

namespace TwinPanel.Core.Tools;

/// <summary>Impression sous Linux par les commandes du système d'impression (CUPS) : lpstat pour la liste, lp pour l'envoi.</summary>
public static class LinuxPrinters
{
    private const int TimeoutMilliseconds = 15_000;

    public static (IReadOnlyList<string> Printers, string? Default) List()
    {
        var printers = Names(Run("lpstat", "-e") ?? string.Empty);
        return (printers, DefaultName(Run("lpstat", "-d") ?? string.Empty));
    }

    /// <summary>Le système sait imprimer les textes, les PDF et les images ; pour le reste, il répond par une erreur.</summary>
    public static bool Send(string path, string printer) => Run("lp", "-d", printer, "--", path) is not null;

    internal static IReadOnlyList<string> Names(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>« system default destination: nom », ou « no system default destination » : pas de deux-points, pas d'imprimante.</summary>
    internal static string? DefaultName(string text)
    {
        int colon = text.LastIndexOf(':');
        var name = colon < 0 ? string.Empty : text[(colon + 1)..].Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>Texte produit par la commande, null si elle a échoué ou n'existe pas.</summary>
    private static string? Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // Réponses en anglais, quelle que soit la langue du système : on les lit.
        start.Environment["LC_ALL"] = "C";
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill();
                return null;
            }
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return null; }
    }
}
