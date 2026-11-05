// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Media;

/// <summary>Où se trouve le fichier d'une vignette.</summary>
public enum ThumbnailSourceKind { Local, Remote }

/// <summary>Quels fichiers ont droit à une vignette, et comment la retrouver.</summary>
public static class ThumbnailRules
{
    /// <summary>Sur un serveur, une image plus lourde reste une icône (il faudrait la télécharger entière).</summary>
    public const long DefaultMaxRemoteBytes = 20L * 1024 * 1024;

    /// <summary>PC : images et vidéos. Serveur : images seulement, sous la limite. Intérieur d'une archive : aucune.</summary>
    public static bool CanHaveThumbnail(FileEntry entry, ThumbnailSourceKind kind, bool insideArchive, long maxRemoteBytes = DefaultMaxRemoteBytes)
    {
        if (entry.IsDirectory || insideArchive) return false;
        return kind == ThumbnailSourceKind.Local
            ? entry.Category is FileCategory.Image or FileCategory.Video
            : entry.Category == FileCategory.Image && entry.Size <= maxRemoteBytes;
    }

    /// <summary>La taille et la date en font partie : un fichier modifié retrouve une nouvelle vignette.</summary>
    public static string Key(FileEntry entry, int pixels) => $"{entry.FullPath}|{entry.Size}|{entry.LastWriteTime.Ticks}|{pixels}";
}

/// <summary>
/// Mémoire des vignettes, limitée en octets : les moins récemment utilisées sont retirées.
/// À n'utiliser que depuis un seul fil (celui de l'interface).
/// </summary>
public sealed class ThumbnailCache<T>(long maxBytes) where T : class
{
    private readonly record struct Slot(string Key, T Value, long Bytes);

    private readonly LinkedList<Slot> _order = new(); // en tête : la plus récemment utilisée
    private readonly Dictionary<string, LinkedListNode<Slot>> _map = new(StringComparer.Ordinal);

    public long UsedBytes { get; private set; }
    public int Count => _map.Count;

    public T? Get(string key)
    {
        if (!_map.TryGetValue(key, out var node)) return null;
        _order.Remove(node);
        _order.AddFirst(node);
        return node.Value.Value;
    }

    /// <summary>Ajoute une vignette ; rend celles retirées, pour que l'appelant les libère.</summary>
    public IReadOnlyList<T> Add(string key, T value, long bytes)
    {
        var removed = new List<T>();
        if (_map.TryGetValue(key, out var old)) Remove(old, removed);
        _map[key] = _order.AddFirst(new Slot(key, value, bytes));
        UsedBytes += bytes;
        // La plus récente reste toujours, même plus grosse que la limite.
        while (UsedBytes > maxBytes && _order.Count > 1) Remove(_order.Last!, removed);
        return removed;
    }

    private void Remove(LinkedListNode<Slot> node, List<T> removed)
    {
        _order.Remove(node);
        _map.Remove(node.Value.Key);
        UsedBytes -= node.Value.Bytes;
        removed.Add(node.Value.Value);
    }
}
