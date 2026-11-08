// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Operations;

/// <summary>
/// Copies sur le PC des éléments d'un serveur ou d'une archive, pour les autres programmes (Explorateur, messagerie…) :
/// Windows et Linux ne savent coller ou recevoir par glisser-déposer que des fichiers présents sur le disque.
/// Chaque Copier / glisser a son propre dossier dans <see cref="Root"/>. Ménage : au Copier suivant, et au démarrage
/// pour celles de plus d'un jour (pas à la fermeture : un collage fait après avoir quitté TwinPanel doit encore marcher).
/// </summary>
public sealed class ClipboardStaging(string root)
{
    /// <summary>Au-delà, Copier demande d'abord ; le glisser-déposer renvoie vers Copier / Coller.</summary>
    public const long AskAboveBytes = 500L * 1024 * 1024;

    /// <summary>Copies plus anciennes : effacées au démarrage.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(1);

    public static ClipboardStaging Default { get; } = new(Path.Combine(Path.GetTempPath(), "TwinPanel", "presse-papiers"));

    public string Root { get; } = root;

    /// <summary>Nouveau dossier vide pour un Copier ou un glisser.</summary>
    public string NewFolder()
    {
        var dir = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Retire les copies (toutes, ou seulement celles plus anciennes que <paramref name="olderThan"/>), sauf <paramref name="keep"/>.</summary>
    /// <returns>Nombre de dossiers retirés. Un dossier encore ouvert par un programme reste pour la prochaine fois.</returns>
    public int CleanUp(TimeSpan? olderThan = null, string? keep = null, DateTime? now = null)
    {
        if (!Directory.Exists(Root)) return 0;
        var limit = (now ?? DateTime.Now) - (olderThan ?? TimeSpan.Zero);
        int removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            if (keep is not null && PathText.Comparer.Equals(Path.GetFullPath(dir), Path.GetFullPath(keep))) continue;
            try
            {
                if (olderThan is not null && Directory.GetCreationTime(dir) > limit) continue;
                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fichier encore ouvert (copie en cours dans un autre programme) : on réessaiera plus tard.
            }
        }
        return removed;
    }

    /// <summary>
    /// Où va chaque élément : le dossier donné, puis des sous-dossiers « 2 », « 3 »… quand des noms se répètent
    /// (vue à plat : deux « photo.jpg » de dossiers différents ne doivent pas s'écraser).
    /// </summary>
    public static IReadOnlyList<(string Folder, IReadOnlyList<T> Items)> Plan<T>(string folder, IEnumerable<T> items, Func<T, string> name)
    {
        var groups = Search.FlatListing.SplitUniqueNames(items, name);
        var plan = new List<(string, IReadOnlyList<T>)>(groups.Count);
        for (int i = 0; i < groups.Count; i++)
            plan.Add((i == 0 ? folder : Path.Combine(folder, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), groups[i]));
        return plan;
    }

    /// <summary>Chemins sur le PC des éléments, une fois copiés selon <see cref="Plan"/> (même ordre que le plan).</summary>
    public static IReadOnlyList<string> LocalPaths<T>(IReadOnlyList<(string Folder, IReadOnlyList<T> Items)> plan, Func<T, string> name) =>
        plan.SelectMany(g => g.Items.Select(i => Path.Combine(g.Folder, name(i)))).ToList();

    /// <summary>Taille connue d'avance : celle des fichiers (celle des dossiers n'est pas connue sans les parcourir).</summary>
    public static long KnownBytes(IEnumerable<FileEntry> entries) => entries.Where(e => !e.IsDirectory).Sum(e => Math.Max(0, e.Size));

    /// <summary>Vrai si Copier doit d'abord demander (gros volume à télécharger ou extraire).</summary>
    public static bool ShouldAsk(IEnumerable<FileEntry> entries) => KnownBytes(entries) > AskAboveBytes;
}
