// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text;

namespace TwinPanel.Core.Help;

public enum GuideBlockKind { Heading, Paragraph, Bullet, Table, Shortcuts }

/// <summary>Un élément d'un chapitre. Rows : lignes du tableau, la première étant celle des titres.</summary>
public sealed record GuideBlock(GuideBlockKind Kind, string Text, IReadOnlyList<IReadOnlyList<string>>? Rows = null);

public sealed record GuideChapter(string Title, IReadOnlyList<GuideBlock> Blocks)
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    /// <summary>Le chapitre parle-t-il de ce mot ? Majuscules et accents ne comptent pas.</summary>
    public bool Contains(string query)
    {
        bool Has(string text) => Compare.IndexOf(text, query, Loose) >= 0;
        return Has(Title) || Blocks.Any(b => Has(b.Text) || (b.Rows?.Any(r => r.Any(Has)) ?? false));
    }
}

/// <summary>Morceau de texte d'une ligne du guide, gras ou « code » (touche, chemin…).</summary>
public readonly record struct GuideSpan(string Text, bool Bold = false, bool Code = false);

/// <summary>
/// Lit le guide écrit dans le README, entre &lt;!-- guide --&gt; et &lt;!-- /guide --&gt; : un titre « ## » ouvre un chapitre.
/// Le même texte sert ainsi à GitHub et à la fenêtre d'aide, sans deuxième copie à tenir à jour.
/// </summary>
public static class GuideDocument
{
    private const string Start = "<!-- guide -->";
    private const string End = "<!-- /guide -->";
    private const string ShortcutsMark = "<!-- raccourcis -->";
    private const string ShortcutsEnd = "<!-- /raccourcis -->";

    public static IReadOnlyList<GuideChapter> Parse(string markdown)
    {
        var chapters = new List<GuideChapter>();
        string? title = null;
        var blocks = new List<GuideBlock>();
        var paragraph = new List<string>();
        var table = new List<IReadOnlyList<string>>();

        void Flush()
        {
            if (paragraph.Count > 0) blocks.Add(new GuideBlock(GuideBlockKind.Paragraph, string.Join(' ', paragraph)));
            paragraph.Clear();
            if (table.Count > 0) blocks.Add(new GuideBlock(GuideBlockKind.Table, string.Empty, table.ToList()));
            table.Clear();
        }

        void Close()
        {
            Flush();
            if (title is not null) chapters.Add(new GuideChapter(title, blocks.ToList()));
            blocks.Clear();
        }

        var inside = false;
        var skipping = false; // tableau fixe pour GitHub : TwinPanel montre les raccourcis en vigueur à la place
        foreach (var raw in markdown.TrimStart('﻿').Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');
            if (line == Start) { inside = true; continue; }
            if (line == End) break;
            if (!inside) continue;
            if (line == ShortcutsEnd) { skipping = false; continue; }
            if (skipping) continue;
            if (line.StartsWith("## ", StringComparison.Ordinal)) { Close(); title = line[3..].Trim(); }
            else if (line.StartsWith("### ", StringComparison.Ordinal)) { Flush(); blocks.Add(new GuideBlock(GuideBlockKind.Heading, line[4..].Trim())); }
            else if (line.StartsWith("- ", StringComparison.Ordinal)) { Flush(); blocks.Add(new GuideBlock(GuideBlockKind.Bullet, line[2..].Trim())); }
            else if (line.StartsWith('|'))
            {
                if (paragraph.Count > 0) Flush();
                if (!IsTableRule(line)) table.Add(Cells(line));
            }
            else if (line == ShortcutsMark) { Flush(); blocks.Add(new GuideBlock(GuideBlockKind.Shortcuts, string.Empty)); skipping = true; }
            else if (line.Length == 0 || line.StartsWith("<!--", StringComparison.Ordinal)) Flush();
            else paragraph.Add(line);
        }
        Close();
        return chapters;
    }

    private static bool IsTableRule(string line) => line.All(c => c is '|' or '-' or ':' or ' ');

    /// <summary>Cellules d'une ligne de tableau ; « \| » est une vraie barre, pas une séparation.</summary>
    private static List<string> Cells(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (int i = 1; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '|') { cell.Append('|'); i++; }
            else if (line[i] == '|') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else cell.Append(line[i]);
        }
        return cells;
    }

    /// <summary>Découpe **gras** et `code`. Un repère sans sa fin reste du texte ordinaire.</summary>
    public static IReadOnlyList<GuideSpan> Spans(string text)
    {
        var spans = new List<GuideSpan>();
        var pos = 0;
        while (pos < text.Length)
        {
            var bold = text.IndexOf("**", pos, StringComparison.Ordinal);
            var code = text.IndexOf('`', pos);
            var isBold = bold >= 0 && (code < 0 || bold < code);
            var start = isBold ? bold : code;
            var mark = isBold ? 2 : 1;
            var end = start < 0 ? -1 : text.IndexOf(isBold ? "**" : "`", start + mark, StringComparison.Ordinal);
            if (end < 0) break;
            if (start > pos) spans.Add(new GuideSpan(text[pos..start]));
            spans.Add(new GuideSpan(text[(start + mark)..end], isBold, !isBold));
            pos = end + mark;
        }
        if (pos < text.Length) spans.Add(new GuideSpan(text[pos..]));
        return spans;
    }
}
