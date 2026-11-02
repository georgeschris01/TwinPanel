// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Navigation;

/// <summary>Historique précédent / suivant d'un onglet (comme un navigateur web).</summary>
public sealed class NavigationHistory(int capacity = 100)
{
    private readonly List<string> _items = [];
    private int _index = -1;

    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index >= 0 && _index < _items.Count - 1;
    public string? Current => _index >= 0 ? _items[_index] : null;
    public IReadOnlyList<string> Items => _items;

    /// <summary>Enregistre une nouvelle visite ; efface la branche "suivant".</summary>
    public void Push(string path)
    {
        if (Current is { } cur && string.Equals(cur, path, StringComparison.OrdinalIgnoreCase)) return;
        if (_index < _items.Count - 1) _items.RemoveRange(_index + 1, _items.Count - _index - 1);
        _items.Add(path);
        if (_items.Count > capacity) _items.RemoveAt(0);
        _index = _items.Count - 1;
    }

    public string? Back() => CanGoBack ? _items[--_index] : null;
    public string? Forward() => CanGoForward ? _items[++_index] : null;

    /// <summary>Annule le dernier Back/Forward si la navigation a échoué.</summary>
    public void Undo(bool wasBack)
    {
        if (wasBack && _index < _items.Count - 1) _index++;
        else if (!wasBack && _index > 0) _index--;
    }
}
