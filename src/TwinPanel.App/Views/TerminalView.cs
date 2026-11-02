// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using TwinPanel.Core.Terminal;

namespace TwinPanel.App.Views;

/// <summary>
/// Écran d'un terminal : dessine les cases de l'écran virtuel et envoie clavier et souris au programme.
/// Quand il a le focus, il garde les touches pour lui : les raccourcis des fichiers ne s'y déclenchent pas.
/// </summary>
public sealed class TerminalView : Control
{
    public static readonly StyledProperty<TerminalSession?> SessionProperty =
        AvaloniaProperty.Register<TerminalView, TerminalSession?>(nameof(Session));

    public static readonly StyledProperty<double> TerminalFontSizeProperty =
        AvaloniaProperty.Register<TerminalView, double>(nameof(TerminalFontSize), 13);

    private const double Inset = 6;
    private const int WheelLines = 3;
    private const int TextCacheLimit = 5000;
    private const string WordBreaks = " \t\"'`()[]{}<>|;,";

    private static readonly FontFamily Font = new("Cascadia Mono, Consolas, Courier New");
    private static readonly Typeface Regular = new(Font);
    private static readonly Typeface Bold = new(Font, weight: FontWeight.Bold);
    private static readonly Typeface Italic = new(Font, FontStyle.Italic);

    private static readonly Color[] DarkPalette = Colors("#1F2329", "#E5646E", "#7DBE6E", "#DDB35C", "#5FA8E8", "#C27CDC", "#4FBFC9", "#C5CBD3",
        "#5D6570", "#FF7F89", "#9BDC8A", "#F2CC78", "#82C0FF", "#DB9BF0", "#73DCE3", "#F3F5F7");
    private static readonly Color[] LightPalette = Colors("#2B2F36", "#C4303C", "#2F8132", "#94690A", "#1F66C2", "#8C35B0", "#0F7F8A", "#D4D7DC",
        "#6A717C", "#DE4853", "#3E9C43", "#AD7E12", "#3B80DB", "#A64FCA", "#1C97A3", "#F6F7F8");
    private static readonly Color DarkForeground = Color.Parse("#D8DEE6");
    private static readonly Color DarkBackground = Color.Parse("#16191E");
    private static readonly Color LightForeground = Color.Parse("#1F2329");
    private static readonly Color LightBackground = Color.Parse("#FBFBFC");
    private static readonly Color DarkSelection = Color.Parse("#3B6EA8");
    private static readonly Color LightSelection = Color.Parse("#A9CCF2");
    private const double MinimumContrast = 3;

    private readonly Dictionary<Color, IBrush> _brushes = [];
    private readonly Dictionary<(Color Foreground, Color Background), Color> _readable = [];
    private readonly Dictionary<(string Text, Color Color, CellStyle Style), FormattedText> _texts = [];
    private readonly StringBuilder _run = new();

    private double _cellWidth = 8;
    private double _cellHeight = 16;
    private int _pendingRefresh;
    private int _columns;
    private int _rows;

    /// <summary>Lignes remontées dans l'historique (0 : bas de l'écran).</summary>
    private int _scroll;
    private long _lastTotalLines;

    // Sélection en numéros absolus : elle reste en place quand le texte défile.
    private (long Line, int Column)? _selectionStart;
    private (long Line, int Column)? _selectionEnd;
    private bool _selecting;
    private (int Column, int Row) _lastMouseCell = (-1, -1);

