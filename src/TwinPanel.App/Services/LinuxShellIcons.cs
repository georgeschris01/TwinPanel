// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Media.Imaging;
using SkiaSharp;
using Svg.Skia;
using TwinPanel.Core.Tools;

namespace TwinPanel.App.Services;

/// <summary>
/// Vraies icônes sous Linux : celles du thème du bureau, trouvées d'après le type du fichier.
/// Les thèmes récents sont en SVG : chaque icône est dessinée une fois à la bonne taille, puis gardée en mémoire par <see cref="ShellIcons"/>.
/// </summary>
internal static class LinuxShellIcons
{
    private const int Pixels = 32;

    private static readonly Lazy<LinuxIconTheme?> Theme = new(LinuxIconTheme.ForThisUser);
    private static readonly Lazy<LinuxFileTypes> Types = new(LinuxFileTypes.ForThisSystem);

    public static bool IsAvailable => OperatingSystem.IsLinux() && Theme.Value is not null;

    public static Bitmap? Load(string path, bool isDirectory)
    {
        if (Theme.Value is not { } theme) return null;
        var names = isDirectory ? ["folder"] : Types.Value.IconNames(Path.GetFileName(path));
        if (theme.Find(names, Pixels) is not { } file) return null;
        return file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? Draw(file) : new Bitmap(file);
    }

    private static Bitmap? Draw(string file)
    {
        using var svg = new SKSvg();
        svg.FromSvg(LinuxSvgText.SeparateTransformNumbers(File.ReadAllText(file)));
        if (svg.Picture is not { } picture) return null;
        var bounds = picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        using var bitmap = new SKBitmap(Pixels, Pixels);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(Pixels / bounds.Width, Pixels / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
        }
        // Une image vide cacherait l'icône colorée, qui reste affichée quand il n'y a pas d'icône.
        if (!bitmap.Pixels.Any(p => p.Alpha > 0)) return null;
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(png.ToArray());
        return new Bitmap(stream);
    }
}
