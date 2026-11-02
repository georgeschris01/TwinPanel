// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using System.Text;
using TwinPanel.Core.Selection;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Customization;

/// <summary>Ce que voient les paramètres %P %N %F %T %S au moment du clic.</summary>
public sealed record CommandContext(string CurrentFolder, string? CurrentName, string? CurrentPath, string OtherFolder, IReadOnlyList<string> Marked);

/// <summary>Remplacement des paramètres des boutons et du menu Démarrer.</summary>
public static class CommandArguments
{
    /// <summary>
    /// %P dossier actif, %N nom de l'élément sous le curseur, %F son chemin complet, %T dossier de l'autre panneau,
    /// %S éléments marqués (ou l'élément courant), chacun entre guillemets ; %% donne « % ». Lettres en majuscules ou minuscules.
    /// </summary>
    public static string Expand(string template, CommandContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        var sb = new StringBuilder(template.Length + 32);
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c != '%' || i + 1 >= template.Length) { sb.Append(c); continue; }
            char k = char.ToUpperInvariant(template[i + 1]);
            string? value = k switch
            {
                'P' => ctx.CurrentFolder,
                'N' => ctx.CurrentName ?? string.Empty,
                'F' => ctx.CurrentPath ?? string.Empty,
                'T' => ctx.OtherFolder,
                'S' => string.Join(' ', SelectedPaths(ctx).Select(Quote)),
                '%' => "%",
                _ => null,
            };
            if (value is null) { sb.Append(c); continue; }
            sb.Append(value);
            i++;
        }
        return sb.ToString();
    }

    private static IEnumerable<string> SelectedPaths(CommandContext ctx)
    {
        if (ctx.Marked.Count > 0) return ctx.Marked;
        return ctx.CurrentPath is null ? Array.Empty<string>() : new[] { ctx.CurrentPath };
    }

    /// <summary>Met entre guillemets pour Windows (les guillemets internes sont doublés en \").</summary>
    public static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    /// <summary>Lancement d'un programme ou document d'un bouton (le système choisit l'application pour un document).</summary>
    public static ProcessStartInfo BuildStartInfo(CustomCommand command, CommandContext ctx)
    {
        var target = Environment.ExpandEnvironmentVariables(Expand(command.Target, ctx)).Trim().Trim('"');
        var startIn = command.StartIn.Length > 0 ? Environment.ExpandEnvironmentVariables(Expand(command.StartIn, ctx)).Trim('"') : ctx.CurrentFolder;
        return new ProcessStartInfo(target)
        {
            Arguments = Expand(command.Arguments, ctx),
            WorkingDirectory = Directory.Exists(startIn) ? startIn : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = true,
        };
    }
}

/// <summary>Raccourcis clavier écrits de façon unique (« ctrl+shift+o » → « Ctrl+Shift+O »), pour comparer et trouver les doublons.</summary>
public static class Shortcuts
{
    private static readonly string[] ModifierOrder = ["Ctrl", "Alt", "Shift", "Meta"];

