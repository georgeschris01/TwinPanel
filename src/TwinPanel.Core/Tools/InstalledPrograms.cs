// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace TwinPanel.Core.Tools;

/// <summary>Un programme installé trouvé sur le PC.</summary>
public sealed record InstalledProgram(string Name, string Path, bool IsKnownEditor = false)
{
    public override string ToString() => Name;
}

/// <summary>
/// Programmes installés, pour les proposer avant d'ouvrir « Parcourir » : registre « App Paths » (programmes
/// déclarés à Windows), registre « Désinstaller » (icône du programme), éditeurs connus détectés par TwinPanel.
/// Lecture seule ; rien n'est modifié.
/// </summary>
public static class InstalledPrograms
{
    private static IReadOnlyList<InstalledProgram>? _cache;

    public static IReadOnlyList<InstalledProgram> Get(bool refresh = false)
    {
        if (_cache is not null && !refresh) return _cache;
        var list = new List<InstalledProgram>();
        foreach (var e in ExternalTools.DetectEditors()) list.Add(new InstalledProgram(e.Name, e.Path, IsKnownEditor: true));
        if (OperatingSystem.IsWindows())
        {
            try { list.AddRange(FromRegistry()); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
        _cache = list
            .Where(p => p.Path.Length > 0 && File.Exists(p.Path) && !LooksLikeUninstaller(p.Path))
            .GroupBy(p => System.IO.Path.GetFullPath(p.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.IsKnownEditor).ThenByDescending(p => p.Name.Length).First())
            .OrderByDescending(p => p.IsKnownEditor).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return _cache;
    }

    /// <summary>Filtre par nom ou chemin (texte tapé dans la recherche).</summary>
    public static IEnumerable<InstalledProgram> Filter(IEnumerable<InstalledProgram> all, string? text)
    {
        var t = (text ?? string.Empty).Trim();
        return t.Length == 0 ? all : all.Where(p => p.Name.Contains(t, StringComparison.CurrentCultureIgnoreCase) || p.Path.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksLikeUninstaller(string path)
    {
        var f = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        return f.StartsWith("unins", StringComparison.Ordinal) || f.Contains("uninstall") || f.Contains("désinstall") || f is "setup" or "install" or "update" or "updater";
    }

    /// <summary>« "C:\…\app.exe",0 » → « C:\…\app.exe » (null si ce n'est pas un .exe).</summary>
    public static string? ExeFromIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var s = icon.Trim();
        int comma = s.LastIndexOf(',');
        if (comma > 0 && int.TryParse(s[(comma + 1)..].Trim(), out _)) s = s[..comma];
        s = Environment.ExpandEnvironmentVariables(s.Trim().Trim('"'));
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s : null;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<InstalledProgram> FromRegistry()
    {
        const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        const string uninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = root.OpenSubKey(appPaths);
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub?.GetValue(null) is not string raw) continue;
                var path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                yield return new InstalledProgram(Describe(path), path);
            }
        }

        foreach (var (root, keyPath) in new[] { (Registry.LocalMachine, uninstall), (Registry.LocalMachine, uninstall32), (Registry.CurrentUser, uninstall) })
        {
            using var key = root.OpenSubKey(keyPath);
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name);
                if (sub is null || sub.GetValue("SystemComponent") is int and 1) continue;
                if (sub.GetValue("DisplayName") is not string display || display.Length == 0) continue;
                if (ExeFromIcon(sub.GetValue("DisplayIcon") as string) is { } exe) yield return new InstalledProgram(display, exe);
            }
        }
    }

    /// <summary>Nom lisible : description du programme, sinon son nom de fichier.</summary>
    private static string Describe(string path)
    {
        try
        {
            if (File.Exists(path) && FileVersionInfo.GetVersionInfo(path) is { } v)
            {
                var d = (v.FileDescription ?? string.Empty).Trim();
                if (d.Length > 0) return d;
                var p = (v.ProductName ?? string.Empty).Trim();
                if (p.Length > 0) return p;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException) { }
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }
}
