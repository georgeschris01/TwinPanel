// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Search;

/// <summary>Plusieurs dossiers de départ dans un seul champ : « C:\;D:\;sftp://chrypox/home ».</summary>
public static class SearchRoots
{
    /// <summary>Sépare par « ; » (les espaces autour et les guillemets sont enlevés, les doublons aussi).</summary>
    public static IReadOnlyList<string> Split(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.Trim('"').Trim()).Where(r => r.Length > 0)
            .Distinct(PathText.Comparer).ToList();

    /// <summary>Tous les disques prêts (C:\;D:\;…).</summary>
    public static string AllDrives()
    {
        // Sous Linux, partir de « / » parcourrait aussi /proc et /sys, qui ne sont pas des fichiers.
        if (OperatingSystem.IsLinux())
            return string.Join(';', LocalDisks.Usable().Select(d => d.Name).Where(n => n != "/").Prepend(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        var roots = new List<string>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Network) roots.Add(d.Name); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return string.Join(';', roots);
    }
}
