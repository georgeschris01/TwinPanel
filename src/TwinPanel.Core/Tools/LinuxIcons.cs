// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text.RegularExpressions;

namespace TwinPanel.Core.Tools;

/// <summary>Remise en forme des SVG des thèmes, avant de les donner à la bibliothèque de dessin.</summary>
public static class LinuxSvgText
{
    private static readonly Regex Transforms = new("((?:gradient|pattern)?[tT]ransform=[\"'])([^\"']*)", RegexOptions.CultureInvariant);
    private static readonly Regex GluedNumbers = new(@"(?<=[0-9.])-", RegexOptions.CultureInvariant);

    /// <summary>
    /// « translate(-384.57-515.8) » est valide en SVG, mais Svg.Skia le lit mal et dessine l'icône hors de l'image.
    /// Les thèmes écrivent souvent ainsi : sans cet espace, seules 119 des 513 icônes de fichiers de Breeze s'affichent.
    /// </summary>
    public static string SeparateTransformNumbers(string svg) =>
        Transforms.Replace(svg, m => m.Groups[1].Value + GluedNumbers.Replace(m.Groups[2].Value, " -"));
}

/// <summary>Type des fichiers d'après leur nom, avec la base commune aux bureaux Linux (shared-mime-info).</summary>
public sealed class LinuxFileTypes
{
    private readonly Dictionary<string, string> _suffixes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _generic = new(StringComparer.Ordinal);

    /// <param name="globs">Lignes de globs2 : « poids:type:motif ». Le fichier est trié du poids le plus fort au plus faible.</param>
    /// <param name="genericIcons">Lignes de generic-icons : « type:icône ».</param>
    public LinuxFileTypes(IEnumerable<string> globs, IEnumerable<string> genericIcons)
    {
        foreach (var line in globs)
        {
            if (line.StartsWith('#')) continue;
            var parts = line.Split(':');
            if (parts.Length < 3) continue;
            var (type, pattern) = (parts[1], parts[2]);
            if (pattern.StartsWith("*.", StringComparison.Ordinal) && pattern.IndexOfAny(['*', '?', '['], 1) < 0) _suffixes.TryAdd(pattern[1..], type);
            else if (pattern.IndexOfAny(['*', '?', '[']) < 0) _names.TryAdd(pattern, type);
        }
        foreach (var line in genericIcons)
        {
            int colon = line.IndexOf(':');
            if (colon > 0 && !line.StartsWith('#')) _generic[line[..colon]] = line[(colon + 1)..].Trim();
        }
    }

    public static LinuxFileTypes ForThisSystem()
    {
        var globs = new List<string>();
        var generic = new List<string>();
        foreach (var folder in DataFolders().Select(f => Path.Combine(f, "mime")))
        {
            ReadInto(globs, Path.Combine(folder, "globs2"));
            ReadInto(generic, Path.Combine(folder, "generic-icons"));
        }
        return new LinuxFileTypes(globs, generic);
    }

