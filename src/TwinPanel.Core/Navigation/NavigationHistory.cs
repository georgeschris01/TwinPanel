// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
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
        if (Current is { } cur && string.Equals(cur, path, PathText.Comparison)) return;
        if (_index < _items.Count - 1) _items.RemoveRange(_index + 1, _items.Count - _index - 1);
        _items.Add(path);
        if (_items.Count > capacity) _items.RemoveAt(0);
        _index = _items.Count - 1;
    }

    // ---- Place du curseur au retour (Précédent / Suivant) ----

    private readonly Dictionary<string, string> _cursors = new(PathText.Comparer);

    /// <summary>On quitte ce dossier : on retient l'élément sous le curseur, pour le retrouver en y revenant.</summary>
    public void RememberCursor(string path, string? name)
    {
        if (path.Length == 0) return;
        if (string.IsNullOrEmpty(name) || name == "..") _cursors.Remove(path);
        else _cursors[path] = name;
        if (_cursors.Count > capacity * 2) _cursors.Clear(); // jamais sans fin
    }

    /// <summary>
    /// Élément à pointer en revenant dans « target » depuis « current » : celui qu'on y avait laissé ;
    /// sinon, si on était plus bas, le dossier par lequel on était descendu ; sinon null (haut de la liste).
    /// </summary>
    public string? CursorFor(string target, string current) =>
        _cursors.TryGetValue(target, out var name) ? name : ChildToward(target, current);

    /// <summary>« C:\A » et « C:\A\B\C » → « B » ; « sftp://s/home » et « sftp://s/home/u/x » → « u » ; sinon null.</summary>
    public static string? ChildToward(string parent, string current)
    {
        var p = parent.TrimEnd('\\', '/');
        var comparison = parent.Contains("://", StringComparison.Ordinal) ? StringComparison.Ordinal : PathText.Comparison;
        if (current.Length <= p.Length + 1 || !current.StartsWith(p, comparison) || current[p.Length] is not ('\\' or '/')) return null;
        var rest = current[(p.Length + 1)..];
        int sep = rest.IndexOfAny(['\\', '/']);
        var child = sep < 0 ? rest : rest[..sep];
        return child.Length > 0 ? child : null;
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
