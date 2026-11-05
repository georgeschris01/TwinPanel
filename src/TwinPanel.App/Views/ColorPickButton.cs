// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace TwinPanel.App.Views;

/// <summary>Palette commune aux sélecteurs de couleur : couleurs prêtes + « Mes couleurs » (ajoutées avec la roue).</summary>
public static class ColorPalette
{
    public static IReadOnlyList<string> Standard { get; } =
    [
        "#E5534B", "#F47067", "#F778BA", "#DB61A2", "#A371F7", "#8256D0", "#6E7BF2", "#4F8EF7",
        "#2F81F7", "#39C5CF", "#1B9AAA", "#3FB68B", "#57AB5A", "#2DA44E", "#9BD15B", "#D4C52B",
        "#F2B84B", "#D29922", "#E08A3C", "#BC4C00", "#A0522D", "#8B949E", "#6E7681", "#C9D1D9",
        "#FFFFFF", "#000000",
    ];

    public const int MaxCustom = 24;

    /// <summary>Couleurs de l'utilisateur (enregistrées avec les Options).</summary>
    public static ObservableCollection<string> Custom { get; } = [];

    public static void SetCustom(IEnumerable<string> colors)
    {
        Custom.Clear();
        foreach (var c in colors.Where(TwinPanel.Core.Customization.FileColorRules.IsColor).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxCustom))
            Custom.Add(c.ToUpperInvariant());
    }

    public static void AddCustom(string hex)
    {
        if (!TwinPanel.Core.Customization.FileColorRules.IsColor(hex)) return;
        hex = hex.ToUpperInvariant();
        if (Standard.Contains(hex, StringComparer.OrdinalIgnoreCase)) return;
        var existing = Custom.FirstOrDefault(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) Custom.Remove(existing);
        Custom.Insert(0, hex);
        while (Custom.Count > MaxCustom) Custom.RemoveAt(Custom.Count - 1);
    }

    public static string ToHex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
}

/// <summary>
/// Bouton « pastille + code » ; un clic ouvre la palette, « Mes couleurs », la roue chromatique et le code #RRGGBB.
/// <see cref="Hex"/> vide = pas de couleur (couleur par défaut).
/// </summary>
public sealed class ColorPickButton : UserControl
{
    public static readonly StyledProperty<string?> HexProperty =
        AvaloniaProperty.Register<ColorPickButton, string?>(nameof(Hex), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Texte quand aucune couleur n'est choisie (« défaut », « d'origine »…).</summary>
    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<ColorPickButton, string>(nameof(EmptyText), L.T("(défaut)"));

    public string? Hex { get => GetValue(HexProperty); set => SetValue(HexProperty, value); }
    public string EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }

    // Contour gris semi-transparent : visible en thème clair comme sombre (même autour du blanc et du noir).
    private static readonly IBrush Outline = new SolidColorBrush(Color.Parse("#808B949E")).ToImmutable();

    private readonly Border _swatch;
    private readonly TextBlock _label;
    private readonly Button _button;
    private readonly Flyout _flyout;

    public ColorPickButton()
    {
        _swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = Outline };
        _label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 };
        _button = new Button
        {
            Padding = new Thickness(6, 3),
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _swatch, _label } },
        };
        ToolTip.SetTip(_button, L.T("Choisir une couleur : palette, roue chromatique ou code #RRGGBB"));
        _flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        _flyout.Opening += (_, _) => _flyout.Content = BuildPicker();
        _button.Flyout = _flyout;
        Content = _button;
        Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HexProperty || change.Property == EmptyTextProperty) Refresh();
    }

    private static bool IsColor(string? hex) => TwinPanel.Core.Customization.FileColorRules.IsColor(hex ?? string.Empty);

    private void Refresh()
    {
        bool has = IsColor(Hex);
        _swatch.Background = has ? new SolidColorBrush(Color.Parse(Hex!)) : Brushes.Transparent;
        _label.Text = has ? Hex!.ToUpperInvariant() : (string.IsNullOrEmpty(Hex) ? EmptyText : Hex + " ?");
    }

    private void Pick(string hex)
    {
        Hex = hex;
        _flyout.Hide();
    }

    private Control BuildPicker()
    {
        var root = new StackPanel { Spacing = 6, Width = 360 };

        WrapPanel Swatches(IEnumerable<string> colors)
        {
            var wrap = new WrapPanel();
            foreach (var c in colors)
            {
                var b = new Button
                {
                    Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4),
                    CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.Parse(c)), BorderThickness = new Thickness(1),
                    BorderBrush = Outline,
                };
                ToolTip.SetTip(b, c);
                var hex = c;
                b.Click += (_, _) => Pick(hex);
                wrap.Children.Add(b);
            }
            return wrap;
        }

        root.Children.Add(new TextBlock { Text = "Palette", Classes = { "subtle" } });
        root.Children.Add(Swatches(ColorPalette.Standard));
        if (ColorPalette.Custom.Count > 0)
        {
            root.Children.Add(new TextBlock { Text = L.T("Mes couleurs"), Classes = { "subtle" } });
            root.Children.Add(Swatches(ColorPalette.Custom));
        }

        root.Children.Add(new TextBlock { Text = L.T("Roue chromatique"), Classes = { "subtle" }, Margin = new Thickness(0, 4, 0, 0) });
        var start = IsColor(Hex) ? Color.Parse(Hex!) : Color.Parse("#4F8EF7");
        var wheel = new ColorView
        {
            Color = start,
            ColorSpectrumShape = ColorSpectrumShape.Ring,
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsColorPaletteVisible = false,
            IsColorComponentsVisible = false,
            IsColorModelVisible = false,
            IsHexInputVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        root.Children.Add(wheel);

        var preview = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(start) };
        var code = new TextBox { Text = ColorPalette.ToHex(start), Width = 110, PlaceholderText = "#RRGGBB" };
        bool syncing = false;
        wheel.PropertyChanged += (_, e) =>
        {
            if (e.Property != ColorView.ColorProperty || syncing) return;
            syncing = true;
            preview.Background = new SolidColorBrush(wheel.Color);
            code.Text = ColorPalette.ToHex(wheel.Color);
            syncing = false;
        };
        code.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var t = (code.Text ?? string.Empty).Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if (!IsColor(t)) return;
            syncing = true;
            var c = Color.Parse(t);
            wheel.Color = c;
            preview.Background = new SolidColorBrush(c);
            syncing = false;
        };

        var use = new Button { Content = L.T("Utiliser cette couleur"), Classes = { "accent" } };
        use.Click += (_, _) =>
        {
            var hex = ColorPalette.ToHex(wheel.Color);
            ColorPalette.AddCustom(hex); // gardée dans « Mes couleurs »
            Pick(hex);
        };
        var none = new Button { Content = EmptyText.Trim('(', ')') is { Length: > 0 } e ? L.F("Aucune ({0})", e) : L.T("Aucune") };
        none.Click += (_, _) => Pick(string.Empty);

        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { preview, code },
        });
        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0),
            Children = { use, none },
        });
        return root;
    }
}
