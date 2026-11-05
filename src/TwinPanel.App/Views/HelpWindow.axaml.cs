// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using TwinPanel.Core.Help;

namespace TwinPanel.App.Views;

/// <summary>Une ligne de la page « Raccourcis », calculée au moment d'ouvrir le guide. Sans touche : titre de groupe.</summary>
public sealed record HelpShortcut(string Keys, string Action);

/// <summary>Guide intégré (F1) : le texte du README de la langue en service, sans internet.</summary>
public partial class HelpWindow : Window
{
    private static readonly FontFamily CodeFont = new("Cascadia Mono,Consolas,Menlo,monospace");
    private readonly IReadOnlyList<GuideChapter> _chapters = Load();
    private IReadOnlyList<HelpShortcut> _shortcuts = [];

    public HelpWindow()
    {
        InitializeComponent();
        ChapterList.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(GuideChapter.Title));
        ChapterList.ItemsSource = _chapters;
        ChapterList.SelectionChanged += (_, _) => { if (ChapterList.SelectedItem is GuideChapter c) Render(c); };
        SearchBox.TextChanged += (_, _) => Filter();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Opened += (_, _) => SearchBox.Focus();
    }

    /// <summary>Raccourcis en vigueur et chapitre à montrer (le premier, sinon).</summary>
    public void Open(IReadOnlyList<HelpShortcut> shortcuts, bool atShortcuts)
    {
        _shortcuts = shortcuts;
        SearchBox.Text = string.Empty;
        ChapterList.ItemsSource = _chapters;
        ChapterList.SelectedItem = atShortcuts
            ? _chapters.FirstOrDefault(c => c.Blocks.Any(b => b.Kind == GuideBlockKind.Shortcuts))
            : _chapters.FirstOrDefault();
    }

    private static IReadOnlyList<GuideChapter> Load()
    {
        var name = L.Language == L.English ? "README.en.md" : "README.md";
        using var stream = AssetLoader.Open(new Uri($"avares://TwinPanel/Assets/{name}"));
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return GuideDocument.Parse(reader.ReadToEnd());
    }

    private void Filter()
    {
        var query = (SearchBox.Text ?? string.Empty).Trim();
        var shown = query.Length == 0 ? _chapters : _chapters.Where(c => c.Contains(query)).ToList();
        ChapterList.ItemsSource = shown;
        ChapterList.SelectedItem = shown.FirstOrDefault();
        if (shown.Count == 0)
        {
            Page.Children.Clear();
            Page.Children.Add(new TextBlock { Text = L.T("Rien trouvé dans le guide."), Classes = { "subtle" } });
        }
    }

    private void Render(GuideChapter chapter)
    {
        Page.Children.Clear();
        Page.Children.Add(new TextBlock { Text = chapter.Title, FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        foreach (var block in chapter.Blocks)
        {
            switch (block.Kind)
            {
                case GuideBlockKind.Heading:
                    Page.Children.Add(new TextBlock { Text = block.Text, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
                    break;
                case GuideBlockKind.Paragraph:
                    Page.Children.Add(Rich(block.Text));
                    break;
                case GuideBlockKind.Bullet:
                    var line = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*") };
                    line.Children.Add(new TextBlock { Text = "•" });
                    var text = Rich(block.Text);
                    Grid.SetColumn(text, 1);
                    line.Children.Add(text);
                    Page.Children.Add(line);
                    break;
                case GuideBlockKind.Table:
                    Page.Children.Add(Table(block.Rows!));
                    break;
                case GuideBlockKind.Shortcuts:
                    AddShortcuts();
                    break;
            }
        }
        PageScroller.Offset = default;
    }

    private void AddShortcuts()
    {
        var rows = new List<IReadOnlyList<string>>();
        void Flush()
        {
            if (rows.Count > 0) Page.Children.Add(Table(rows.ToList()));
            rows.Clear();
        }
        foreach (var s in _shortcuts)
        {
            if (s.Keys.Length > 0) { rows.Add([$"`{s.Keys}`", s.Action]); continue; }
            Flush();
            Page.Children.Add(new TextBlock { Text = s.Action, FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
            rows.Add([L.T("Touche"), L.T("Action")]);
        }
        Flush();
    }

    private static Grid Table(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var columns = rows.Max(r => r.Count);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(columns == 2 ? "2*,3*" : string.Join(',', Enumerable.Repeat("*", columns))) };
        for (int r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < rows[r].Count; c++)
            {
                var cell = Rich(rows[r][c]);
                cell.Margin = new Thickness(0, 3, 14, 3);
                if (r == 0) cell.FontWeight = FontWeight.SemiBold;
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return grid;
    }

    /// <summary>Texte avec gras et « code » ; on peut le sélectionner pour le copier.</summary>
    private static SelectableTextBlock Rich(string text)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Inlines = [] };
        foreach (var span in GuideDocument.Spans(text))
        {
            var run = new Run(span.Text);
            if (span.Bold) run.FontWeight = FontWeight.SemiBold;
            if (span.Code) run.FontFamily = CodeFont;
            block.Inlines.Add(run);
        }
        return block;
    }
}
