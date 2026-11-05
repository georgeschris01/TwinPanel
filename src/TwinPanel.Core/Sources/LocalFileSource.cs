// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Sources;

/// <summary>Disques et dossiers locaux ou réseau (chemins Windows classiques / UNC).</summary>
public sealed class LocalFileSource : IFileSource
{
    public string Id => "local";
    public bool SupportsWatching => true;

    public bool CanHandle(string path) => Path.IsPathRooted(path);

    public Task<IReadOnlyList<FileEntry>> ListAsync(string path, bool showHidden, CancellationToken ct) =>
        // Task.Run : un partage réseau lent ne doit pas figer l'interface.
        Task.Run(() => DirectoryReader.Read(path, showHidden), ct);

    public string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        // "C:" seul désigne le dossier courant du lecteur : on le force en racine.
        if (full.Length == 2 && full[1] == ':') full += Path.DirectorySeparatorChar;
        return full.Length > 3 ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    public string? GetParent(string path) => Directory.GetParent(Normalize(path))?.FullName;
}
