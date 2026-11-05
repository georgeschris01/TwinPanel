// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using TwinPanel.App.Converters;
using TwinPanel.App.Services;
using TwinPanel.App.ViewModels;
using TwinPanel.Core;
using TwinPanel.Core.Media;

namespace TwinPanel.App.Views;

/// <summary>
/// Vue Miniatures d'un onglet. Tout est dessiné ici et seules les cases visibles existent à l'écran :
/// un dossier de milliers de fichiers reste fluide. Clavier et souris sont gérés par <see cref="PanelView"/>,
/// avec le même code que la liste ; cette vue dit seulement quel élément se trouve sous un point.
/// </summary>
public sealed class ThumbnailView : Control
{
    private const double Gap = 6, TextLines = 2;
    private static readonly IBrush MarkedBrush = new SolidColorBrush(Color.Parse("#E5534B"));

    private FolderTabViewModel? _tab;
    private IReadOnlyList<FileItemViewModel> _items = [];
    private ScrollBar? _bar;
    private double _offset;
    private double _size = 160;
    private double _fontSize = 13;
    private long _maxServerBytes = ThumbnailRules.DefaultMaxRemoteBytes;
    private readonly HashSet<FileItemViewModel> _watched = [];

    public ThumbnailView()
    {
        Focusable = true;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThumbnailLoader.ThumbnailReady += OnThumbnailReady;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThumbnailLoader.ThumbnailReady -= OnThumbnailReady;
        ThumbnailLoader.Request(this, []);
    }

    private void OnThumbnailReady(object? sender, EventArgs e)
    {
        if (IsEffectivelyVisible) InvalidateVisual();
    }

    /// <summary>
    /// Côté des images et taille du texte, en points d'écran ; poids maximum d'une image de serveur (négatif : aucune).
    /// Le curseur reste à l'écran.
    /// </summary>
    public void Configure(double size, double fontSize, long maxServerBytes)
    {
        _maxServerBytes = maxServerBytes;
        _size = Math.Clamp(size, 48, 512);
        _fontSize = fontSize;
        UpdateScroll();
        if (_tab?.CurrentItem is { } current) Reveal(current);
        InvalidateVisual();
    }

    public void AttachScrollBar(ScrollBar bar)
    {
        _bar = bar;
        bar.Orientation = Avalonia.Layout.Orientation.Vertical;
        bar.Minimum = 0;
        bar.ValueChanged += (_, e) => Offset = e.NewValue;
        UpdateScroll();
    }

