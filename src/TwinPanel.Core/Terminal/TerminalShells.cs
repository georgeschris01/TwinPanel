// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Terminal;

/// <summary>Programmes qu'on peut lancer dans le terminal intégré : seulement ceux installés sur ce PC.</summary>
public static class TerminalShells
{
    private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    /// <summary>Chemin du programme, ou null s'il n'est pas installé.</summary>
    public static string? Find(TerminalShell shell, Func<string, bool>? exists = null, string? path = null)
    {
        exists ??= File.Exists;
        if (!OperatingSystem.IsWindows()) return shell == TerminalShell.UserShell ? LoginShell(exists) : null;
        return shell switch
        {
            TerminalShell.PowerShell => First(exists, Path.Combine(System32, "WindowsPowerShell", "v1.0", "powershell.exe")),
            TerminalShell.PowerShell7 => First(exists, [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
                .. OnPath("pwsh.exe", path)]),
            TerminalShell.Cmd => First(exists, Path.Combine(System32, "cmd.exe")),
            TerminalShell.Wsl => First(exists, Path.Combine(System32, "wsl.exe")),
            _ => null,
        };
    }

    private static string? First(Func<string, bool> exists, params string[] candidates) => candidates.FirstOrDefault(exists);

    private static IEnumerable<string> OnPath(string file, string? path) =>
        (path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => Path.Combine(d, file));

    /// <summary>Choix à proposer dans les Options.</summary>
    public static IReadOnlyList<TerminalShell> Installed(Func<string, bool>? exists = null) =>
        [.. Enum.GetValues<TerminalShell>().Where(s => s == TerminalShell.Custom || Find(s, exists) is not null).OrderBy(s => s == TerminalShell.Custom)];

    /// <summary>Le shell de l'utilisateur sous Linux : celui que son compte indique, sinon le plus courant.</summary>
    public static string? LoginShell(Func<string, bool>? exists = null, string? configured = null)
    {
        exists ??= File.Exists;
        configured ??= Environment.GetEnvironmentVariable("SHELL");
        return new[] { configured, "/bin/bash", "/bin/sh" }.FirstOrDefault(s => s is not null && s.StartsWith('/') && exists(s));
    }

    /// <summary>Programme à lancer sous Linux. Un réglage venu d'ailleurs ou un programme disparu donne le shell habituel.</summary>
    public static string LinuxProgram(TerminalSettings settings, Func<string, bool>? exists = null, string? configured = null)
    {
        exists ??= File.Exists;
        if (settings.IntegratedShell == TerminalShell.Custom)
        {
            var custom = settings.IntegratedShellPath.Trim().Trim('"');
            if (custom.Length > 0 && exists(custom)) return custom;
        }
        return LoginShell(exists, configured) ?? throw new IOException(L.T("Aucun shell trouvé sur ce système."));
    }

    /// <summary>La connexion qui convient au système.</summary>
    public static ITerminalConnection Connect(TerminalSettings settings, string directory, int columns, int rows)
    {
        if (OperatingSystem.IsWindows()) return new ConPtyConnection(CommandLine(settings), directory, columns, rows);
        if (OperatingSystem.IsLinux()) return new UnixPtyConnection(LinuxProgram(settings), [], directory, columns, rows);
        throw new PlatformNotSupportedException();
    }

    /// <summary>
    /// Ligne de commande à lancer. Si le programme choisi n'existe plus, on prend le premier qui marche
    /// (PowerShell, puis cmd) plutôt que de laisser un terminal vide.
    /// </summary>
    public static string CommandLine(TerminalSettings settings, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (settings.IntegratedShell == TerminalShell.Custom)
        {
            var custom = settings.IntegratedShellPath.Trim().Trim('"');
            if (custom.Length > 0 && exists(custom)) return Quote(custom);
        }
        else if (Find(settings.IntegratedShell, exists) is { } chosen) return Build(settings.IntegratedShell, chosen);

        foreach (var shell in new[] { TerminalShell.PowerShell, TerminalShell.Cmd })
            if (Find(shell, exists) is { } fallback) return Build(shell, fallback);
        return "cmd.exe";
    }

    private static string Build(TerminalShell shell, string file) =>
        shell is TerminalShell.PowerShell or TerminalShell.PowerShell7 ? Quote(file) + " -NoLogo" : Quote(file);

    private static string Quote(string file) => file.Contains(' ', StringComparison.Ordinal) ? $"\"{file}\"" : file;
}
