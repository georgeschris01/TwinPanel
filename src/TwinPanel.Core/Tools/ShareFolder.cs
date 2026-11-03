// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Tools;

/// <summary>Réunit dans un même dossier temporaire des fichiers à partager venus de dossiers différents.</summary>
public static class ShareFolder
{
    /// <summary>
    /// Copie les fichiers dans un nouveau dossier temporaire et rend les copies, dans le même ordre.
    /// Deux fichiers de même nom : le second devient « nom (2).ext », rien n'est écrasé.
    /// </summary>
    public static List<string> Gather(IEnumerable<string> files, string? root = null)
    {
        var folder = Path.Combine(root ?? Path.Combine(Path.GetTempPath(), "TwinPanel"), "partage-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var copies = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file);
            var target = Path.Combine(folder, name + ext);
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{name} ({n}){ext}");
            File.Copy(file, target);
            copies.Add(target);
        }
        return copies;
    }
}
