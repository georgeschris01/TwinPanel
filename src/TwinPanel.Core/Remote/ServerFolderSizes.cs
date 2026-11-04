// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Serveur capable de mesurer lui-même un dossier et ses sous-dossiers (« du -b -d N ») : beaucoup plus rapide
/// qu'une lecture de toute l'arborescence par le réseau.
/// </summary>
public interface IServerFolderSizes
{
    /// <summary>
    /// Taille (octets) du dossier et de ses sous-dossiers jusqu'à <paramref name="depth"/> niveaux, clé : chemin du serveur.
    /// Null si le serveur ne sait pas répondre (pas de commandes, pas de « du -b »).
    /// Une partie illisible (droits) est simplement comptée pour 0.
    /// </summary>
    Task<IReadOnlyDictionary<string, long>?> MeasureTreeOnServerAsync(string path, int depth, CancellationToken ct);
}

public static class ServerFolderSizes
{
    /// <summary>Commande « du » : tailles réelles en octets, sans suivre les liens, jusqu'à la profondeur voulue.</summary>
    public static string Command(string quotedPath, int depth) =>
        string.Create(CultureInfo.InvariantCulture, $"du -b -d {Math.Clamp(depth, 0, 8)} -- {quotedPath} 2>/dev/null");

    /// <summary>
    /// Lit la sortie de « du -b » (« taille », tabulation, chemin, une ligne par dossier).
    /// Rend null si la ligne du dossier demandé manque (commande refusée, « du » sans -b…).
    /// </summary>
    public static IReadOnlyDictionary<string, long>? Parse(string? output, string root)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var raw in (output ?? string.Empty).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (!long.TryParse(line.AsSpan(0, tab), NumberStyles.None, CultureInfo.InvariantCulture, out var size)) continue;
            var path = Normalize(line[(tab + 1)..]);
            if (path.Length > 0) result[path] = size;
        }
        return result.ContainsKey(Normalize(root)) ? result : null;
    }

    /// <summary>« /media//x/ » → « /media/x » (« / » reste « / »).</summary>
    public static string Normalize(string path)
    {
        while (path.Contains("//", StringComparison.Ordinal)) path = path.Replace("//", "/", StringComparison.Ordinal);
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
