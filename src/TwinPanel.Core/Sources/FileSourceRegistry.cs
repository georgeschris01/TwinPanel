// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Sources;

/// <summary>Choisit la source capable d'afficher un chemin. Les plugins s'enregistreront ici.</summary>
public sealed class FileSourceRegistry
{
    private readonly List<IFileSource> _sources = [];

    /// <summary>Disque local + archives (les archives sont testées en premier).</summary>
    public static FileSourceRegistry Default { get; } = new FileSourceRegistry()
        .Register(new LocalFileSource())
        .Register(new Archives.ArchiveFileSource());

    /// <summary>Les sources enregistrées en dernier sont prioritaires (un plugin passe avant le disque local).</summary>
    public FileSourceRegistry Register(IFileSource source)
    {
        _sources.Insert(0, source);
        return this;
    }

    public IFileSource Resolve(string path) =>
        _sources.FirstOrDefault(s => s.CanHandle(path))
        ?? throw new NotSupportedException(L.F("Aucune source ne sait ouvrir : {0}", path));
}
