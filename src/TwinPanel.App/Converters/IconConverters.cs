// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TwinPanel.Core;

namespace TwinPanel.App.Converters;

/// <summary>Icônes vectorielles dessinées pour le projet (aucune ressource externe).</summary>
public static class FileIcons
{
    public static readonly Geometry Folder = Geometry.Parse(
        "M3,6 C3,4.9 3.9,4 5,4 L9.6,4 L11.6,6 L19,6 C20.1,6 21,6.9 21,8 L21,18 C21,19.1 20.1,20 19,20 L5,20 C3.9,20 3,19.1 3,18 Z");
    public static readonly Geometry File = Geometry.Parse(
        "F0 M6,2 L14,2 L20,8 L20,20 C20,21.1 19.1,22 18,22 L6,22 C4.9,22 4,21.1 4,20 L4,4 C4,2.9 4.9,2 6,2 Z M13,3.5 L13,9 L18.5,9 Z");
    public static readonly Geometry Parent = Geometry.Parse(
        "M11,20 L11,7.8 L5.4,13.4 L4,12 L12,4 L20,12 L18.6,13.4 L13,7.8 L13,20 Z");

    private static TwinPanel.Core.Customization.FileColorRules _rules = new(new TwinPanel.Core.Settings.AppearanceSettings());

    /// <summary>Couleurs et options d'apparence choisies dans les Options.</summary>
    public static TwinPanel.Core.Customization.FileColorRules Rules => _rules;
    public static bool ColorNames { get; private set; }

    public static void Configure(TwinPanel.Core.Settings.AppearanceSettings appearance)
    {
        _rules = new TwinPanel.Core.Customization.FileColorRules(appearance);
        ColorNames = appearance.ColorNames;
    }

    /// <summary>Couleur d'un fichier précis : règle par masque, sinon couleur (éventuellement personnalisée) de son type.</summary>
    public static IBrush BrushFor(FileEntry entry) =>
        _rules.RuleColor(entry) is { } hex ? Brush(hex) : BrushFor(entry.Category);

    public static IBrush BrushFor(FileCategory c) =>
        _rules.CategoryColor(c) is { } custom ? Brush(custom) : DefaultBrushFor(c);

    public static IBrush DefaultBrushFor(FileCategory c) => c switch
    {
        FileCategory.Folder => Brush("#F2B84B"),
        FileCategory.Image => Brush("#3FB68B"),
        FileCategory.Video => Brush("#E5534B"),
        FileCategory.Audio => Brush("#A371F7"),
        FileCategory.Archive => Brush("#D29922"),
        FileCategory.Document => Brush("#4F8EF7"),
        FileCategory.Code => Brush("#39C5CF"),
        FileCategory.Executable => Brush("#F778BA"),
        _ => Brush("#8B949E"),
    };

    private static readonly Dictionary<string, IBrush> Cache = [];
    private static IBrush Brush(string hex)
    {
        if (!Cache.TryGetValue(hex, out var b)) Cache[hex] = b = new SolidColorBrush(Color.Parse(hex)).ToImmutable();
        return b;
    }
}

public sealed class CategoryToGeometryConverter : IValueConverter
{
    public static readonly CategoryToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FileCategory.Folder => FileIcons.Folder,
        FileCategory.Parent => FileIcons.Parent,
        _ => FileIcons.File,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CategoryToBrushConverter : IValueConverter
{
    public static readonly CategoryToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is FileCategory c ? FileIcons.BrushFor(c) : FileIcons.BrushFor(FileCategory.File);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Couleur de fond d'une ligne de comparaison de fichiers.</summary>
public sealed class DiffKindToBrushConverter : IValueConverter
{
    public static readonly DiffKindToBrushConverter Instance = new();

    private static readonly IBrush Added = new SolidColorBrush(Color.Parse("#403FB68B")).ToImmutable();
    private static readonly IBrush Removed = new SolidColorBrush(Color.Parse("#40E5534B")).ToImmutable();
    private static readonly IBrush Changed = new SolidColorBrush(Color.Parse("#40D29922")).ToImmutable();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TwinPanel.Core.Compare.DiffKind.Added => Added,
        TwinPanel.Core.Compare.DiffKind.Removed => Removed,
        TwinPanel.Core.Compare.DiffKind.Changed => Changed,
        _ => Brushes.Transparent,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
