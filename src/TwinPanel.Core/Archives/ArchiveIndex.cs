// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.Concurrent;
using SharpCompress.Common;

namespace TwinPanel.Core.Archives;

/// <summary>Une entrée d'archive (fichier ou dossier, éventuellement implicite).</summary>
public sealed record ArchiveItem(string Key, bool IsDirectory, long Size, DateTime Modified);

/// <summary>
/// Contenu d'une archive, lu une fois puis gardé en mémoire tant que le fichier ne change pas
/// (taille + date). Les dossiers absents de l'archive mais sous-entendus par un chemin sont ajoutés.
/// </summary>
public sealed class ArchiveIndex
{
    private static readonly ConcurrentDictionary<string, (long Length, DateTime Stamp, ArchiveIndex Index)> Cache =
        new(PathText.Comparer);

    private readonly Dictionary<string, ArchiveItem> _items;
    private readonly Dictionary<string, List<ArchiveItem>> _children;

    private ArchiveIndex(IEnumerable<ArchiveItem> items, bool isEncrypted)
    {
        IsEncrypted = isEncrypted;
        _items = new Dictionary<string, ArchiveItem>(StringComparer.Ordinal);
        foreach (var item in items) _items[item.Key] = item;

        // Dossiers implicites : « a/b/c.txt » sous-entend « a » et « a/b ».
        foreach (var key in _items.Keys.ToList())
        {
            var parent = ParentOf(key);
            while (parent.Length > 0 && !_items.ContainsKey(parent))
            {
                _items[parent] = new ArchiveItem(parent, true, 0, _items[key].Modified);
                parent = ParentOf(parent);
            }
        }

        _children = _items.Values.GroupBy(i => ParentOf(i.Key)).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ArchiveItem> All => _items.Values;

    /// <summary>Au moins un fichier est chiffré : il faudra un mot de passe pour le lire.</summary>
    public bool IsEncrypted { get; }

    public bool DirectoryExists(string inner) => inner.Length == 0 || (_items.TryGetValue(inner, out var i) && i.IsDirectory);

    public IReadOnlyList<ArchiveItem> ChildrenOf(string inner) =>
        _children.TryGetValue(inner, out var list) ? list : [];

    /// <summary>Éléments sélectionnés : chaque chemin donné, et tout ce qu'il contient si c'est un dossier.</summary>
    public IEnumerable<ArchiveItem> Expand(IEnumerable<string> keys)
    {
        var set = keys.Select(ArchivePath.NormalizeKey).ToList();
        if (set.Any(k => k.Length == 0)) return _items.Values; // racine = tout
        return _items.Values.Where(i => set.Any(k => i.Key == k || i.Key.StartsWith(k + "/", StringComparison.Ordinal)));
    }

    public static string ParentOf(string key)
    {
        int slash = key.LastIndexOf('/');
        return slash < 0 ? string.Empty : key[..slash];
    }

    /// <summary>Oublie l'index gardé de cette archive (elle vient d'être réécrite).</summary>
    public static void Forget(string archivePath) => Cache.TryRemove(Path.GetFullPath(archivePath), out _);

    /// <summary>Lit (ou reprend du cache) l'index d'une archive.</summary>
    /// <exception cref="IOException">Archive illisible ou endommagée.</exception>
    /// <exception cref="ArchivePasswordException">Le mot de passe manque, jusque pour lire les noms (7z à noms cachés).</exception>
    public static ArchiveIndex Load(string archivePath)
    {
        var info = new FileInfo(archivePath);
        if (Cache.TryGetValue(info.FullName, out var cached) && cached.Length == info.Length && cached.Stamp == info.LastWriteTimeUtc)
            return cached.Index;

        var index = ArchiveErrors.Wrap(archivePath, () =>
        {
            var items = new List<ArchiveItem>();
            bool encrypted = false;
            foreach (var e in ArchiveReading.Read(info.FullName))
            {
                var key = ArchivePath.NormalizeKey(e.Key);
                if (key.Length == 0) continue;
                encrypted |= e.Encrypted && !e.IsDirectory;
                items.Add(new ArchiveItem(key, e.IsDirectory, e.Size, e.Modified));
            }
            return new ArchiveIndex(items, encrypted);
        });

        Cache[info.FullName] = (info.Length, info.LastWriteTimeUtc, index);
        if (Cache.Count > 20) Cache.Clear(); // garde-fou mémoire très simple
        return index;
    }

    /// <summary>Un .gz seul (pas un .tar.gz) contient un fichier sans nom : on reprend le nom de l'archive sans « .gz ».</summary>
    internal static string SingleFileName(FileInfo archive) => Path.GetFileNameWithoutExtension(archive.Name);
}

/// <summary>Traduit les erreurs de la bibliothèque d'archives en messages clairs (IOException).</summary>
internal static class ArchiveErrors
{
    public static T Wrap<T>(string archive, Func<T> action)
    {
        try { return action(); }
        catch (Exception ex) when (IsPassword(ex) || (IsUnreadable(ex) && ArchivePasswords.Get(archive) is not null))
        {
            // Un 7z lu avec un faux mot de passe donne n'importe quelle erreur de décodage : rien ne le distingue d'une archive abîmée.
            bool wrong = ArchivePasswords.Get(archive) is not null;
            ArchivePasswords.Forget(archive);
            throw new ArchivePasswordException(archive, wrong, ex);
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            throw new IOException(L.F("« {0} » est illisible ou endommagée ({1}).", Path.GetFileName(archive), ex.Message), ex);
        }
    }

    public static void Wrap(string archive, Action action) => Wrap<bool>(archive, () => { action(); return true; });

    /// <summary>Erreurs de décodage ; celle des données fausses est un type privé de la bibliothèque.</summary>
    public static bool IsUnreadable(Exception ex) =>
        ex is SharpCompressException or InvalidFormatException or ArchiveException or InvalidOperationException
            or ICSharpCode.SharpZipLib.SharpZipBaseException or InvalidDataException or FormatException
            or NotSupportedException or IndexOutOfRangeException or ArgumentException or EndOfStreamException
        || ex.GetType().Name == "DataErrorException";

    public static bool IsPassword(Exception ex) =>
        ex is CryptographicException || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase);
}
