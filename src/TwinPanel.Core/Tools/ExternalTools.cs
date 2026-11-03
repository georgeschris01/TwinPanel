// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;

namespace TwinPanel.Core.Tools;

/// <summary>Un programme externe (éditeur, visionneuse) : chemin + modèle d'arguments.</summary>
public sealed record ExternalTool(string Name, string Path, string Arguments = "\"%1\"");

/// <summary>Construction de la ligne de commande et détection des éditeurs installés.</summary>
public static class ExternalTools
{
    /// <summary>Le Bloc-notes de Windows : présent sur tous les Windows, sert de valeur par défaut et de repli.</summary>
    public static ExternalTool WindowsNotepad { get; } = new(L.T("Bloc-notes (Windows)"), "notepad.exe");

    /// <summary>Chemin spécial qui désigne l'éditeur intégré de TwinPanel.</summary>
    public const string InternalPath = "internal";

    /// <summary>L'éditeur intégré : toujours disponible, proposé juste après le Bloc-notes.</summary>
    public static ExternalTool InternalEditor { get; } = new(L.T("Éditeur intégré (TwinPanel)"), InternalPath, string.Empty);

    public static bool IsInternal(string path) => string.Equals(path?.Trim(), InternalPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Remplace les codes du modèle d'arguments :
    /// %1 chemin complet, %N nom du fichier, %D dossier, %L numéro de ligne (1 par défaut).
    /// Si le modèle ne contient pas %1, le chemin est ajouté à la fin entre guillemets.
    /// </summary>
    public static string ExpandArguments(string template, string filePath, int line = 1)
    {
        if (string.IsNullOrWhiteSpace(template)) template = "\"%1\"";
        var result = template
            .Replace("%1", filePath, StringComparison.Ordinal)
            .Replace("%N", System.IO.Path.GetFileName(filePath), StringComparison.Ordinal)
            .Replace("%D", System.IO.Path.GetDirectoryName(filePath) ?? string.Empty, StringComparison.Ordinal)
            .Replace("%L", line.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (!template.Contains("%1", StringComparison.Ordinal)) result = $"{result} \"{filePath}\"".Trim();
        return result;
    }

    public static ProcessStartInfo BuildStartInfo(ExternalTool tool, string filePath, int line = 1) =>
        new(tool.Path, ExpandArguments(tool.Arguments, filePath, line))
        {
            UseShellExecute = false,
            WorkingDirectory = System.IO.Path.GetDirectoryName(filePath) ?? string.Empty,
        };

    /// <summary>
    /// Vrai si le programme peut être lancé : chemin complet existant, ou simple nom de programme
    /// (« notepad.exe ») trouvé dans le dossier Windows ou le PATH.
    /// </summary>
    public static bool IsAvailable(string path, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (IsInternal(path)) return true;
        if (System.IO.Path.IsPathRooted(path)) return fileExists(path);

        var dirs = new List<string>();
        var windir = Environment.GetEnvironmentVariable("WINDIR");
        if (!string.IsNullOrEmpty(windir)) { dirs.Add(windir); dirs.Add(System.IO.Path.Combine(windir, "System32")); }
        dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        return dirs.Any(d => fileExists(System.IO.Path.Combine(d.Trim('"'), path)));
    }

    /// <summary>Éditeurs connus réellement installés (le Bloc-notes en premier sous Windows).</summary>
    public static IReadOnlyList<ExternalTool> DetectEditors(Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        var found = new List<ExternalTool>();
        if (OperatingSystem.IsWindows()) found.Add(WindowsNotepad);
        found.Add(InternalEditor);

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        (string Name, string Args, string[] Paths)[] candidates =
        [
            ("VSCodium", "--goto \"%1\":%L", [Combine(local, @"Programs\VSCodium\VSCodium.exe"), Combine(pf, @"VSCodium\VSCodium.exe")]),
            ("Visual Studio Code", "--goto \"%1\":%L", [Combine(local, @"Programs\Microsoft VS Code\Code.exe"), Combine(pf, @"Microsoft VS Code\Code.exe")]),
            ("Notepad++", "-n%L \"%1\"", [Combine(pf, @"Notepad++\notepad++.exe"), Combine(pf86, @"Notepad++\notepad++.exe")]),
            ("Sublime Text", "\"%1\":%L", [Combine(pf, @"Sublime Text\sublime_text.exe"), Combine(pf, @"Sublime Text 3\sublime_text.exe")]),
            ("Kate", "-l %L \"%1\"", [Combine(pf, @"Kate\bin\kate.exe")]),
            // Linux
            ("VSCodium", "--goto \"%1\":%L", ["/usr/bin/codium", "/usr/share/codium/codium"]),
            ("Kate", "-l %L \"%1\"", ["/usr/bin/kate"]),
            ("gedit", "\"%1\"", ["/usr/bin/gedit"]),
            // Éditeur de GNOME depuis GNOME 42 (Fedora, Ubuntu), à la place de gedit.
            ("GNOME Text Editor", "\"%1\"", ["/usr/bin/gnome-text-editor"]),
            ("Mousepad", "\"%1\"", ["/usr/bin/mousepad"]),
        ];

        foreach (var (name, args, paths) in candidates)
        {
            var path = paths.FirstOrDefault(p => p.Length > 0 && fileExists(p));
            if (path is not null && !found.Any(f => f.Name == name)) found.Add(new ExternalTool(name, path, args));
        }
        return found;
    }

    private static string Combine(string root, string relative) =>
        string.IsNullOrEmpty(root) ? string.Empty : System.IO.Path.Combine(root, relative.Replace('\\', System.IO.Path.DirectorySeparatorChar));
}
