// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text.RegularExpressions;

namespace TwinPanel.Core.Selection;

/// <summary>
/// Masques de sélection : « *.jpg;*.png » (plusieurs masques séparés par ; ou espace)
/// et exclusions après « | » : « *.* | *.tmp ».
/// </summary>
public sealed class MaskMatcher
{
    private readonly Regex[] _include;
    private readonly Regex[] _exclude;

    public MaskMatcher(string mask)
    {
        var parts = mask.Split('|', 2);
        _include = Parse(parts[0]);
        _exclude = parts.Length > 1 ? Parse(parts[1]) : [];
        if (_include.Length == 0) _include = [ToRegex("*")];
    }

    public bool IsMatch(string fileName) =>
        _include.Any(r => r.IsMatch(fileName)) && !_exclude.Any(r => r.IsMatch(fileName));

    private static Regex[] Parse(string text) =>
        text.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ToRegex).ToArray();

    /// <summary>« *.* » accepte aussi les noms sans extension (comme sous Windows).</summary>
    private static Regex ToRegex(string wildcard)
    {
        if (wildcard == "*.*") wildcard = "*";
        var pattern = "^" + Regex.Escape(wildcard).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
