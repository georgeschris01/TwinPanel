// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text.Json;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Navigation;

/// <summary>
/// Derniers dossiers visités, tous panneaux confondus, du plus récent au plus ancien.
/// Un dossier du PC est gardé tel quel ; un dossier d'un serveur sous la forme « remote:{fiche}|{chemin} »
/// (voir <see cref="SavedRemoteTab"/>), sans aucun identifiant ni mot de passe.
/// Fichier à part (history.json) : l'historique ne part pas dans les sauvegardes de réglages.
/// </summary>
public sealed class RecentFolders(string filePath, int capacity = RecentFolders.DefaultCapacity)
{
    public const int DefaultCapacity = 30;

    private readonly List<string> _items = [];
    private int _capacity = capacity;

    /// <summary>Nombre de dossiers retenus (Options) ; réduit, les plus anciens sont oubliés.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(1, value);
            if (_items.Count > _capacity) _items.RemoveRange(_capacity, _items.Count - _capacity);
        }
    }

    public string FilePath { get; } = filePath;
    public IReadOnlyList<string> Items => _items;

    /// <summary>Met le dossier en tête de liste (sans doublon) ; les plus anciens au-delà de la limite sont oubliés.</summary>
    public void Visit(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _items.RemoveAll(p => IsSame(p, path));
        _items.Insert(0, path);
        if (_items.Count > _capacity) _items.RemoveRange(_capacity, _items.Count - _capacity);
    }

    public void Clear() => _items.Clear();

    /// <summary>Le PC ne distingue pas les majuscules ; un serveur, si.</summary>
    public static bool IsSame(string a, string b) =>
        string.Equals(a, b, SavedRemoteTab.TryDecode(a, out _, out _) ? StringComparison.Ordinal : PathText.Comparison);

    /// <summary>Lit la liste ; fichier absent ou abîmé : liste vide (rien d'important n'est perdu).</summary>
    public void Load()
    {
        _items.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            var saved = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [];
            // Lecture à l'envers : Visit remet chaque dossier en tête, l'ordre d'origine est retrouvé.
            foreach (var path in Enumerable.Reverse(saved)) Visit(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { _items.Clear(); }
    }

    /// <summary>Écriture atomique, comme les réglages.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