    public TerminalView()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        ActualThemeVariantChanged += (_, _) =>
        {
            _texts.Clear();
            InvalidateVisual();
        };
        UpdateMetrics();
    }

    public TerminalSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public double TerminalFontSize
    {
        get => GetValue(TerminalFontSizeProperty);
        set => SetValue(TerminalFontSizeProperty, value);
    }

    /// <summary>Touche à laisser à l'application (afficher / masquer le terminal).</summary>
    public Func<KeyEventArgs, bool>? PassThrough { get; set; }

    /// <summary>Question avant de coller plusieurs lignes dans un programme qui les exécuterait aussitôt.</summary>
    public Func<string, Task<bool>>? ConfirmPaste { get; set; }

    /// <summary>Le nombre de colonnes ou de lignes a changé.</summary>
    public event Action<int, int>? GridChanged;

    public event Action<bool>? FocusChanged;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SessionProperty)
        {
            if (change.OldValue is TerminalSession old) old.Changed -= OnSessionChanged;
            if (change.NewValue is TerminalSession session) session.Changed += OnSessionChanged;
            _scroll = 0;
            ClearSelection();
            _lastTotalLines = TotalLines();
            UpdateGrid();
            InvalidateVisual();
        }
        else if (change.Property == TerminalFontSizeProperty)
        {
            _texts.Clear();
            UpdateMetrics();
            UpdateGrid();
            InvalidateVisual();
        }
        else if (change.Property == BoundsProperty) UpdateGrid();
        else if (change.Property == IsFocusedProperty)
        {
            FocusChanged?.Invoke(IsFocused);
            if (Session is { } session && ScreenValue(s => s.ReportFocus)) session.Send(IsFocused ? "\u001b[I" : "\u001b[O");
            InvalidateVisual();
        }
    }

    private long TotalLines() => Session is null ? 0 : ScreenValue(s => s.HistoryCount + s.HistoryDropped);

    private T ScreenValue<T>(Func<TerminalEmulator, T> read)
    {
        var session = Session!;
        lock (session.Gate) return read(session.Screen);
    }

    private void OnSessionChanged()
    {
        if (Interlocked.Exchange(ref _pendingRefresh, 1) != 0) return;
        // Priorité basse : un programme bavard ne doit pas bloquer le clavier.
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _pendingRefresh, 0);
            if (Session is null) return;
            long total = TotalLines();
            // Remonté dans l'historique : la vue ne bouge pas quand de nouvelles lignes arrivent.
            if (_scroll > 0) _scroll += (int)Math.Clamp(total - _lastTotalLines, 0, int.MaxValue / 2);
            _lastTotalLines = total;
            InvalidateVisual();
        }, DispatcherPriority.Background);
    }

    private void UpdateMetrics()
    {
        var sample = new FormattedText("M", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Regular, TerminalFontSize, Brushes.White);
        _cellWidth = Math.Max(1, sample.WidthIncludingTrailingWhitespace);
        _cellHeight = Math.Max(1, Math.Ceiling(sample.Height));
    }

    private void UpdateGrid()
    {
        if (Bounds.Width < _cellWidth * 2 + Inset * 2 || Bounds.Height < _cellHeight + Inset * 2) return;
        int columns = Math.Max(2, (int)((Bounds.Width - Inset * 2) / _cellWidth));
        int rows = Math.Max(1, (int)((Bounds.Height - Inset * 2) / _cellHeight));
        bool changed = columns != _columns || rows != _rows;
        _columns = columns;
        _rows = rows;
        if (Session is { } session && (changed || ScreenValue(s => s.Columns != columns || s.Rows != rows)))
        {
            ClearSelection();
            session.Resize(columns, rows);
            _lastTotalLines = TotalLines();
        }
        if (changed) GridChanged?.Invoke(columns, rows);
    }

    // ---- Dessin ------------------------------------------------------------------------------------

    private bool IsDark => ActualThemeVariant == ThemeVariant.Dark;

    private IBrush BrushOf(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush))
        {
            if (_brushes.Count > 4096) _brushes.Clear();
            brush = new ImmutableSolidColorBrush(color);
            _brushes[color] = brush;
        }
        return brush;
    }

    private Color ColorOf(int value, Color fallback)
    {
        if (value == TerminalColor.Default) return fallback;
        if (TerminalColor.IsRgb(value))
        {
            var (r, g, b) = TerminalColor.Rgb(value);
            return Color.FromRgb(r, g, b);
        }
        if (value < 16) return (IsDark ? DarkPalette : LightPalette)[value];
        var (xr, xg, xb) = TerminalColor.Extended(value);
        return Color.FromRgb(xr, xg, xb);
    }

    private (Color Foreground, Color Background) CellColors(in TerminalCell cell, bool reverse, bool selected)
    {
        var defaultForeground = IsDark ? DarkForeground : LightForeground;
        var defaultBackground = IsDark ? DarkBackground : LightBackground;
        var foreground = ColorOf(cell.Foreground, defaultForeground);
        var background = ColorOf(cell.Background, defaultBackground);
        if (cell.Style.HasFlag(CellStyle.Inverse) != reverse) (foreground, background) = (background, foreground);
        if (selected) return (IsDark ? Avalonia.Media.Colors.White : Avalonia.Media.Colors.Black, IsDark ? DarkSelection : LightSelection);
        if (cell.Style.HasFlag(CellStyle.Hidden)) return (background, background);
        foreground = Readable(foreground, background);
        if (cell.Style.HasFlag(CellStyle.Faint)) foreground = Color.FromArgb(160, foreground.R, foreground.G, foreground.B);
        return (foreground, background);
    }

    /// <summary>
    /// Un programme pense souvent à un fond noir : son gris clair serait invisible sur le thème clair.
    /// Un texte trop proche de son fond prend la couleur de texte lisible sur ce fond.
    /// </summary>
    private Color Readable(Color foreground, Color background)
    {
        if (_readable.TryGetValue((foreground, background), out var known)) return known;
        var text = (foreground.R, foreground.G, foreground.B);
        var back = (background.R, background.G, background.B);
        var color = TerminalColor.Contrast(text, back) >= MinimumContrast ? foreground
            : TerminalColor.Luminance(back) > 0.3 ? LightForeground : DarkForeground;
        if (_readable.Count > 4096) _readable.Clear();
        _readable[(foreground, background)] = color;
        return color;
    }

    public override void Render(DrawingContext context)
    {
        var reverseDefault = IsDark ? DarkBackground : LightBackground;
        if (Session is not { } session)
        {
            context.FillRectangle(BrushOf(reverseDefault), new Rect(Bounds.Size));
            return;
        }

        lock (session.Gate)
        {
            var screen = session.Screen;
            bool reverse = screen.ReverseVideo;
            context.FillRectangle(BrushOf(reverse ? (IsDark ? DarkForeground : LightForeground) : reverseDefault), new Rect(Bounds.Size));

            int scroll = screen.IsAlternateScreen ? 0 : Math.Min(_scroll, screen.HistoryCount);
            int top = screen.HistoryCount - scroll;
            for (int row = 0; row < screen.Rows; row++)
            {
                int index = top + row;
                var line = screen.LineAt(index);
                long absolute = index + screen.HistoryDropped;
                double y = Inset + row * _cellHeight;
                DrawBackgrounds(context, line, absolute, y, screen.Columns, reverse);
                DrawTexts(context, line, absolute, y, screen.Columns, reverse);
            }

            if (scroll == 0 && screen.CursorVisible) DrawCursor(context, screen);
            if (scroll > 0) DrawScrollMark(context, scroll, screen.HistoryCount);
        }
    }

    private void DrawBackgrounds(DrawingContext context, TerminalLine line, long absolute, double y, int columns, bool reverse)
    {
        var cells = line.Cells;
        var defaultBackground = reverse ? (IsDark ? DarkForeground : LightForeground) : (IsDark ? DarkBackground : LightBackground);
        int count = Math.Min(columns, cells.Length);
        int start = 0;
        Color current = defaultBackground;
        for (int x = 0; x <= count; x++)
        {
            var color = x < count ? CellColors(cells[x], reverse, IsSelected(absolute, x)).Background : defaultBackground;
            if (x < count && color == current) continue;
            if (current != defaultBackground)
                context.FillRectangle(BrushOf(current), new Rect(Inset + start * _cellWidth, y, (x - start) * _cellWidth, _cellHeight));
            start = x;
            current = color;
        }
    }

    private static bool IsAsciiText(string? text) => text is null || text.All(c => c < 0x7F);

    private void DrawTexts(DrawingContext context, TerminalLine line, long absolute, double y, int columns, bool reverse)
    {
        var cells = line.Cells;
        int count = Math.Min(columns, cells.Length);
        int x = 0;
        while (x < count)
        {
            ref var cell = ref cells[x];
            var decorated = (cell.Style & (CellStyle.Underline | CellStyle.Strike)) != 0;
            if (cell.Width == 0 || (!decorated && cell.Text is null or " ")) { x++; continue; }

            var (foreground, _) = CellColors(cell, reverse, IsSelected(absolute, x));
            var style = cell.Style;
            int start = x;
            if (!IsAsciiText(cell.Text))
            {
                // Largeur propre à chaque police : ces caractères sont placés un par un, sur leur case.
                int width = Math.Max(1, (int)cell.Width);
                DrawRun(context, cell.Text!, start, width, y, foreground, style);
                x += width;
                continue;
            }

            _run.Clear();
            while (x < count)
            {
                ref var next = ref cells[x];
                if (next.Width != 1 || next.Style != style || !IsAsciiText(next.Text)) break;
                if (CellColors(next, reverse, IsSelected(absolute, x)).Foreground != foreground) break;
                _run.Append(next.Text ?? " ");
                x++;
            }
            if (x == start) { x++; continue; }
            DrawRun(context, _run.ToString(), start, x - start, y, foreground, style);
        }
    }

    private void DrawRun(DrawingContext context, string text, int column, int width, double y, Color color, CellStyle style)
    {
        double left = Inset + column * _cellWidth;
        var drawable = text.TrimEnd(' ');
        if (drawable.Length > 0) context.DrawText(TextOf(drawable, color, style), new Point(left, y));
        if ((style & (CellStyle.Underline | CellStyle.Strike)) == 0) return;
        var pen = new Pen(BrushOf(color), 1);
        double right = left + width * _cellWidth;
        if (style.HasFlag(CellStyle.Underline)) context.DrawLine(pen, new Point(left, y + _cellHeight - 1.5), new Point(right, y + _cellHeight - 1.5));
        if (style.HasFlag(CellStyle.Strike)) context.DrawLine(pen, new Point(left, y + _cellHeight / 2), new Point(right, y + _cellHeight / 2));
    }

    private FormattedText TextOf(string text, Color color, CellStyle style)
    {
        var key = (text, color, style & (CellStyle.Bold | CellStyle.Italic));
        if (_texts.TryGetValue(key, out var found)) return found;
        if (_texts.Count >= TextCacheLimit) _texts.Clear();
        var typeface = style.HasFlag(CellStyle.Bold) ? Bold : style.HasFlag(CellStyle.Italic) ? Italic : Regular;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, TerminalFontSize, BrushOf(color));
        _texts[key] = formatted;
        return formatted;
    }

    private void DrawCursor(DrawingContext context, TerminalEmulator screen)
    {
        double x = Inset + screen.CursorX * _cellWidth;
        double y = Inset + screen.CursorY * _cellHeight;
        var cell = screen.ScreenLine(screen.CursorY).Cells[screen.CursorX];
        int width = Math.Max(1, (int)cell.Width);
        var color = IsDark ? DarkForeground : LightForeground;
        var rect = new Rect(x, y, width * _cellWidth, _cellHeight);

        if (!IsFocused)
        {
            context.DrawRectangle(new Pen(BrushOf(color), 1), rect.Deflate(0.5));
            return;
        }
        switch (screen.CursorShape)
        {
            case 1: context.FillRectangle(BrushOf(color), new Rect(x, y + _cellHeight - 2, rect.Width, 2)); break;
            case 2: context.FillRectangle(BrushOf(color), new Rect(x, y, 2, _cellHeight)); break;
            default:
                context.FillRectangle(BrushOf(color), rect);
                if (cell.Text is { } text and not " ")
                    context.DrawText(TextOf(text, IsDark ? DarkBackground : LightBackground, cell.Style), new Point(x, y));
                break;
        }
    }

    /// <summary>Repère de position quand on regarde l'historique.</summary>
    private void DrawScrollMark(DrawingContext context, int scroll, int history)
    {
        var text = new FormattedText($"↑ {scroll} / {history}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Regular, TerminalFontSize - 1, Brushes.White);
        var box = new Rect(Bounds.Width - text.Width - 18, 4, text.Width + 12, text.Height + 4);
        context.FillRectangle(BrushOf(Color.FromArgb(200, 60, 90, 130)), box, 4);
        context.DrawText(text, new Point(box.X + 6, box.Y + 2));
    }

    // ---- Sélection ---------------------------------------------------------------------------------

    private bool HasSelection => _selectionStart is { } a && _selectionEnd is { } b && a != b;

    private ((long Line, int Column) From, (long Line, int Column) To)? OrderedSelection()
    {
        if (_selectionStart is not { } a || _selectionEnd is not { } b || a == b) return null;
        return a.CompareTo(b) <= 0 ? (a, b) : (b, a);
    }

    private bool IsSelected(long line, int column)
    {
        if (OrderedSelection() is not { } range) return false;
        var position = (line, column);
        return position.CompareTo(range.From) >= 0 && position.CompareTo(range.To) < 0;
    }

    private void ClearSelection()
    {
        if (_selectionStart is null && _selectionEnd is null) return;
        _selectionStart = _selectionEnd = null;
        InvalidateVisual();
    }

    private (long Line, int Column) PositionAt(Point point, TerminalEmulator screen)
    {
        int scroll = screen.IsAlternateScreen ? 0 : Math.Min(_scroll, screen.HistoryCount);
        int row = Math.Clamp((int)((point.Y - Inset) / _cellHeight), 0, screen.Rows - 1);
        // La moitié droite d'une case compte pour la case suivante : on sélectionne comme dans un texte.
        int column = Math.Clamp((int)Math.Round((point.X - Inset) / _cellWidth), 0, screen.Columns);
        return (screen.HistoryCount - scroll + row + screen.HistoryDropped, column);
    }

    private void SelectAround((long Line, int Column) position, TerminalEmulator screen, bool wholeLine)
    {
        int index = (int)(position.Line - screen.HistoryDropped);
        var cells = screen.LineAt(index).Cells;
        int start = 0, end = screen.Columns;
        if (!wholeLine)
        {
            int column = Math.Clamp(position.Column, 0, cells.Length - 1);
            if (IsBreak(cells[column])) return;
            start = column;
            while (start > 0 && !IsBreak(cells[start - 1])) start--;
            end = column;
            while (end < cells.Length && !IsBreak(cells[end])) end++;
        }
        _selectionStart = (position.Line, start);
        _selectionEnd = (position.Line, end);

        static bool IsBreak(in TerminalCell c) => c.Width != 0 && (c.Text is null || WordBreaks.Contains(c.Text[0], StringComparison.Ordinal));
    }

    private async Task CopySelectionAsync()
    {
        if (OrderedSelection() is not { } range || Session is not { } session) return;
        string text;
        lock (session.Gate)
        {
            var screen = session.Screen;
            long dropped = screen.HistoryDropped;
            text = screen.TextBetween((int)Math.Max(0, range.From.Line - dropped), range.From.Column, (int)Math.Max(0, range.To.Line - dropped), range.To.Column);
        }
        ClearSelection();
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    private async Task PasteAsync()
    {
        if (Session is not { } session || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text)) return;
        bool bracketed = ScreenValue(s => s.BracketedPaste);
        // Un programme qui ne sait pas reconnaître un collage exécuterait chaque ligne aussitôt.
        if (!bracketed && TerminalKeys.IsMultiline(text) && ConfirmPaste is { } confirm && !await confirm(text)) return;
        SendInput(TerminalKeys.Paste(text, bracketed));
    }

    private void SendInput(string text)
    {
        if (Session is not { } session) return;
        if (_scroll != 0)
        {
            _scroll = 0;
            InvalidateVisual();
        }
        ClearSelection();
        session.Send(text);
    }

    private void ScrollBy(int lines)
    {
        if (Session is not { } session) return;
        int history = ScreenValue(s => s.IsAlternateScreen ? 0 : s.HistoryCount);
        int scroll = Math.Clamp(_scroll + lines, 0, history);
        if (scroll == _scroll) return;
        _scroll = scroll;
        InvalidateVisual();
    }

    // ---- Clavier -----------------------------------------------------------------------------------

    private static TerminalKey? SpecialKey(Key key) => key switch
    {
        Key.Up => TerminalKey.Up,
        Key.Down => TerminalKey.Down,
        Key.Right => TerminalKey.Right,
        Key.Left => TerminalKey.Left,
        Key.Home => TerminalKey.Home,
        Key.End => TerminalKey.End,
        Key.Insert => TerminalKey.Insert,
        Key.Delete => TerminalKey.Delete,
        Key.PageUp => TerminalKey.PageUp,
        Key.PageDown => TerminalKey.PageDown,
        Key.Enter => TerminalKey.Enter,
        Key.Tab => TerminalKey.Tab,
        Key.Back => TerminalKey.Backspace,
        Key.Escape => TerminalKey.Escape,
        >= Key.F1 and <= Key.F12 => TerminalKey.F1 + (key - Key.F1),
        _ => null,
    };

    private static TerminalModifiers ModifiersOf(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? TerminalModifiers.Shift : TerminalModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? TerminalModifiers.Alt : TerminalModifiers.None)
        | (modifiers.HasFlag(KeyModifiers.Control) ? TerminalModifiers.Control : TerminalModifiers.None);

    private static char? ControlKeyCharacter(KeyEventArgs e) => e.Key switch
    {
        >= Key.A and <= Key.Z => (char)('A' + (e.Key - Key.A)),
        Key.Space => ' ',
        >= Key.D2 and <= Key.D8 => (char)('2' + (e.Key - Key.D2)),
        _ => e.KeySymbol is { Length: > 0 } symbol ? symbol[0] : null,
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Session is null || e.Handled) return;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        if (e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.Alt) return;
        if (PassThrough?.Invoke(e) == true) return;

        bool control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (control && !alt)
        {
            // Ctrl+C : copie s'il y a une sélection, sinon interrompt le programme. Ctrl+V colle toujours.
            if (e.Key == Key.C && (shift || HasSelection))
            {
                e.Handled = true;
                _ = CopySelectionAsync();
                return;
            }
            if (e.Key == Key.V)
            {
                e.Handled = true;
                _ = PasteAsync();
                return;
            }
        }
        if (shift && !control && !alt)
        {
            if (e.Key == Key.Insert)
            {
                e.Handled = true;
                _ = PasteAsync();
                return;
            }
            if (e.Key is Key.PageUp or Key.PageDown)
            {
                e.Handled = true;
                int page = Math.Max(1, _rows - 1);
                ScrollBy(e.Key == Key.PageUp ? page : -page);
                return;
            }
        }

        if (SpecialKey(e.Key) is { } special)
        {
            e.Handled = true;
            if (TerminalKeys.Encode(special, ModifiersOf(e.KeyModifiers), ScreenValue(s => s.ApplicationCursorKeys)) is { } sequence) SendInput(sequence);
            return;
        }

        // Ctrl+Alt : c'est AltGr (@, #, €…), le caractère arrive par la saisie de texte.
        if (control && alt) return;
        if (control)
        {
            e.Handled = true;
            if (ControlKeyCharacter(e) is { } c && TerminalKeys.ControlCharacter(c) is { } text) SendInput(text);
        }
        else if (alt && e.KeySymbol is { Length: > 0 } symbol)
        {
            e.Handled = true;
            SendInput("\u001b" + symbol);
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (Session is null || string.IsNullOrEmpty(e.Text)) return;
        e.Handled = true;
        SendInput(e.Text);
    }

    // ---- Souris ------------------------------------------------------------------------------------

    /// <summary>Le programme veut la souris (vim, htop…). Maj la garde pour sélectionner du texte.</summary>
    private bool ProgramWantsMouse(KeyModifiers modifiers) =>
        Session is not null && !modifiers.HasFlag(KeyModifiers.Shift) && ScreenValue(s => s.Mouse != MouseTracking.None);

    private (int Column, int Row) CellAt(Point point) => (
        Math.Clamp((int)((point.X - Inset) / _cellWidth), 0, Math.Max(0, _columns - 1)),
        Math.Clamp((int)((point.Y - Inset) / _cellHeight), 0, Math.Max(0, _rows - 1)));

    private void SendMouse(TerminalMouseButton button, TerminalMouseAction action, Point point, KeyModifiers modifiers)
    {
        if (Session is not { } session) return;
        var (column, row) = CellAt(point);
        var (tracking, sgr) = ScreenValue(s => (s.Mouse, s.MouseSgr));
        if (TerminalKeys.Mouse(button, action, column, row, ModifiersOf(modifiers), tracking, sgr) is { } report) session.Send(report);
    }

    private static TerminalMouseButton ButtonOf(PointerPointProperties p) =>
        p.IsLeftButtonPressed ? TerminalMouseButton.Left
        : p.IsMiddleButtonPressed ? TerminalMouseButton.Middle
        : p.IsRightButtonPressed ? TerminalMouseButton.Right
        : TerminalMouseButton.None;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Handled = true;
        if (Session is not { } session) return;
        var point = e.GetCurrentPoint(this);

        if (ProgramWantsMouse(e.KeyModifiers))
        {
            SendMouse(ButtonOf(point.Properties), TerminalMouseAction.Press, point.Position, e.KeyModifiers);
            return;
        }
        if (point.Properties.IsRightButtonPressed)
        {
            // Comme une console Windows : copie s'il y a une sélection, sinon colle.
            _ = HasSelection ? CopySelectionAsync() : PasteAsync();
            return;
        }
        if (!point.Properties.IsLeftButtonPressed) return;

        lock (session.Gate)
        {
            var position = PositionAt(point.Position, session.Screen);
            ClearSelection();
            if (e.ClickCount >= 2)
            {
                SelectAround((position.Line, CellAt(point.Position).Column), session.Screen, e.ClickCount >= 3);
                InvalidateVisual();
                return;
            }
            _selectionStart = _selectionEnd = position;
            _selecting = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Session is not { } session) return;
        var point = e.GetCurrentPoint(this);

        if (ProgramWantsMouse(e.KeyModifiers))
        {
            var cell = CellAt(point.Position);
            if (cell == _lastMouseCell) return;
            _lastMouseCell = cell;
            SendMouse(ButtonOf(point.Properties), TerminalMouseAction.Move, point.Position, e.KeyModifiers);
            return;
        }
        if (!_selecting) return;

        if (point.Position.Y < 0) ScrollBy(1);
        else if (point.Position.Y > Bounds.Height) ScrollBy(-1);
        lock (session.Gate) _selectionEnd = PositionAt(point.Position, session.Screen);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_selecting)
        {
            _selecting = false;
            return;
        }
        if (ProgramWantsMouse(e.KeyModifiers))
        {
            var button = e.InitialPressMouseButton switch
            {
                MouseButton.Left => TerminalMouseButton.Left,
                MouseButton.Middle => TerminalMouseButton.Middle,
                MouseButton.Right => TerminalMouseButton.Right,
                _ => TerminalMouseButton.None,
            };
            SendMouse(button, TerminalMouseAction.Release, e.GetPosition(this), e.KeyModifiers);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Session is null) return;
        e.Handled = true;
        bool up = e.Delta.Y > 0;

        if (ProgramWantsMouse(e.KeyModifiers))
        {
            SendMouse(up ? TerminalMouseButton.WheelUp : TerminalMouseButton.WheelDown, TerminalMouseAction.Press, e.GetPosition(this), e.KeyModifiers);
            return;
        }
        var (alternate, application) = ScreenValue(s => (s.IsAlternateScreen, s.ApplicationCursorKeys));
        if (alternate)
        {
            // Second écran sans souris (less, man…) : pas d'historique ici, la molette fait les flèches.
            var key = TerminalKeys.Encode(up ? TerminalKey.Up : TerminalKey.Down, TerminalModifiers.None, application);
            for (int i = 0; i < WheelLines; i++) SendInput(key!);
            return;
        }
        ScrollBy(up ? WheelLines : -WheelLines);
    }

    private static Color[] Colors(params string[] values) => [.. values.Select(Color.Parse)];
}
