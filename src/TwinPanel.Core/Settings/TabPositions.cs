// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Settings;

/// <summary>Les onglets d'un serveur s'ouvrent après les autres : ils reprennent la place qu'ils avaient dans la liste.</summary>
public static class TabPositions
{
    /// <summary>
    /// Remet des onglets à leur rang d'origine, du premier au dernier, pour que chacun trouve ceux d'avant en place.
    /// Retourne le rang de l'onglet actif après ces insertions.
    /// </summary>
    public static int Merge(List<string> tabs, int active, IEnumerable<(int Position, string Tab)> pending)
    {
        foreach (var (position, tab) in pending.OrderBy(p => p.Position))
        {
            int at = Math.Clamp(position, 0, tabs.Count);
            tabs.Insert(at, tab);
            if (at <= active) active++;
        }
        return active;
    }
}
