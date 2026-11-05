// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core;

/// <summary>
/// Décide quels transferts tournent : un nombre limité à la fois, dans l'ordre d'arrivée.
/// Une clé (le chemin d'une archive) interdit à deux opérations sur la même chose de tourner ensemble.
/// </summary>
public sealed class TransferGate<T> where T : class
{
    public const int MinLimit = 1;
    public const int MaxLimit = 8;

    private readonly object _lock = new();
    private readonly List<(T Item, string? Key)> _waiting = [];
    private readonly List<(T Item, string? Key)> _running = [];
    private int _limit = MinLimit;

    public int Limit
    {
        get { lock (_lock) return _limit; }
    }

    /// <summary>Change la limite sans interrompre ce qui tourne. Renvoie ce qui peut démarrer.</summary>
    public IReadOnlyList<T> SetLimit(int limit)
    {
        lock (_lock)
        {
            _limit = Math.Clamp(limit, MinLimit, MaxLimit);
            return Admit();
        }
    }

    /// <summary>Présente un transfert. Vrai : il peut démarrer ; faux : il attend son tour.</summary>
    public bool Enter(T item, string? key = null)
    {
        lock (_lock)
        {
            _waiting.Add((item, key));
            return Admit().Contains(item);
        }
    }

    /// <summary>Un transfert est fini. Renvoie ce qui peut démarrer à sa place.</summary>
    public IReadOnlyList<T> Leave(T item)
    {
        lock (_lock)
        {
            _running.RemoveAll(r => ReferenceEquals(r.Item, item));
            return Admit();
        }
    }

    /// <summary>Retire un transfert qui attendait. Faux s'il n'attendait pas.</summary>
    public bool Forget(T item)
    {
        lock (_lock) return _waiting.RemoveAll(w => ReferenceEquals(w.Item, item)) > 0;
    }

    private List<T> Admit()
    {
        var admitted = new List<T>();
        for (int i = 0; i < _waiting.Count && _running.Count < _limit;)
        {
            var next = _waiting[i];
            if (next.Key is not null && _running.Any(r => string.Equals(r.Key, next.Key, StringComparison.OrdinalIgnoreCase))) { i++; continue; }
            _waiting.RemoveAt(i);
            _running.Add(next);
            admitted.Add(next.Item);
        }
        return admitted;
    }
}

/// <summary>Les lignes de progression affichées en bas de la fenêtre.</summary>
public static class ProgressLines
{
    public const int Shown = 3;

    /// <summary>Les plus importantes d'abord (rang le plus petit) ; à rang égal, l'ordre d'arrivée.</summary>
    public static IReadOnlyList<T> Pick<T>(IEnumerable<T> items, Func<T, int> rank) => items.OrderBy(rank).Take(Shown).ToList();
}
