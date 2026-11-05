// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Navigation;

/// <summary>Place du curseur (la barre de couleur) après une copie ou un déplacement.</summary>
public static class TransferCursor
{
    /// <summary>Même dossier affiché ? « C:\Photos\ » = « c:\photos » sous Windows ; « sftp://serveur/home/ » = « sftp://serveur/home ».</summary>
    public static bool SameFolder(string panelPath, string folder)
    {
        bool remoteA = PanelRemotePath.TryParse(panelPath, out var a), remoteB = PanelRemotePath.TryParse(folder, out var b);
        if (remoteA || remoteB) return remoteA && remoteB && a == b;
        return string.Equals(Trim(panelPath), Trim(folder), PathText.Comparison);
    }

    private static string Trim(string path)
    {
        var t = path.TrimEnd('\\', '/');
        return t.Length == 0 ? path : t;
    }

    /// <summary>
    /// La ligne sous le curseur a disparu (un ou plusieurs éléments déplacés ou supprimés) : le curseur va sur
    /// la première ligne restante qui la suivait, sinon sur la dernière restante avant elle. null : plus rien.
    /// </summary>
    /// <param name="oldKeys">Les lignes avant la relecture, dans l'ordre.</param>
    /// <param name="oldIndex">Rang de la ligne du curseur avant la relecture.</param>
    public static string? NextRemaining(IReadOnlyList<string> oldKeys, int oldIndex, Func<string, bool> stillThere)
    {
        for (int i = Math.Max(0, oldIndex); i < oldKeys.Count; i++)
            if (stillThere(oldKeys[i])) return oldKeys[i];
        for (int i = Math.Min(oldIndex, oldKeys.Count) - 1; i >= 0; i--)
            if (stillThere(oldKeys[i])) return oldKeys[i];
        return null;
    }

    /// <summary>Parmi les éléments arrivés, le premier dans l'ordre de la liste (null : aucun n'est affiché).</summary>
    public static string? FirstArrived(IEnumerable<string> namesInListOrder, IEnumerable<string> arrived, bool remote)
    {
        var wanted = new HashSet<string>(arrived, remote ? StringComparer.Ordinal : PathText.Comparer);
        return namesInListOrder.FirstOrDefault(wanted.Contains);
    }
}
