// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TwinPanel.App.Views;

/// <summary>
/// Texte coupé par « … » au repos. Sous la souris, s'il ne rentre pas, il défile lentement jusqu'à sa fin puis revient.
/// Seulement sous la souris : vingt onglets qui bougent en permanence seraient illisibles.
/// </summary>
public sealed class ScrollingText : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ScrollingText, string?>(nameof(Text));

    private const double PixelsPerSecond = 45;
    private const double PauseSeconds = 0.8;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(33);

    private readonly TextBlock _text = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TranslateTransform _shift = new();
    private readonly DispatcherTimer _timer = new() { Interval = Tick };
    private readonly Stopwatch _clock = new();
    private Control? _host;
    private bool _scrolling;

    public ScrollingText()
    {
        ClipToBounds = true;
        _text.RenderTransform = _shift;
        LogicalChildren.Add(_text);
        VisualChildren.Add(_text);
        _timer.Tick += (_, _) => Advance();
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    static ScrollingText()
    {
        TextProperty.Changed.AddClassHandler<ScrollingText>((c, _) =>
        {
            c._text.Text = c.Text;
            c.RemeasureUpToList();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Toute la pastille compte, pas seulement le texte.
        _host = (Control?)this.FindAncestorOfType<ListBoxItem>() ?? this;
        _host.PointerEntered += OnEnter;
        _host.PointerExited += OnLeave;
        Dispatcher.UIThread.Post(RemeasureUpToList, DispatcherPriority.Loaded);
    }

    /// <summary>La largeur de la pastille dépend du texte : elle et ses parents doivent être remesurés avec lui.</summary>
    private void RemeasureUpToList()
    {
        InvalidateMeasure();
        foreach (var parent in this.GetVisualAncestors().OfType<Layoutable>())
        {
            parent.InvalidateMeasure();
            if (parent is ListBox) break;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_host is not null)
        {
            _host.PointerEntered -= OnEnter;
            _host.PointerExited -= OnLeave;
        }
        _host = null;
        Stop();
    }

    private void OnEnter(object? sender, PointerEventArgs e)
    {
        if (Overflow <= 1) return;
        _scrolling = true;
        _text.TextTrimming = TextTrimming.None;
        InvalidateArrange();
        _clock.Restart();
        _timer.Start();
    }

    private void OnLeave(object? sender, PointerEventArgs e) => Stop();

    private void Stop()
    {
        _timer.Stop();
        _scrolling = false;
        _shift.X = 0;
        _text.TextTrimming = TextTrimming.CharacterEllipsis;
        InvalidateArrange();
    }

    private double Overflow => _text.DesiredSize.Width - Bounds.Width;

    /// <summary>Un aller-retour : pause, vers la fin, pause, retour.</summary>
    private void Advance()
    {
        double travel = Overflow / PixelsPerSecond;
        if (travel <= 0) { Stop(); return; }
        double cycle = 2 * (PauseSeconds + travel);
        double t = _clock.Elapsed.TotalSeconds % cycle;
        double position = t < PauseSeconds ? 0
            : t < PauseSeconds + travel ? (t - PauseSeconds) / travel
            : t < 2 * PauseSeconds + travel ? 1
            : 1 - ((t - 2 * PauseSeconds - travel) / travel);
        _shift.X = -position * Overflow;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(Size.Infinity);
        return new Size(Math.Min(_text.DesiredSize.Width, availableSize.Width), _text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = _scrolling ? _text.DesiredSize.Width : finalSize.Width;
        _text.Arrange(new Rect(0, 0, width, finalSize.Height));
        return finalSize;
    }
}
