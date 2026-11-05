// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text;

namespace TwinPanel.Core.Terminal;

[Flags]
public enum CellStyle : byte
{
    None = 0,
    Bold = 1,
    Faint = 2,
    Italic = 4,
    Underline = 8,
    Inverse = 16,
    Strike = 32,
    Hidden = 64,
}

/// <summary>Couleur d'une case : défaut, palette (0 à 255) ou couleur exacte.</summary>
public static class TerminalColor
{
    public const int Default = -1;
    private const int Exact = 0x1000000;

    public static int FromRgb(int r, int g, int b) => Exact | ((r & 255) << 16) | ((g & 255) << 8) | (b & 255);
    public static bool IsRgb(int color) => color >= Exact;
    public static (byte R, byte G, byte B) Rgb(int color) => ((byte)(color >> 16), (byte)(color >> 8), (byte)color);

    /// <summary>Écart de luminosité entre deux couleurs : 1 (identiques) à 21 (noir sur blanc).</summary>
    public static double Contrast((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    public static double Luminance((byte R, byte G, byte B) c) =>
        0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte value)
    {
        double v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>Palette 256 couleurs, partie fixe : cube de 216 couleurs puis 24 gris (16 à 255).</summary>
    public static (byte R, byte G, byte B) Extended(int index)
    {
        if (index >= 232)
        {
            var gray = (byte)(8 + (index - 232) * 10);
            return (gray, gray, gray);
        }
        var n = index - 16;
        return (Level(n / 36), Level(n / 6 % 6), Level(n % 6));
        static byte Level(int v) => (byte)(v == 0 ? 0 : 55 + v * 40);
    }
}

/// <summary>Une case de l'écran. <see cref="Text"/> nul : case vide. Largeur 0 : suite d'un caractère large.</summary>
public struct TerminalCell
{
    public string? Text;
    public int Foreground;
    public int Background;
    public CellStyle Style;
    public byte Width;

    public static TerminalCell Blank(int foreground, int background) =>
        new() { Foreground = foreground, Background = background, Width = 1 };
}

/// <summary>Une ligne. <see cref="Wrapped"/> : elle continue sur la suivante (coupée par la largeur).</summary>
public sealed class TerminalLine(int columns, int foreground, int background)
{
    public TerminalCell[] Cells { get; private set; } = NewCells(columns, foreground, background);
    public bool Wrapped { get; set; }

    private static TerminalCell[] NewCells(int columns, int foreground, int background)
    {
        var cells = new TerminalCell[columns];
        Array.Fill(cells, TerminalCell.Blank(foreground, background));
        return cells;
    }

    /// <summary>Remet la ligne à neuf pour la réutiliser : un flot de texte ne crée plus de lignes à jeter.</summary>
    public void Reset(int foreground, int background)
    {
        Array.Fill(Cells, TerminalCell.Blank(foreground, background));
        Wrapped = false;
    }

    /// <summary>Une ligne plus longue que l'écran garde son texte : il revient si la fenêtre s'élargit.</summary>
    public void EnsureColumns(int columns)
    {
        if (Cells.Length >= columns) return;
        var old = Cells;
        Cells = NewCells(columns, TerminalColor.Default, TerminalColor.Default);
        Array.Copy(old, Cells, old.Length);
    }
}

public enum MouseTracking { None, Press, Drag, All }

/// <summary>Un mot trouvé : de sa première case à la case qui suit la dernière, en numéros de ligne de <see cref="TerminalEmulator.LineAt"/>.</summary>
public readonly record struct TerminalMatch(int StartLine, int StartColumn, int EndLine, int EndColumn);

/// <summary>
/// Écran virtuel d'un terminal : reçoit le flux du programme et tient à jour les lignes, le curseur,
/// les couleurs et l'historique. Sans interface, donc testable. Il n'est pas protégé contre les
/// accès simultanés : <see cref="TerminalSession"/> s'en occupe.
/// </summary>
public sealed class TerminalEmulator : IVtHandler
{
    private readonly VtParser _parser;
    private readonly List<TerminalLine> _history = [];
    private readonly Stack<TerminalLine> _spareLines = new();
    private TerminalLine[] _main;
    private TerminalLine[] _alternate;
    private TerminalLine[] _lines;
    private bool[] _tabStops = [];

    private int _top;
    private int _bottom;
    private bool _pendingWrap;
    private bool _insertMode;
    private bool _originMode;
    private bool _autoWrap = true;
    private bool _newLineMode;

    private int _foreground = TerminalColor.Default;
    private int _background = TerminalColor.Default;
    private CellStyle _style;
    private Rune? _lastPrinted;

    private bool[] _lineDrawing = [false, false];
    private int _activeCharset;

    private sealed record SavedCursor(int X, int Y, int Foreground, int Background, CellStyle Style, bool Origin, bool AutoWrap, bool[] LineDrawing, int Charset);
    private SavedCursor? _savedMain;
    private SavedCursor? _savedAlternate;

    public TerminalEmulator(int columns = 80, int rows = 24, int historyLimit = 10_000)
    {
        Columns = Math.Max(2, columns);
        Rows = Math.Max(1, rows);
        HistoryLimit = Math.Max(0, historyLimit);
        _parser = new VtParser(this);
        _main = NewScreen();
        _alternate = NewScreen();
        _lines = _main;
        _bottom = Rows - 1;
        ResetTabStops();
    }

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public int HistoryLimit { get; }
    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool CursorVisible { get; private set; } = true;
    /// <summary>Forme du curseur : 0 bloc, 1 souligné, 2 barre.</summary>
    public int CursorShape { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public bool IsAlternateScreen => _lines == _alternate;
    public bool ReverseVideo { get; private set; }
    public bool ApplicationCursorKeys { get; private set; }
    public bool ApplicationKeypad { get; private set; }
    public bool BracketedPaste { get; private set; }
    public bool ReportFocus { get; private set; }
    public MouseTracking Mouse { get; private set; }
    public bool MouseSgr { get; private set; }

    /// <summary>Lignes parties de l'écran, gardées pour remonter.</summary>
    public int HistoryCount => _history.Count;
    /// <summary>Augmente à chaque ligne perdue de l'historique plein : une sélection garde sa place.</summary>
    public long HistoryDropped { get; private set; }

    /// <summary>Réponse au programme (position du curseur, identité du terminal…).</summary>
    public event Action<string>? Reply;
    public event Action? Bell;
    public event Action? TitleChanged;

    public void Feed(ReadOnlySpan<byte> data) => _parser.Feed(data);

    /// <summary>Ligne par numéro absolu : l'historique d'abord, puis l'écran.</summary>
    public TerminalLine LineAt(int index) => index < _history.Count ? _history[index] : _lines[index - _history.Count];

    /// <summary>Ligne de l'écran seul (0 : la première).</summary>
    public TerminalLine ScreenLine(int row) => _lines[row];

    /// <summary>Texte de l'écran, sans les espaces de fin de ligne (essais et copie).</summary>
    public string ScreenText() => string.Join('\n', _lines.Select(l => LineText(l, 0, Columns))).TrimEnd('\n');

    /// <summary>Texte entre deux positions absolues : les lignes coupées par la largeur sont recollées.</summary>
    public string TextBetween(int startLine, int startColumn, int endLine, int endColumn)
    {
        var last = _history.Count + Rows - 1;
        startLine = Math.Clamp(startLine, 0, last);
        endLine = Math.Clamp(endLine, 0, last);
        var sb = new StringBuilder();
        for (int i = startLine; i <= endLine; i++)
        {
            var line = LineAt(i);
            int from = i == startLine ? startColumn : 0;
            int to = i == endLine ? endColumn : Columns;
            sb.Append(LineText(line, from, to, keepTrailing: line.Wrapped && i < endLine));
            if (i < endLine && !line.Wrapped) sb.Append(Environment.NewLine);
        }
        return sb.ToString();
    }

    private static string LineText(TerminalLine line, int from, int to, bool keepTrailing = false)
    {
        var sb = new StringBuilder();
        to = Math.Min(to, line.Cells.Length);
        for (int x = Math.Max(0, from); x < to; x++)
        {
            ref var c = ref line.Cells[x];
            if (c.Width == 0) continue;
            sb.Append(c.Text ?? " ");
        }
        return keepTrailing ? sb.ToString() : sb.ToString().TrimEnd(' ');
    }

    /// <summary>Cherche un texte, sans tenir compte des majuscules. Les lignes coupées par la largeur sont lues d'un seul tenant.</summary>
    public List<TerminalMatch> Find(string text)
    {
        var matches = new List<TerminalMatch>();
        if (text.Length == 0) return matches;
        var content = new StringBuilder();
        var places = new List<(int Line, int Column, int Width)>();
        int last = _history.Count + Rows - 1;
        // Le second écran (nano, less…) n'a pas d'historique : l'autre texte n'y est pas visible.
        for (int i = IsAlternateScreen ? _history.Count : 0; i <= last; i++)
        {
            var line = LineAt(i);
            for (int x = 0; x < line.Cells.Length; x++)
            {
                ref var cell = ref line.Cells[x];
                if (cell.Width == 0) continue;
                var value = cell.Text ?? " ";
                content.Append(value);
                for (int c = 0; c < value.Length; c++) places.Add((i, x, Math.Max(1, (int)cell.Width)));
            }
            if (line.Wrapped && i < last) continue;
            CollectMatches(content.ToString(), places, text, matches);
            content.Clear();
            places.Clear();
        }
        return matches;
    }

    private static void CollectMatches(string content, List<(int Line, int Column, int Width)> places, string text, List<TerminalMatch> matches)
    {
        int at = 0;
        while ((at = content.IndexOf(text, at, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var (firstLine, firstColumn, _) = places[at];
            var (lastLine, lastColumn, lastWidth) = places[at + text.Length - 1];
            matches.Add(new TerminalMatch(firstLine, firstColumn, lastLine, lastColumn + lastWidth));
            at += text.Length;
        }
    }

    public void Resize(int columns, int rows)
    {
        columns = Math.Max(2, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows) return;
        bool alternate = IsAlternateScreen;

        if (rows != Rows)
        {
            var main = _main.ToList();
            if (alternate)
            {
                // L'écran normal reste caché : on coupe ou on complète par le bas.
                while (main.Count > rows) main.RemoveAt(main.Count - 1);
            }
            else
            {
                // La ligne du curseur reste visible : le haut part dans l'historique.
                int cursorRow = CursorY;
                while (main.Count > rows)
                {
                    if (cursorRow < main.Count - 1) main.RemoveAt(main.Count - 1);
                    else
                    {
                        PushHistory(main[0]);
                        main.RemoveAt(0);
                        cursorRow--;
                    }
                }
                // Plus de place : les dernières lignes de l'historique redescendent.
                while (main.Count < rows && _history.Count > 0 && cursorRow == main.Count - 1)
                {
                    main.Insert(0, _history[^1]);
                    _history.RemoveAt(_history.Count - 1);
                    cursorRow++;
                }
                CursorY = cursorRow;
            }
            while (main.Count < rows) main.Add(NewLine(columns));
            _main = [.. main];

            var other = _alternate.ToList();
            while (other.Count > rows) other.RemoveAt(other.Count - 1);
            while (other.Count < rows) other.Add(NewLine(columns));
            _alternate = [.. other];
        }

        Columns = columns;
        Rows = rows;
        foreach (var line in _main.Concat(_alternate)) line.EnsureColumns(columns);
        _lines = alternate ? _alternate : _main;
        _top = 0;
        _bottom = rows - 1;
        CursorX = Math.Min(CursorX, columns - 1);
        CursorY = Math.Clamp(CursorY, 0, rows - 1);
        _pendingWrap = false;
        var stops = _tabStops;
        ResetTabStops();
        Array.Copy(stops, _tabStops, Math.Min(stops.Length, _tabStops.Length));
    }

    private TerminalLine[] NewScreen()
    {
        var lines = new TerminalLine[Rows];
        for (int i = 0; i < lines.Length; i++) lines[i] = NewLine(Columns);
        return lines;
    }

    private TerminalLine NewLine(int columns)
    {
        while (_spareLines.Count > 0)
        {
            var line = _spareLines.Pop();
            if (line.Cells.Length != columns) continue;
            line.Reset(TerminalColor.Default, _background);
            return line;
        }
        return new TerminalLine(columns, TerminalColor.Default, _background);
    }

    private void ResetTabStops()
    {
        _tabStops = new bool[Columns];
        for (int i = 8; i < Columns; i += 8) _tabStops[i] = true;
    }

    // ---- Texte -----------------------------------------------------------------------------------

    void IVtHandler.Print(Rune rune)
    {
        var active = _lineDrawing[_activeCharset];
        if (active && rune.Value is >= 0x60 and <= 0x7E) rune = new Rune(LineDrawing[rune.Value - 0x60]);
        int width = CharacterWidth(rune);

        if (width == 0)
        {
            AppendToPrevious(rune);
            return;
        }

        _lastPrinted = rune;
        PutCell(TextOf(rune), width);
    }

    /// <summary>Textes des caractères simples, partagés : créer une chaîne par lettre ralentirait tout le terminal.</summary>
    private static readonly string[] AsciiText = [.. Enumerable.Range(0, 128).Select(i => ((char)i).ToString())];

    private static string TextOf(Rune rune) => rune.Value < 128 ? AsciiText[rune.Value] : rune.ToString();

    private void PutCell(string text, int width)
    {
        if (_pendingWrap || (width == 2 && CursorX == Columns - 1))
        {
            if (_autoWrap)
            {
                _lines[CursorY].Wrapped = true;
                CursorX = 0;
                LineFeed();
            }
            _pendingWrap = false;
        }

        var cells = _lines[CursorY].Cells;
        if (_insertMode)
        {
            Array.Copy(cells, CursorX, cells, CursorX + width, Math.Max(0, Columns - CursorX - width));
        }
        ClearWideHalves(cells, CursorX);
        if (width == 2) ClearWideHalves(cells, CursorX + 1);

        cells[CursorX] = new TerminalCell { Text = text, Foreground = _foreground, Background = _background, Style = _style, Width = (byte)width };
        if (width == 2) cells[CursorX + 1] = new TerminalCell { Foreground = _foreground, Background = _background, Style = _style, Width = 0 };

        if (CursorX + width >= Columns)
        {
            CursorX = Columns - 1;
            _pendingWrap = _autoWrap;
        }
        else CursorX += width;
    }

    /// <summary>Écrire sur une moitié de caractère large : l'autre moitié ne doit pas rester seule.</summary>
    private void ClearWideHalves(TerminalCell[] cells, int x)
    {
        if (x >= Columns) return;
        if (cells[x].Width == 0 && x > 0) cells[x - 1] = TerminalCell.Blank(cells[x - 1].Foreground, cells[x - 1].Background);
        else if (cells[x].Width == 2 && x + 1 < Columns) cells[x + 1] = TerminalCell.Blank(cells[x + 1].Foreground, cells[x + 1].Background);
    }

    private void AppendToPrevious(Rune rune)
    {
        int x = _pendingWrap ? CursorX : CursorX - 1;
        if (x < 0) return;
        var cells = _lines[CursorY].Cells;
        if (cells[x].Width == 0 && x > 0) x--;
        if (cells[x].Text is { } text) cells[x].Text = text + rune;
    }

    /// <summary>Largeur à l'écran : 0 (accent à poser sur le caractère précédent), 1 ou 2 (idéogrammes, émojis).</summary>
    public static int CharacterWidth(Rune rune)
    {
        int v = rune.Value;
        if (v < 0x300) return 1;
        if (v is 0x200D or 0xFE0F or 0xFE0E) return 0;
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        return v is (>= 0x1100 and <= 0x115F)
            or (>= 0x2E80 and <= 0x303E)
            or (>= 0x3041 and <= 0x33FF)
            or (>= 0x3400 and <= 0x4DBF)
            or (>= 0x4E00 and <= 0x9FFF)
            or (>= 0xA000 and <= 0xA4CF)
            or (>= 0xAC00 and <= 0xD7A3)
            or (>= 0xF900 and <= 0xFAFF)
            or (>= 0xFE30 and <= 0xFE6F)
            or (>= 0xFF00 and <= 0xFF60)
            or (>= 0xFFE0 and <= 0xFFE6)
            or (>= 0x1F300 and <= 0x1F64F)
            or (>= 0x1F680 and <= 0x1F6FF)
            or (>= 0x1F900 and <= 0x1F9FF)
            or (>= 0x20000 and <= 0x3FFFD)
            ? 2 : 1;
    }

    /// <summary>Jeu de caractères graphiques : les lettres 0x60 à 0x7E dessinent des cadres.</summary>
    private const string LineDrawing = "◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·";

    // ---- Commandes simples -----------------------------------------------------------------------

    void IVtHandler.Control(byte code)
    {
        switch (code)
        {
            case 0x07: Bell?.Invoke(); break;
            case 0x08:
                if (_pendingWrap) _pendingWrap = false;
                else if (CursorX > 0) CursorX--;
                break;
            case 0x09: NextTab(1); break;
            case 0x0A or 0x0B or 0x0C:
                _lines[CursorY].Wrapped = false;
                LineFeed();
                if (_newLineMode) CursorX = 0;
                break;
            case 0x0D:
                CursorX = 0;
                _pendingWrap = false;
                break;
            case 0x0E: _activeCharset = 1; break;
            case 0x0F: _activeCharset = 0; break;
        }
    }

    private void NextTab(int count)
    {
        for (int i = 0; i < count && CursorX < Columns - 1; i++)
        {
            do CursorX++;
            while (CursorX < Columns - 1 && !_tabStops[CursorX]);
        }
        _pendingWrap = false;
    }

    private void PreviousTab(int count)
    {
        for (int i = 0; i < count && CursorX > 0; i++)
        {
            do CursorX--;
            while (CursorX > 0 && !_tabStops[CursorX]);
        }
        _pendingWrap = false;
    }

    private void LineFeed()
    {
        _pendingWrap = false;
        if (CursorY == _bottom) ScrollUp(_top, _bottom, 1);
        else if (CursorY < Rows - 1) CursorY++;
    }

    private void ReverseIndex()
    {
        _pendingWrap = false;
        if (CursorY == _top) ScrollDown(_top, _bottom, 1);
        else if (CursorY > 0) CursorY--;
    }

    private void PushHistory(TerminalLine line)
    {
        if (HistoryLimit == 0)
        {
            _spareLines.Push(line);
            return;
        }
        _history.Add(line);
        if (_history.Count > HistoryLimit)
        {
            // Par paquets : retirer une seule ligne à chaque fois recopierait toute la liste.
            int extra = Math.Max(1, HistoryLimit / 10);
            for (int i = 0; i < extra; i++) _spareLines.Push(_history[i]);
            _history.RemoveRange(0, extra);
            HistoryDropped += extra;
        }
    }

    private void ScrollUp(int top, int bottom, int count)
    {
        count = Math.Min(count, bottom - top + 1);
        for (int i = 0; i < count; i++)
        {
            var line = _lines[top];
            if (top == 0 && !IsAlternateScreen) PushHistory(line);
            else _spareLines.Push(line);
            Array.Copy(_lines, top + 1, _lines, top, bottom - top);
            _lines[bottom] = NewLine(Columns);
        }
    }

    private void ScrollDown(int top, int bottom, int count)
    {
        count = Math.Min(count, bottom - top + 1);
        for (int i = 0; i < count; i++)
        {
            Array.Copy(_lines, top, _lines, top + 1, bottom - top);
            _lines[top] = NewLine(Columns);
        }
    }

    void IVtHandler.Escape(char intermediate, char final)
    {
        if (intermediate is '(' or ')')
        {
            _lineDrawing[intermediate == '(' ? 0 : 1] = final == '0';
            return;
        }
        if (intermediate != '\0') return;
        switch (final)
        {
            case '7': SaveCursor(); break;
            case '8': RestoreCursor(); break;
            case 'D': LineFeed(); break;
            case 'E': CursorX = 0; LineFeed(); break;
            case 'H': _tabStops[CursorX] = true; break;
            case 'M': ReverseIndex(); break;
            case 'c': FullReset(); break;
            case '=': ApplicationKeypad = true; break;
            case '>': ApplicationKeypad = false; break;
        }
    }

    private void SaveCursor()
    {
        var saved = new SavedCursor(CursorX, CursorY, _foreground, _background, _style, _originMode, _autoWrap, [.. _lineDrawing], _activeCharset);
        if (IsAlternateScreen) _savedAlternate = saved; else _savedMain = saved;
    }

    private void RestoreCursor()
    {
        var saved = IsAlternateScreen ? _savedAlternate : _savedMain;
        _pendingWrap = false;
        if (saved is null)
        {
            CursorX = CursorY = 0;
            return;
        }
        CursorX = Math.Min(saved.X, Columns - 1);
        CursorY = Math.Min(saved.Y, Rows - 1);
        _foreground = saved.Foreground;
        _background = saved.Background;
        _style = saved.Style;
        _originMode = saved.Origin;
        _autoWrap = saved.AutoWrap;
        _lineDrawing = [.. saved.LineDrawing];
        _activeCharset = saved.Charset;
    }

    private void SoftReset()
    {
        _foreground = _background = TerminalColor.Default;
        _style = CellStyle.None;
        _top = 0;
        _bottom = Rows - 1;
        _insertMode = false;
        _originMode = false;
        _autoWrap = true;
        CursorVisible = true;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        _lineDrawing = [false, false];
        _activeCharset = 0;
        _pendingWrap = false;
    }

    private void FullReset()
    {
        SoftReset();
        _lines = _main;
        _main = NewScreen();
        _alternate = NewScreen();
        _lines = _main;
        _savedMain = _savedAlternate = null;
        CursorX = CursorY = 0;
        ReverseVideo = false;
        BracketedPaste = false;
        ReportFocus = false;
        Mouse = MouseTracking.None;
        MouseSgr = false;
        _newLineMode = false;
        CursorShape = 0;
        ResetTabStops();
    }

    // ---- CSI ---------------------------------------------------------------------------------------

    private static int[] ParseParameters(string text)
    {
        if (text.Length == 0) return [];
        var parts = text.Split(';');
        var values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            int colon = part.IndexOf(':');
            if (colon >= 0) part = part[..colon];
            values[i] = part.Length > 0 && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? Math.Min(v, 65535) : 0;
        }
        return values;
    }

    private static int Param(int[] p, int index, int fallback) => index < p.Length && p[index] != 0 ? p[index] : fallback;

    void IVtHandler.Csi(char prefix, char intermediate, char final, string parameters)
    {
        var p = ParseParameters(parameters);
        if (prefix == '?') { PrivateCsi(final, p); return; }
        if (prefix == '>')
        {
            if (final == 'c') Reply?.Invoke("\u001b[>0;10;1c");
            return;
        }
        if (prefix != '\0') return;

        if (intermediate == ' ')
        {
            if (final == 'q') CursorShape = Param(p, 0, 1) switch { 3 or 4 => 1, 5 or 6 => 2, _ => 0 };
            return;
        }
        if (intermediate == '!')
        {
            if (final == 'p') SoftReset();
            return;
        }
        if (intermediate != '\0') return;

        int n = Param(p, 0, 1);
        switch (final)
        {
            case '@': InsertCharacters(n); break;
            case 'A': MoveTo(CursorX, Math.Max(CursorY - n, CursorY >= _top ? _top : 0)); break;
            case 'B' or 'e': MoveTo(CursorX, Math.Min(CursorY + n, CursorY <= _bottom ? _bottom : Rows - 1)); break;
            case 'C' or 'a': MoveTo(CursorX + n, CursorY); break;
            case 'D': MoveTo(CursorX - n, CursorY); break;
            case 'E': MoveTo(0, Math.Min(CursorY + n, _bottom)); break;
            case 'F': MoveTo(0, Math.Max(CursorY - n, _top)); break;
            case 'G' or '`': MoveTo(n - 1, CursorY); break;
            case 'H' or 'f': MoveWithOrigin(Param(p, 1, 1) - 1, n - 1); break;
            case 'I': NextTab(n); break;
            case 'J': EraseDisplay(p.Length > 0 ? p[0] : 0); break;
            case 'K': EraseLine(p.Length > 0 ? p[0] : 0); break;
            case 'L': if (CursorY >= _top && CursorY <= _bottom) { ScrollDown(CursorY, _bottom, n); CursorX = 0; _pendingWrap = false; } break;
            case 'M': if (CursorY >= _top && CursorY <= _bottom) { ScrollUp(CursorY, _bottom, n); CursorX = 0; _pendingWrap = false; } break;
            case 'P': DeleteCharacters(n); break;
            case 'S': ScrollUp(_top, _bottom, n); break;
            case 'T': ScrollDown(_top, _bottom, n); break;
            case 'X': EraseCells(CursorX, Math.Min(CursorX + n, Columns)); _pendingWrap = false; break;
            case 'Z': PreviousTab(n); break;
            case 'b': if (_lastPrinted is { } last) for (int i = 0; i < Math.Min(n, Columns * Rows); i++) PutCell(TextOf(last), CharacterWidth(last)); break;
            case 'c': if (Param(p, 0, 0) == 0) Reply?.Invoke("\u001b[?1;2c"); break;
            case 'd': MoveWithOrigin(CursorX, n - 1); break;
            case 'g':
                if (Param(p, 0, 0) == 0) _tabStops[CursorX] = false;
                else if (p[0] == 3) Array.Clear(_tabStops);
                break;
            case 'h': SetMode(p, true); break;
            case 'l': SetMode(p, false); break;
            case 'm': SelectStyle(parameters); break;
            case 'n':
                if (Param(p, 0, 0) == 5) Reply?.Invoke("\u001b[0n");
                else if (Param(p, 0, 0) == 6) Reply?.Invoke(CursorReport(string.Empty));
                break;
            case 'r': SetScrollRegion(n, Param(p, 1, Rows)); break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
        }
    }

    private string CursorReport(string prefix)
    {
        int row = CursorY - (_originMode ? _top : 0) + 1;
        return $"\u001b[{prefix}{row.ToString(CultureInfo.InvariantCulture)};{(CursorX + 1).ToString(CultureInfo.InvariantCulture)}R";
    }

    private void MoveTo(int x, int y)
    {
        CursorX = Math.Clamp(x, 0, Columns - 1);
        CursorY = Math.Clamp(y, 0, Rows - 1);
        _pendingWrap = false;
    }

    private void MoveWithOrigin(int x, int y)
    {
        if (_originMode) MoveTo(x, Math.Clamp(y + _top, _top, _bottom));
        else MoveTo(x, y);
    }

    private void SetScrollRegion(int top, int bottom)
    {
        top--;
        bottom = Math.Min(bottom, Rows) - 1;
        if (top >= bottom) return;
        _top = top;
        _bottom = bottom;
        MoveWithOrigin(0, 0);
    }

    private void InsertCharacters(int count)
    {
        var cells = _lines[CursorY].Cells;
        count = Math.Min(count, Columns - CursorX);
        Array.Copy(cells, CursorX, cells, CursorX + count, Columns - CursorX - count);
        EraseCells(CursorX, CursorX + count);
        _pendingWrap = false;
    }

    private void DeleteCharacters(int count)
    {
        var cells = _lines[CursorY].Cells;
        count = Math.Min(count, Columns - CursorX);
        Array.Copy(cells, CursorX + count, cells, CursorX, Columns - CursorX - count);
        EraseCells(Columns - count, Columns);
        _pendingWrap = false;
    }

    private void EraseCells(int from, int to) => EraseCells(_lines[CursorY], from, to);

    private void EraseCells(TerminalLine line, int from, int to)
    {
        var cells = line.Cells;
        var blank = TerminalCell.Blank(TerminalColor.Default, _background);
        for (int x = from; x < Math.Min(to, cells.Length); x++) cells[x] = blank;
    }

    private void EraseLine(int mode)
    {
        _pendingWrap = false;
        switch (mode)
        {
            case 0: EraseCells(CursorX, Columns); _lines[CursorY].Wrapped = false; break;
            case 1: EraseCells(0, CursorX + 1); break;
            case 2: EraseCells(0, Columns); _lines[CursorY].Wrapped = false; break;
        }
    }

    private void EraseDisplay(int mode)
    {
        _pendingWrap = false;
        switch (mode)
        {
            case 0:
                EraseLine(0);
                for (int y = CursorY + 1; y < Rows; y++) ClearLine(y);
                break;
            case 1:
                for (int y = 0; y < CursorY; y++) ClearLine(y);
                EraseCells(0, CursorX + 1);
                break;
            case 2:
                for (int y = 0; y < Rows; y++) ClearLine(y);
                break;
            case 3:
                HistoryDropped += _history.Count;
                _history.Clear();
                break;
        }
    }

    private void ClearLine(int y)
    {
        EraseCells(_lines[y], 0, Columns);
        _lines[y].Wrapped = false;
    }

    private void SetMode(int[] p, bool on)
    {
        foreach (var mode in p)
        {
            if (mode == 4) _insertMode = on;
            else if (mode == 20) _newLineMode = on;
        }
    }

    private void PrivateCsi(char final, int[] p)
    {
        if (final is 'h' or 'l')
        {
            foreach (var mode in p) SetPrivateMode(mode, final == 'h');
        }
        else if (final == 'n' && Param(p, 0, 0) == 6) Reply?.Invoke(CursorReport("?"));
    }

    private void SetPrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1: ApplicationCursorKeys = on; break;
            case 5: ReverseVideo = on; break;
            case 6: _originMode = on; MoveWithOrigin(0, 0); break;
            case 7: _autoWrap = on; if (!on) _pendingWrap = false; break;
            case 25: CursorVisible = on; break;
            case 47 or 1047: SwitchScreen(on, clear: on); break;
            case 1048: if (on) SaveCursor(); else RestoreCursor(); break;
            case 1049:
                if (on)
                {
                    SaveCursor();
                    SwitchScreen(true, clear: true);
                }
                else
                {
                    SwitchScreen(false, clear: false);
                    RestoreCursor();
                }
                break;
            case 9 or 1000: Mouse = on ? MouseTracking.Press : MouseTracking.None; break;
            case 1002: Mouse = on ? MouseTracking.Drag : MouseTracking.None; break;
            case 1003: Mouse = on ? MouseTracking.All : MouseTracking.None; break;
            case 1004: ReportFocus = on; break;
            case 1006: MouseSgr = on; break;
            case 2004: BracketedPaste = on; break;
        }
    }

    private void SwitchScreen(bool alternate, bool clear)
    {
        var target = alternate ? _alternate : _main;
        if (target == _lines) return;
        _lines = target;
        _pendingWrap = false;
        _top = 0;
        _bottom = Rows - 1;
        if (clear)
            for (int y = 0; y < Rows; y++) ClearLine(y);
    }

    /// <summary>Couleurs et styles. Les deux écritures des couleurs exactes sont acceptées (« 38;2;r;g;b » et « 38:2::r:g:b »).</summary>
    private void SelectStyle(string parameters)
    {
        if (parameters.Length == 0)
        {
            ResetStyle();
            return;
        }
        var parts = parameters.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Contains(':'))
            {
                ColonColor(part);
                continue;
            }
            int code = part.Length == 0 ? 0 : int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1;
            switch (code)
            {
                case 0: ResetStyle(); break;
                case 1: _style |= CellStyle.Bold; break;
                case 2: _style |= CellStyle.Faint; break;
                case 3: _style |= CellStyle.Italic; break;
                case 4: _style |= CellStyle.Underline; break;
                case 7: _style |= CellStyle.Inverse; break;
                case 8: _style |= CellStyle.Hidden; break;
                case 9: _style |= CellStyle.Strike; break;
                case 21: _style |= CellStyle.Underline; break;
                case 22: _style &= ~(CellStyle.Bold | CellStyle.Faint); break;
                case 23: _style &= ~CellStyle.Italic; break;
                case 24: _style &= ~CellStyle.Underline; break;
                case 27: _style &= ~CellStyle.Inverse; break;
                case 28: _style &= ~CellStyle.Hidden; break;
                case 29: _style &= ~CellStyle.Strike; break;
                case >= 30 and <= 37: _foreground = code - 30; break;
                case 38: i = ExtendedColor(parts, i, ref _foreground); break;
                case 39: _foreground = TerminalColor.Default; break;
                case >= 40 and <= 47: _background = code - 40; break;
                case 48: i = ExtendedColor(parts, i, ref _background); break;
                case 49: _background = TerminalColor.Default; break;
                case >= 90 and <= 97: _foreground = code - 90 + 8; break;
                case >= 100 and <= 107: _background = code - 100 + 8; break;
            }
        }
    }

