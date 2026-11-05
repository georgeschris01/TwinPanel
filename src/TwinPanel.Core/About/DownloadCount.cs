// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text.Json;

namespace TwinPanel.Core.About;

/// <summary>
/// « À propos » : total des téléchargements de TwinPanel pour le système utilisé (toutes versions), lu sur les pages
/// publiques des Releases GitHub. Rien n'est envoyé : simple lecture, sans compte. Les installations par winget
/// passent par ces fichiers et sont comptées.
/// </summary>
public static class DownloadCount
{
    public const string Repository = "georgeschris01/TwinPanel";

    /// <summary>100 Releases par page (le plus que GitHub permet) ; 10 pages au plus.</summary>
    private const int PerPage = 100, MaxPages = 10;

    public static Uri ApiUri(int page) => new($"https://api.github.com/repos/{Repository}/releases?per_page={PerPage}&page={page}");

    /// <summary>Fichier du système : installateur « .exe » pour Windows ; « .deb », « .rpm », AppImage pour Linux.</summary>
    public static bool IsForSystem(string fileName, bool windows) => windows
        ? fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        : fileName.EndsWith(".deb", StringComparison.OrdinalIgnoreCase)
          || fileName.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase)
          || fileName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);

    /// <summary>Une page de la liste des Releases : total des fichiers du système, et nombre de Releases lues. null : réponse inattendue.</summary>
    public static (long Total, int Releases)? ParsePage(string json, bool windows)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            long total = 0;
            int releases = 0;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                releases++;
                if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
                foreach (var asset in assets.EnumerateArray())
                    if (asset.TryGetProperty("name", out var name) && name.GetString() is { } n && IsForSystem(n, windows)
                        && asset.TryGetProperty("download_count", out var count) && count.TryGetInt64(out var c))
                        total += c;
            }
            return (total, releases);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Total pour ce système ; null sans Internet ou si GitHub ne répond pas (5 secondes au plus par page).</summary>
    public static async Task<long?> FetchTotalAsync(bool windows, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            // GitHub demande un nom de programme ; rien d'autre n'est envoyé.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TwinPanel");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            long total = 0;
            for (int page = 1; page <= MaxPages; page++)
            {
                using var response = await http.GetAsync(ApiUri(page), ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (ParsePage(json, windows) is not { } part) return null;
                total += part.Total;
                if (part.Releases < PerPage) break; // dernière page
            }
            return total;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return null;
        }
    }
}