    public void SetTab(FolderTabViewModel? tab)
    {
        if (_tab is not null) _tab.Items.CollectionChanged -= OnItemsChanged;
        _tab = tab;
        _items = tab is null ? [] : tab.Items;
        if (tab is null) ThumbnailLoader.Request(this, []); // plus rien à montrer : on cesse de lire
        if (_tab is not null) _tab.Items.CollectionChanged += OnItemsChanged;
        _offset = 0;
        UpdateScroll();
        InvalidateVisual();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset) _offset = 0;
        UpdateScroll();
        InvalidateVisual();
    }

    // ---- Géométrie ---------------------------------------------------------------------------

    private double CellWidth => _size + 2 * Gap;
    private double CellHeight => _size + 3 * Gap + TextLines * _fontSize * 1.35;

    /// <summary>Nombre de cases par rangée (flèches gauche / droite : ±1, haut / bas : ± ce nombre).</summary>
    public int Columns => Math.Max(1, (int)(Bounds.Width / CellWidth));

    /// <summary>Nombre de cases visibles : Page précédente / suivante.</summary>
    public int PageItems => Columns * Math.Max(1, (int)(Bounds.Height / CellHeight));

    private double SpareWidth => Math.Max(0, (Bounds.Width - Columns * CellWidth) / Columns);
    private double MaxOffset => Math.Max(0, Math.Ceiling(_items.Count / (double)Columns) * CellHeight - Bounds.Height);

    private Rect CellRect(int index)
    {
        int row = index / Columns, column = index % Columns;
        double width = CellWidth + SpareWidth;
        return new Rect(column * width, row * CellHeight - _offset, width, CellHeight);
    }

    /// <summary>Élément sous un point (relatif à cette vue) ; null dans le vide.</summary>
    public FileItemViewModel? ItemAt(Point p)
    {
        if (p.X < 0 || p.Y < 0 || p.X >= Bounds.Width || p.Y >= Bounds.Height) return null;
        int row = (int)((p.Y + _offset) / CellHeight);
        int column = Math.Min(Columns - 1, (int)(p.X / (CellWidth + SpareWidth)));
        int index = row * Columns + column;
        return index >= 0 && index < _items.Count ? _items[index] : null;
    }

    private double Offset
    {
        get => _offset;
        set
        {
            var clamped = Math.Clamp(value, 0, MaxOffset);
            if (Math.Abs(clamped - _offset) < 0.5) return;
            _offset = clamped;
            if (_bar is not null && Math.Abs(_bar.Value - clamped) >= 0.5) _bar.Value = clamped;
            InvalidateVisual();
        }
    }

    private void UpdateScroll()
    {
        _offset = Math.Clamp(_offset, 0, MaxOffset);
        if (_bar is null) return;
        _bar.Maximum = MaxOffset;
        _bar.ViewportSize = Bounds.Height;
        _bar.SmallChange = CellHeight / 2;
        _bar.LargeChange = Math.Max(CellHeight, Bounds.Height - CellHeight);
        _bar.Value = _offset;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateScroll();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled) return;
        Offset -= e.Delta.Y * CellHeight / 2;
        e.Handled = true;
    }

    /// <summary>Le chemin complet dans une bulle : le nom peut être coupé sous l'image.</summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var tip = ItemAt(e.GetPosition(this)) is { IsParentLink: false } item ? item.FullPath : null;
        if (!Equals(ToolTip.GetTip(this), tip)) ToolTip.SetTip(this, tip);
    }

    /// <summary>Amène l'élément à l'écran (curseur déplacé).</summary>
    public void Reveal(FileItemViewModel item)
    {
        if (_tab is null) return;
        int index = _tab.Items.IndexOf(item);
        if (index < 0) return;
        double top = index / Columns * CellHeight;
        if (top < _offset) Offset = top;
        else if (top + CellHeight > _offset + Bounds.Height) Offset = top + CellHeight - Bounds.Height;
        InvalidateVisual();
    }

    // ---- Dessin ------------------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // le vide reçoit aussi les clics

        var visible = new List<FileItemViewModel>();
        var jobs = new List<ThumbnailLoader.Job>();
        if (_tab is not null && _items.Count > 0 && Bounds.Height > 0)
        {
            int first = (int)(_offset / CellHeight) * Columns;
            int last = Math.Min(_items.Count - 1, ((int)((_offset + Bounds.Height) / CellHeight) + 1) * Columns - 1);
            var kind = _tab.IsRemote ? ThumbnailSourceKind.Remote : ThumbnailSourceKind.Local;
            bool insideArchive = _tab.IsInsideArchive;
            int pixels = (int)Math.Round(_size * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
            var foreground = TextElement.GetForeground(this) ?? Brushes.Gray;
            var culture = CultureInfo.CurrentCulture;
            var typeface = new Typeface(TextElement.GetFontFamily(this));

            for (int i = first; i <= last; i++)
            {
                var item = _items[i];
                visible.Add(item);
                var cell = CellRect(i);
                var box = new Rect(cell.X + Gap + (cell.Width - CellWidth) / 2, cell.Y + Gap, _size, _size);
                DrawSelection(context, item, cell);

                string? key = null;
                if (ThumbnailRules.CanHaveThumbnail(item.Entry, kind, insideArchive, _maxServerBytes))
                {
                    key = ThumbnailRules.Key(item.Entry, pixels);
                }
                var bitmap = key is null ? null : ThumbnailLoader.Peek(key);
                if (bitmap is not null) DrawFitted(context, bitmap, box, enlarge: false);
                else
                {
                    DrawIcon(context, item, box);
                    if (key is not null) jobs.Add(new ThumbnailLoader.Job(key, item.Entry, pixels, kind));
                }

                var text = new FormattedText(item.IsParentLink ? ".." : item.Name, culture, FlowDirection.LeftToRight,
                    item.IsMarked ? new Typeface(typeface.FontFamily, weight: FontWeight.SemiBold) : typeface, _fontSize,
                    item.IsMarked ? MarkedBrush : item.HasNameColor ? item.NameColor : foreground)
                {
                    MaxTextWidth = cell.Width - 2 * Gap,
                    MaxLineCount = (int)TextLines,
                    TextAlignment = TextAlignment.Center,
                    Trimming = TextTrimming.CharacterEllipsis,
                };
                context.DrawText(text, new Point(cell.X + Gap, box.Bottom + Gap));
            }
        }
        Watch(visible);
        // Après le dessin : seul ce qui est à l'écran est demandé, les demandes précédentes sont oubliées.
        Dispatcher.UIThread.Post(() => ThumbnailLoader.Request(this, jobs), DispatcherPriority.Background);
    }

    private void DrawSelection(DrawingContext context, FileItemViewModel item, Rect cell)
    {
        if (!ReferenceEquals(item, _tab?.CurrentItem)) return;
        var chip = this.TryFindResource("ChipBackground", ActualThemeVariant, out var found) && found is IBrush b ? b : Brushes.Gray;
        var accent = this.TryFindResource("SystemControlHighlightAccentBrush", ActualThemeVariant, out var a) && a is IBrush c ? c : Brushes.DodgerBlue;
        context.DrawRectangle(chip, new Pen(accent), new RoundedRect(cell.Deflate(2), 6));
    }

    /// <summary>L'image entière, centrée, sans la déformer ; une petite image garde sa taille sauf avec « enlarge ».</summary>
    private static void DrawFitted(DrawingContext context, IImage image, Rect box, bool enlarge)
    {
        var size = image.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        double scale = Math.Min(box.Width / size.Width, box.Height / size.Height);
        if (!enlarge) scale = Math.Min(scale, 1);
        var fitted = new Size(size.Width * scale, size.Height * scale);
        var target = new Rect(box.X + (box.Width - fitted.Width) / 2, box.Y + (box.Height - fitted.Height) / 2, fitted.Width, fitted.Height);
        context.DrawImage(image, new Rect(size), target);
    }

    private static void DrawIcon(DrawingContext context, FileItemViewModel item, Rect box)
    {
        var area = box.CenterRect(new Rect(0, 0, box.Width / 2, box.Height / 2));
        if (item.ShellIcon is { } shell) { DrawFitted(context, shell, area, enlarge: true); return; }
        if (CategoryToGeometryConverter.Instance.Convert(item.Category, typeof(Geometry), null, CultureInfo.InvariantCulture) is not Geometry shape) return;
        var bounds = shape.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        double scale = Math.Min(area.Width / bounds.Width, area.Height / bounds.Height);
        var move = Matrix.CreateTranslation(-bounds.Center.X, -bounds.Center.Y) * Matrix.CreateScale(scale, scale)
                   * Matrix.CreateTranslation(area.Center.X, area.Center.Y);
        using (context.PushTransform(move)) context.DrawGeometry(item.IconBrush, null, shape);
    }

    /// <summary>Une case visible marquée ou démarquée se redessine (seules les visibles sont suivies).</summary>
    private void Watch(List<FileItemViewModel> visible)
    {
        var now = visible.ToHashSet();
        foreach (var old in _watched.Where(i => !now.Contains(i)).ToList())
        {
            old.PropertyChanged -= OnItemChanged;
            _watched.Remove(old);
        }
        foreach (var item in now)
            if (_watched.Add(item)) item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileItemViewModel.IsMarked) or nameof(FileItemViewModel.ShellIcon)) InvalidateVisual();
    }
}
