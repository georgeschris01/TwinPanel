// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.Concurrent;

namespace TwinPanel.Core.Media;

/// <summary>Résultats MediaInfo gardés en mémoire (colonnes des panneaux) : un fichier n'est relu que s'il a changé.</summary>
public static class MediaCache
{
    private static readonly ConcurrentDictionary<string, (long Size, DateTime Date, MediaReport? Report)> Cache =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static bool TryPeek(string path, long size, DateTime date, out MediaReport? report)
    {
        report = null;
        if (!Cache.TryGetValue(path, out var c) || c.Size != size || c.Date != date) return false;
        report = c.Report;
        return true;
    }

    public static MediaReport? Get(string path, long size, DateTime date)
    {
        if (TryPeek(path, size, date, out var cached)) return cached;
        MediaReport? report;
        try { report = MediaAnalyzer.Analyze(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException) { report = null; }
        if (Cache.Count > 20_000) Cache.Clear();
        Cache[path] = (size, date, report);
        return report;
    }
}
