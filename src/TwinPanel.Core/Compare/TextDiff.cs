// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Compare;

public enum DiffKind { Same, Added, Removed, Changed }

/// <summary>Une ligne de l'affichage côte à côte (numéros à partir de 1).</summary>
public sealed record DiffLine(int? LeftNo, string? Left, int? RightNo, string? Right, DiffKind Kind);

/// <summary>Différences ligne par ligne (algorithme de Myers), présentées côte à côte.</summary>
public static class TextDiff
{
    /// <summary>Au-delà de ce nombre de différences, le reste est montré comme un seul bloc modifié.</summary>
    private const int MaxEdits = 4000;

    private enum Op { Same, Insert, Delete }

    public static IReadOnlyList<DiffLine> Diff(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        // Début et fin communs : traités directement (cas le plus fréquent, et beaucoup plus rapide).
        int prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix]) prefix++;
        int suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix]) suffix++;

        var ops = new List<Op>();
        for (int i = 0; i < prefix; i++) ops.Add(Op.Same);
        ops.AddRange(Myers(a, b, prefix, a.Count - suffix, prefix, b.Count - suffix));
        for (int i = 0; i < suffix; i++) ops.Add(Op.Same);
        return Pair(a, b, ops);
    }

    /// <summary>Script d'édition minimal entre a[a0..a1) et b[b0..b1).</summary>
    private static List<Op> Myers(IReadOnlyList<string> a, IReadOnlyList<string> b, int a0, int a1, int b0, int b1)
    {
        int n = a1 - a0, m = b1 - b0, max = n + m;
        var result = new List<Op>();
        if (n == 0) { for (int i = 0; i < m; i++) result.Add(Op.Insert); return result; }
        if (m == 0) { for (int i = 0; i < n; i++) result.Add(Op.Delete); return result; }

        int limit = Math.Min(max, MaxEdits);
        var v = new int[2 * limit + 3];
        int off = limit + 1;
        var trace = new List<int[]>();
        int found = -1;

        for (int d = 0; d <= limit && found < 0; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[off + k - 1] < v[off + k + 1]) ? v[off + k + 1] : v[off + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[a0 + x] == b[b0 + y]) { x++; y++; }
                v[off + k] = x;
                if (x >= n && y >= m) { found = d; break; }
            }
        }

        if (found < 0)
        {
            // Trop de différences : tout le milieu devient un bloc « supprimé puis ajouté ».
            for (int i = 0; i < n; i++) result.Add(Op.Delete);
            for (int i = 0; i < m; i++) result.Add(Op.Insert);
            return result;
        }

        // Remontée du chemin à partir de la fin.
        int cx = n, cy = m;
        var reversed = new List<Op>();
        for (int d = found; d > 0; d--)
        {
            var pv = trace[d];
            int k = cx - cy;
            int prevK = k == -d || (k != d && pv[off + k - 1] < pv[off + k + 1]) ? k + 1 : k - 1;
            int prevX = pv[off + prevK];
            int prevY = prevX - prevK;
            while (cx > prevX && cy > prevY) { reversed.Add(Op.Same); cx--; cy--; }
            reversed.Add(cx == prevX ? Op.Insert : Op.Delete);
            cx = prevX;
            cy = prevY;
        }
        while (cx > 0 && cy > 0) { reversed.Add(Op.Same); cx--; cy--; }
        reversed.Reverse();
        return reversed;
    }

    /// <summary>Transforme le script en lignes côte à côte ; une suppression suivie d'un ajout devient « modifié ».</summary>
    private static List<DiffLine> Pair(IReadOnlyList<string> a, IReadOnlyList<string> b, List<Op> ops)
    {
        var lines = new List<DiffLine>();
        int i = 0, j = 0, p = 0;
        while (p < ops.Count)
        {
            if (ops[p] == Op.Same)
            {
                lines.Add(new DiffLine(i + 1, a[i], j + 1, b[j], DiffKind.Same));
                i++; j++; p++;
                continue;
            }
            var removed = new List<int>();
            var added = new List<int>();
            while (p < ops.Count && ops[p] != Op.Same)
            {
                if (ops[p] == Op.Delete) removed.Add(i++);
                else added.Add(j++);
                p++;
            }
            int common = Math.Min(removed.Count, added.Count);
            for (int t = 0; t < common; t++)
                lines.Add(new DiffLine(removed[t] + 1, a[removed[t]], added[t] + 1, b[added[t]], DiffKind.Changed));
            for (int t = common; t < removed.Count; t++)
                lines.Add(new DiffLine(removed[t] + 1, a[removed[t]], null, null, DiffKind.Removed));
            for (int t = common; t < added.Count; t++)
                lines.Add(new DiffLine(null, null, added[t] + 1, b[added[t]], DiffKind.Added));
        }
        return lines;
    }
}