    public static string Normalize(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture)) return string.Empty;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        // « Ctrl++ » (touche plus) : le dernier morceau vide est la touche « + ».
        if (gesture.TrimEnd().EndsWith("++", StringComparison.Ordinal)) parts.Add("OemPlus");
        var mods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string key = string.Empty;
        foreach (var p in parts)
        {
            var m = p.ToLowerInvariant() switch
            {
                "ctrl" or "control" or "ctl" => "Ctrl",
                "alt" => "Alt",
                "shift" or "maj" => "Shift",
                "win" or "meta" or "cmd" => "Meta",
                _ => null,
            };
            if (m is not null) mods.Add(m);
            else key = p.Length == 1 ? p.ToUpperInvariant() : char.ToUpperInvariant(p[0]) + p[1..];
        }
        if (key.Length == 0) return string.Empty;
        return string.Join('+', ModifierOrder.Where(mods.Contains).Append(key));
    }

    /// <summary>Raccourcis utilisés par plusieurs commandes : touche → commandes.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> FindConflicts(IReadOnlyDictionary<string, string> commandToGesture) =>
        commandToGesture.Where(kv => Normalize(kv.Value).Length > 0)
            .GroupBy(kv => Normalize(kv.Value), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(kv => kv.Key).Order().ToList(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>Couleur d'un fichier selon les réglages : règles par masque d'abord, puis couleur du type.</summary>
public sealed class FileColorRules
{
    private readonly List<(MaskMatcher Matcher, string Color)> _rules;
    private readonly Dictionary<FileCategory, string> _categories;

    public FileColorRules(AppearanceSettings appearance)
    {
        _rules = appearance.ColorRules.Where(r => !string.IsNullOrWhiteSpace(r.Mask) && IsColor(r.Color))
            .Select(r => (new MaskMatcher(r.Mask), r.Color)).ToList();
        _categories = appearance.CategoryColors.Where(kv => IsColor(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    public static bool IsColor(string? s) =>
        s is { Length: 7 or 9 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Couleur choisie par une règle de masque (null : aucune règle ne correspond).</summary>
    public string? RuleColor(FileEntry entry)
    {
        if (entry.IsParentLink) return null;
        foreach (var (m, color) in _rules)
            if (m.IsMatch(entry.Name)) return color;
        return null;
    }

    /// <summary>Couleur personnalisée du type (null : couleur par défaut de TwinPanel).</summary>
    public string? CategoryColor(FileCategory category) => _categories.GetValueOrDefault(category);
}

/// <summary>Commande du terminal à ouvrir « ici » (dossier local, ou connexion ssh vers le serveur affiché).</summary>
public static class TerminalCommand
{
    public sealed record Target(string? Folder, string? SshUser, string? SshHost, int SshPort);

    /// <summary>Le terminal choisi (Auto : Windows Terminal s'il est installé, sinon PowerShell, sinon cmd).</summary>
    public static TerminalKind Resolve(TerminalSettings s, Func<string, bool>? exists = null)
    {
        if (s.Kind != TerminalKind.Auto) return s.Kind;
        exists ??= File.Exists;
        var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        if (OperatingSystem.IsWindows() && exists(wt)) return TerminalKind.WindowsTerminal;
        return OperatingSystem.IsWindows() ? TerminalKind.PowerShell : TerminalKind.Custom;
    }

    /// <summary>Programme et arguments (liste : aucun problème de guillemets, aucune injection possible).</summary>
    public static (string FileName, IReadOnlyList<string> Arguments) Build(TerminalSettings s, Target target, Func<string, bool>? exists = null)
    {
        var kind = Resolve(s, exists);
        var folder = target.Folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string>? ssh = null;
        if (target.SshHost is { Length: > 0 } host)
        {
            ssh = ["ssh"];
            if (target.SshPort is > 0 and not 22) { ssh.Add("-p"); ssh.Add(target.SshPort.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            ssh.Add(string.IsNullOrEmpty(target.SshUser) ? host : $"{target.SshUser}@{host}");
        }
        switch (kind)
        {
            case TerminalKind.WindowsTerminal:
            {
                var args = new List<string> { "-d", folder };
                if (ssh is not null) args.AddRange(ssh);
                return ("wt.exe", args);
            }
            case TerminalKind.PowerShell:
            {
                var args = new List<string> { "-NoExit", "-Command" };
                args.Add(ssh is not null
                    ? string.Join(' ', ssh.Select(PsQuote))
                    : "Set-Location -LiteralPath " + PsQuote(folder));
                return ("powershell.exe", args);
            }
            case TerminalKind.Cmd:
            {
                var args = new List<string> { "/K" };
                if (ssh is not null) args.AddRange(ssh);
                else { args.Add("cd"); args.Add("/d"); args.Add(folder); }
                return ("cmd.exe", args);
            }
            default:
            {
                if (string.IsNullOrWhiteSpace(s.CustomPath)) return (OperatingSystem.IsWindows() ? "cmd.exe" : "x-terminal-emulator", []);
                // %P remplacé après le découpage : un dossier avec des espaces reste un seul argument.
                var args = s.CustomArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Replace("%P", folder, StringComparison.OrdinalIgnoreCase)).ToList();
                if (ssh is not null) args.AddRange(ssh);
                return (s.CustomPath, args);
            }
        }
    }

    /// <summary>Chaîne PowerShell entre apostrophes (l'apostrophe se double).</summary>
    public static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";
}
