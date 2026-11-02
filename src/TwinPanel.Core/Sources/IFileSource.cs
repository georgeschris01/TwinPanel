// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Sources;

/// <summary>
/// Une source de fichiers affichable dans un panneau : disque local aujourd'hui,
/// archives, FTP/SFTP et plugins Total Commander (WFX/WCX) plus tard.
/// </summary>
public interface IFileSource
{
    /// <summary>Identifiant court ("local", "zip", "wfx:sftp"…).</summary>
    string Id { get; }

    /// <summary>Vrai si cette source sait afficher ce chemin.</summary>
    bool CanHandle(string path);

    /// <summary>Liste le contenu du dossier (avec l'entrée "..", non triée).</summary>
    Task<IReadOnlyList<FileEntry>> ListAsync(string path, bool showHidden, CancellationToken ct);

    /// <summary>Chemin canonique (absolu, normalisé) pour comparer et afficher.</summary>
    string Normalize(string path);

    /// <summary>Dossier parent, ou null à la racine.</summary>
    string? GetParent(string path);

    /// <summary>Vrai si la source signale elle-même les changements (surveillance).</summary>
    bool SupportsWatching { get; }
}
