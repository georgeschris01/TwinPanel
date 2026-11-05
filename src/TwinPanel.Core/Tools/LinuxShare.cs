// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.ComponentModel;
using System.Diagnostics;

namespace TwinPanel.Core.Tools;

/// <summary>
/// Partager des fichiers sous Linux, où il n'existe pas de fenêtre de partage commune à tous les bureaux :
/// par courriel (xdg-email ouvre la messagerie habituelle, fichiers joints) ou vers un appareil relié par KDE Connect.
/// </summary>
public static class LinuxShare
{
    private const int TimeoutMilliseconds = 5_000;

    public sealed record Device(string Id, string Name);

    /// <summary>Courriel : la messagerie choisie dans le bureau s'ouvre avec les fichiers joints.</summary>
    public static ProcessStartInfo Email(IEnumerable<string> files)
    {
        var start = new ProcessStartInfo("xdg-email") { UseShellExecute = false };
        foreach (var file in files)
        {
            start.ArgumentList.Add("--attach");
            start.ArgumentList.Add(file);
        }
        return start;
    }

    /// <summary>Envoi d'un fichier à un appareil KDE Connect (un appel par fichier).</summary>
    public static ProcessStartInfo ToDevice(Device device, string file)
    {
        var start = new ProcessStartInfo("kdeconnect-cli") { UseShellExecute = false };
        foreach (var argument in new[] { "--device", device.Id, "--share", file }) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>Appareils KDE Connect joignables maintenant (liste vide si KDE Connect n'est pas installé).</summary>
    public static IReadOnlyList<Device> Devices() => ParseDevices(Run("kdeconnect-cli", "--list-available", "--id-name-only") ?? string.Empty);

    /// <summary>Une ligne par appareil : « identifiant nom de l'appareil ».</summary>
    internal static IReadOnlyList<Device> ParseDevices(string text) =>
        [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(' ', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
            .Select(parts => new Device(parts[0], parts[1]))];

    /// <summary>Le programme est-il installé (dans le PATH) ?</summary>
    public static bool Exists(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, program)));

    /// <summary>Texte produit par la commande, null si elle a échoué, n'existe pas ou ne répond pas.</summary>
    private static string? Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
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