    private void ResetStyle()
    {
        _foreground = _background = TerminalColor.Default;
        _style = CellStyle.None;
    }

    private static int ExtendedColor(string[] parts, int i, ref int target)
    {
        if (i + 1 >= parts.Length) return i;
        if (parts[i + 1] == "5" && i + 2 < parts.Length)
        {
            if (Number(parts[i + 2]) is { } index and <= 255) target = index;
            return i + 2;
        }
        if (parts[i + 1] == "2" && i + 4 < parts.Length)
        {
            target = TerminalColor.FromRgb(Number(parts[i + 2]) ?? 0, Number(parts[i + 3]) ?? 0, Number(parts[i + 4]) ?? 0);
            return i + 4;
        }
        return i;
    }

    private void ColonColor(string part)
    {
        var items = part.Split(':');
        if (items.Length < 3) return;
        bool foreground = items[0] == "38";
        if (!foreground && items[0] != "48") return;
        int color;
        if (items[1] == "5")
        {
            if (Number(items[2]) is not { } index || index > 255) return;
            color = index;
        }
        else if (items[1] == "2" && items.Length >= 5)
        {
            // « 38:2:r:g:b » ou, avec un espace de couleur vide, « 38:2::r:g:b ».
            int shift = items.Length >= 6 ? 1 : 0;
            color = TerminalColor.FromRgb(Number(items[2 + shift]) ?? 0, Number(items[3 + shift]) ?? 0, Number(items[4 + shift]) ?? 0);
        }
        else return;
        if (foreground) _foreground = color; else _background = color;
    }

    private static int? Number(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;

    // ---- OSC ---------------------------------------------------------------------------------------

    void IVtHandler.Osc(string text)
    {
        int separator = text.IndexOf(';');
        if (separator <= 0) return;
        var code = text[..separator];
        if (code is "0" or "2")
        {
            // Les octets ont été gardés un par un : on recompose l'UTF-8 du titre.
            Title = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(text[(separator + 1)..]));
            TitleChanged?.Invoke();
        }
    }
}
