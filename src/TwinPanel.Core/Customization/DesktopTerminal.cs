// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.

namespace TwinPanel.Core.Customization;

/// <summary>
/// Terminal du bureau sous Linux : le programme présent, et la façon de lui donner un dossier et une commande.
/// Chaque terminal a ses options : Ptyxis (GNOME, Fedora, Ubuntu) ne comprend pas « -e », et ouvre sa fenêtre
/// dans son propre dossier si on ne lui en donne pas un.
/// </summary>
public static class DesktopTerminal
{
    /// <summary>Celui choisi par la distribution (Debian, Ubuntu), puis les terminaux connus, dans cet ordre.</summary>
    private static readonly string[] Candidates =
        ["x-terminal-emulator", "ptyxis", "gnome-terminal", "kgx", "konsole", "xfce4-terminal", "xterm"];

    /// <summary>
    /// Programme et arguments. <paramref name="folder"/> : dossier de départ (null : celui du processus) ;
    /// <paramref name="command"/> : programme et arguments à lancer dedans (null : le shell habituel).
    /// <paramref name="find"/> : chemin réel d'un programme (liens suivis), null s'il est absent.
    /// </summary>
    public static (string FileName, IReadOnlyList<string> Arguments) Command(
        string? folder, IReadOnlyList<string>? command, Func<string, string?>? find = null)
    {
        find ??= FindInPath;
        string? program = null;
        foreach (var name in Candidates)
            if ((program = find(name)) is not null) break;
        program ??= "x-terminal-emulator";

        // x-terminal-emulator est un lien : son vrai nom dit quelles options il comprend.
        // gnome-terminal.wrapper (Debian) traduit « -e » ; le vrai programme comprend tout le reste.
        var name0 = Path.GetFileName(program);
        if (name0.EndsWith(".wrapper", StringComparison.Ordinal)) { program = program[..^".wrapper".Length]; name0 = name0[..^".wrapper".Length]; }

        var args = new List<string>();
        switch (name0)
        {
            case "ptyxis":
                if (command is null) args.Add("--new-window");
                if (folder is not null) { args.Add("-d"); args.Add(folder); }
                if (command is not null) { args.Add("--"); args.AddRange(command); }
                break;
            case "gnome-terminal" or "kgx":
                if (folder is not null) args.Add("--working-directory=" + folder);
                if (command is not null) { args.Add("--"); args.AddRange(command); }
                break;
            case "konsole":
                if (folder is not null) { args.Add("--workdir"); args.Add(folder); }
                if (command is not null) { args.Add("-e"); args.AddRange(command); }
                break;
            case "xfce4-terminal":
                if (folder is not null) args.Add("--working-directory=" + folder);
                if (command is not null) { args.Add("-x"); args.AddRange(command); }
                break;
            default: // xterm et les autres : dossier du processus, « -e » compris partout.
                if (command is not null) { args.Add("-e"); args.AddRange(command); }
                break;
        }
        return (program, args);
    }

    private static string? FindInPath(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            try { return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path; }
            catch (IOException) { return path; }
            catch (UnauthorizedAccessException) { return path; }
        }
        return null;
    }
}
