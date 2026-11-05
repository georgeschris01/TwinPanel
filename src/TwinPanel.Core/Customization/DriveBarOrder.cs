// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Customization;

/// <summary>
/// Ordre des boutons déplaçables de la barre des lecteurs (lecteurs réseau, connexions), choisi en les glissant.
/// Une seule liste enregistrée pour les deux groupes ; chaque groupe se range à part (un lecteur réseau
/// ne passe pas parmi les connexions). Les boutons inconnus de la liste gardent leur ordre d'origine, à la fin.
/// </summary>
public static class DriveBarOrder
{
    /// <summary>Range les clés d'un groupe selon l'ordre enregistré.</summary>
    public static List<string> Arrange(IReadOnlyList<string> keys, IReadOnlyList<string> saved)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < saved.Count; i++) rank.TryAdd(saved[i], i);
        return keys
            .Select((k, i) => (Key: k, Rank: rank.TryGetValue(k, out var r) ? r : saved.Count + i))
            .OrderBy(x => x.Rank)
            .Select(x => x.Key)
            .ToList();
    }

    /// <summary>
    /// Déplace <paramref name="key"/> à la place <paramref name="index"/> (place d'avant le déplacement, 0 à Count)
    /// dans l'ordre affiché d'un groupe. Rend null si la clé n'est pas dans le groupe ou ne bouge pas.
    /// </summary>
    public static List<string>? Move(IReadOnlyList<string> group, string key, int index)
    {
        var list = group.ToList();
        int from = list.FindIndex(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        if (from < 0) return null;
        index = Math.Clamp(index, 0, list.Count);
        if (index > from) index--;
        if (index == from) return null;
        list.RemoveAt(from);
        list.Insert(index, key);
        return list;
    }

    /// <summary>Nouvelle liste à enregistrer : l'ordre du groupe déplacé, puis les clés des autres groupes, inchangées.</summary>
    public static List<string> Save(IReadOnlyList<string> saved, IReadOnlyList<string> group)
    {
        var inGroup = new HashSet<string>(group, StringComparer.OrdinalIgnoreCase);
        var result = group.ToList();
        foreach (var k in saved)
            if (!inGroup.Contains(k) && !result.Contains(k, StringComparer.OrdinalIgnoreCase)) result.Add(k);
        return result;
    }
}
