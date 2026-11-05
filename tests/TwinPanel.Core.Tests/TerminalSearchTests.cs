// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Terminal;

namespace TwinPanel.Core.Tests;

public class TerminalSearchTests
{
    private static TerminalEmulator With(string text, int columns = 20, int rows = 4, int history = 100)
    {
        var screen = new TerminalEmulator(columns, rows, history);
        screen.Feed(Encoding.UTF8.GetBytes(text));
        return screen;
    }

    [Fact]
    public void Finds_every_match_regardless_of_case()
    {
        var matches = With("Erreur ici\r\nrien\r\nune ERREUR\r\n").Find("erreur");
        Assert.Equal(2, matches.Count);
        Assert.Equal(new TerminalMatch(0, 0, 0, 6), matches[0]);
        Assert.Equal(new TerminalMatch(2, 4, 2, 10), matches[1]);
    }

    [Fact]
    public void Finds_text_in_the_history()
    {
        var text = "ancien\r\n" + string.Concat(Enumerable.Repeat("ligne\r\n", 10));
        var screen = With(text);
        Assert.True(screen.HistoryCount > 0);
        Assert.Equal(new TerminalMatch(0, 0, 0, 6), Assert.Single(screen.Find("ancien")));
    }

    [Fact]
    public void Finds_a_word_cut_by_the_width()
    {
        var match = Assert.Single(With("0123456789abcdefghij", columns: 10).Find("89ab"));
        Assert.Equal(new TerminalMatch(0, 8, 1, 2), match);
    }

    [Fact]
    public void Wide_characters_keep_their_cells()
    {
        var match = Assert.Single(With("a日本b").Find("本b"));
        Assert.Equal(new TerminalMatch(0, 3, 0, 6), match);
    }

    [Fact]
    public void The_second_screen_ignores_the_history()
    {
        var screen = With("cache\r\n\u001b[?1049h\u001b[2Jvisible");
        Assert.Empty(screen.Find("cache"));
        Assert.Single(screen.Find("visible"));
    }

    [Fact]
    public void Nothing_to_find()
    {
        Assert.Empty(With("texte").Find(string.Empty));
        Assert.Empty(With("texte").Find("absent"));
    }
}