    private static void ReadInto(List<string> lines, string file)
    {
        try { if (File.Exists(file)) lines.AddRange(File.ReadLines(file)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Dossiers de données : celui de l'utilisateur d'abord, comme le prévoit la norme.</summary>
    internal static IEnumerable<string> DataFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var own = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        yield return string.IsNullOrEmpty(own) ? Path.Combine(home, ".local", "share") : own;
        var shared = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        foreach (var folder in (string.IsNullOrEmpty(shared) ? "/usr/local/share:/usr/share" : shared).Split(':', StringSplitOptions.RemoveEmptyEntries))
            yield return folder;
    }

    /// <summary>Le suffixe le plus long gagne : « a.tar.gz » est une archive tar compressée, pas un simple fichier gz.</summary>
    public string? TypeOf(string fileName)
    {
        if (_names.TryGetValue(fileName, out var exact)) return exact;
        for (int dot = fileName.IndexOf('.'); dot >= 0; dot = fileName.IndexOf('.', dot + 1))
            if (_suffixes.TryGetValue(fileName[dot..], out var type)) return type;
        return null;
    }

    /// <summary>Noms d'icônes à essayer pour ce fichier, du plus précis au plus général.</summary>
    public IEnumerable<string> IconNames(string fileName)
    {
        if (TypeOf(fileName) is { } type)
        {
            yield return type.Replace('/', '-');
            if (_generic.TryGetValue(type, out var generic)) yield return generic;
            yield return type[..type.IndexOf('/')] + "-x-generic";
        }
        yield return "unknown";
        yield return "text-x-generic";
    }
}

/// <summary>Thème d'icônes du bureau (norme freedesktop.org), avec les thèmes dont il hérite.</summary>
public sealed class LinuxIconTheme
{
    private const string Fallback = "hicolor";
    private static readonly string[] Contexts = ["MimeTypes", "Places"];
    private static readonly string[] Extensions = [".svg", ".png"];

    /// <summary>Dossier d'icônes. Min / Max : tailles couvertes par un dossier « Scalable » (SVG, Adwaita), sinon Size.</summary>
    private sealed record Folder(string Path, int Size, int Min, int Max)
    {
        public int Distance(int size) => size < Min ? Min - size : size > Max ? size - Max : 0;
    }

    private readonly List<(string Root, List<Folder> Folders)> _themes = [];
    private readonly bool _found;

    /// <param name="name">Nom du thème.</param>
    /// <param name="bases">Dossiers qui contiennent les thèmes, le plus prioritaire d'abord.</param>
    public LinuxIconTheme(string name, IReadOnlyList<string> bases)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Queue<string>([name]);
        bool fallbackDone = false;
        while (waiting.Count > 0 || !fallbackDone)
        {
            if (waiting.Count == 0)
            {
                fallbackDone = true;
                waiting.Enqueue(Fallback);
            }
            var theme = waiting.Dequeue();
            if (!seen.Add(theme)) continue;
            foreach (var root in bases.Select(b => Path.Combine(b, theme)))
            {
                var index = Path.Combine(root, "index.theme");
                if (!File.Exists(index)) continue;
                var sections = ReadIndex(index);
                if (!sections.TryGetValue("Icon Theme", out var head)) continue;
                _themes.Add((root, FoldersOf(head, sections)));
                if (theme == name) _found = true;
                foreach (var parent in List(head, "Inherits")) waiting.Enqueue(parent);
                break;
            }
        }
    }

    /// <summary>
    /// Le thème demandé existe-t-il, avec de quoi afficher quelque chose ? hicolor, le secours de tous les thèmes,
    /// ne compte pas : sous GNOME, Breeze absent ne laissait que lui, presque vide, et Adwaita n'était jamais essayé.
    /// </summary>
    public bool IsUsable => _found && _themes.Any(t => t.Folders.Count > 0);

    /// <summary>Thème choisi dans le bureau (KDE ou GNOME), sinon Breeze, sinon Adwaita. Null s'il n'y a aucun thème.</summary>
    public static LinuxIconTheme? ForThisUser()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> bases = [Path.Combine(home, ".icons"), .. LinuxFileTypes.DataFolders().Select(f => Path.Combine(f, "icons")), "/usr/share/pixmaps"];
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var settings = Path.Combine(string.IsNullOrEmpty(config) ? Path.Combine(home, ".config") : config, "kdeglobals");
        var kde = File.Exists(settings) && ReadIndex(settings).TryGetValue("Icons", out var icons) ? icons.GetValueOrDefault("Theme") : null;
        var gnome = GnomeThemeName(RunGsettings());
        // Un fichier kdeglobals peut rester d'une ancienne session KDE : le bureau en cours décide de l'ordre.
        bool onKde = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("KDE", StringComparison.OrdinalIgnoreCase);
        foreach (var name in onKde ? new[] { kde, gnome, "breeze", "Adwaita" } : [gnome, kde, "Adwaita", "breeze"])
        {
            if (string.IsNullOrEmpty(name)) continue;
            var theme = new LinuxIconTheme(name, bases);
            if (theme.IsUsable) return theme;
        }
        return null;
    }

    /// <summary>Fichier de la première icône trouvée, à la taille la plus proche. Pour chaque nom, le thème passe avant ceux dont il hérite.</summary>
    public string? Find(IEnumerable<string> names, int size)
    {
        foreach (var name in names)
            foreach (var (root, folders) in _themes)
                foreach (var folder in folders.OrderBy(f => f.Distance(size)))
                    foreach (var extension in Extensions)
                    {
                        var file = Path.Combine(root, folder.Path, name + extension);
                        if (File.Exists(file)) return file;
                    }
        return null;
    }

    /// <summary>Réponse de « gsettings get org.gnome.desktop.interface icon-theme » : 'Adwaita' entre apostrophes.</summary>
    public static string? GnomeThemeName(string? output)
    {
        var text = output?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'') text = text[1..^1];
        return text.Length == 0 || text.Contains('/') || text.Contains("..", StringComparison.Ordinal) ? null : text;
    }

    private static string? RunGsettings()
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("gsettings") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "get", "org.gnome.desktop.interface", "icon-theme" }) start.ArgumentList.Add(a);
            using var process = System.Diagnostics.Process.Start(start);
            if (process is null) return null;
            var read = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(2000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
            return process.ExitCode == 0 ? read.Result : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
    }

    private static List<Folder> FoldersOf(Dictionary<string, string> head, Dictionary<string, Dictionary<string, string>> sections)
    {
        var folders = new List<Folder>();
        foreach (var path in List(head, "Directories").Concat(List(head, "ScaledDirectories")))
        {
            if (!sections.TryGetValue(path, out var section) || !Contexts.Contains(section.GetValueOrDefault("Context"))) continue;
            if (section.GetValueOrDefault("Scale", "1") != "1") continue;
            if (!int.TryParse(section.GetValueOrDefault("Size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int size)) continue;
            int min = size, max = size;
            if (section.GetValueOrDefault("Type") == "Scalable")
            {
                min = Number(section, "MinSize") ?? size;
                max = Number(section, "MaxSize") ?? size;
            }
            folders.Add(new Folder(path, size, min, max));
        }
        return folders;
    }

    private static int? Number(Dictionary<string, string> section, string key) =>
        int.TryParse(section.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;

    private static string[] List(Dictionary<string, string> section, string key) =>
        section.GetValueOrDefault(key, string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Fichier de type .ini : sections et clés.</summary>
    private static Dictionary<string, Dictionary<string, string>> ReadIndex(string file)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        Dictionary<string, string>? current = null;
        try
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    current = new Dictionary<string, string>(StringComparer.Ordinal);
                    sections[line[1..^1]] = current;
                }
                else if (current is not null && line.IndexOf('=') is > 0 and var equal)
                    current[line[..equal].Trim()] = line[(equal + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return sections;
    }
}
