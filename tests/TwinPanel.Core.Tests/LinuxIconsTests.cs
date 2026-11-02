// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Tools;

namespace TwinPanel.Core.Tests;

public class LinuxIconsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("twinpanel-icones-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static LinuxFileTypes Types() => new(
    [
        "# commentaire",
        "80:application/x-compressed-tar:*.tar.gz",
        "50:text/plain:*.txt",
        "50:application/gzip:*.gz",
        "50:application/zip:*.zip",
        "50:image/png:*.png",
        "10:text/x-readme:README",
        "10:text/x-backup:*~",
        "ligne abimee",
    ], ["application/x-compressed-tar:package-x-generic", "application/zip:package-x-generic"]);

    [Theory]
    [InlineData("note.txt", "text/plain")]
    [InlineData("NOTE.TXT", "text/plain")]
    [InlineData("a.tar.gz", "application/x-compressed-tar")]
    [InlineData("a.b.gz", "application/gzip")]
    [InlineData("README", "text/x-readme")]
    [InlineData("sans-extension", null)]
    [InlineData(".cache", null)]
    public void The_type_comes_from_the_name(string name, string? type) => Assert.Equal(type, Types().TypeOf(name));

    [Fact]
    public void Icon_names_go_from_precise_to_general()
    {
        Assert.Equal(["application-zip", "package-x-generic", "application-x-generic", "unknown", "text-x-generic"], Types().IconNames("a.zip"));
        Assert.Equal(["unknown", "text-x-generic"], Types().IconNames("inconnu"));
    }

    private void Theme(string name, string inherits, params (string Folder, string Context, int Size)[] folders)
    {
        var root = Path.Combine(_root, name);
        Directory.CreateDirectory(root);
        var text = $"[Icon Theme]\nName={name}\nInherits={inherits}\nDirectories={string.Join(',', folders.Select(f => f.Folder))}\n\n";
        foreach (var (folder, context, size) in folders)
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
            text += $"[{folder}]\nSize={size}\nContext={context}\n\n";
        }
        File.WriteAllText(Path.Combine(root, "index.theme"), text);
    }

    private string Icon(string theme, string folder, string file)
    {
        var path = Path.Combine(_root, theme, folder, file);
        File.WriteAllText(path, "icone");
        return path;
    }

    [Fact]
    public void The_nearest_size_is_chosen()
    {
        Theme("breeze", "", ("mimetypes/16", "MimeTypes", 16), ("mimetypes/32", "MimeTypes", 32), ("mimetypes/64", "MimeTypes", 64));
        Icon("breeze", "mimetypes/16", "text-plain.svg");
        var wanted = Icon("breeze", "mimetypes/32", "text-plain.svg");
        Icon("breeze", "mimetypes/64", "text-plain.svg");
        Assert.Equal(wanted, new LinuxIconTheme("breeze", [_root]).Find(["text-plain"], 32));
    }

    [Fact]
    public void A_missing_icon_comes_from_the_inherited_theme()
    {
        Theme("perso", "breeze", ("mimetypes/32", "MimeTypes", 32));
        Theme("breeze", "", ("places/32", "Places", 32));
        var wanted = Icon("breeze", "places/32", "folder.svg");
        Assert.Equal(wanted, new LinuxIconTheme("perso", [_root]).Find(["folder"], 32));
    }

    [Fact]
    public void Hicolor_is_always_the_last_resort()
    {
        Theme("perso", "", ("mimetypes/32", "MimeTypes", 32));
        Theme("hicolor", "", ("32x32/mimetypes", "MimeTypes", 32));
        var wanted = Icon("hicolor", "32x32/mimetypes", "application-x-truc.png");
        Assert.Equal(wanted, new LinuxIconTheme("perso", [_root]).Find(["application-x-truc"], 32));
    }

    [Fact]
    public void Inherited_themes_that_loop_do_not_hang()
    {
        Theme("a", "b", ("mimetypes/32", "MimeTypes", 32));
        Theme("b", "a", ("mimetypes/32", "MimeTypes", 32));
        Assert.Null(new LinuxIconTheme("a", [_root]).Find(["absent"], 32));
    }

    [Fact]
    public void Other_kinds_of_icons_are_ignored()
    {
        Theme("t", "", ("actions/32", "Actions", 32));
        Icon("t", "actions/32", "folder.svg");
        var theme = new LinuxIconTheme("t", [_root]);
        Assert.False(theme.IsUsable);
        Assert.Null(theme.Find(["folder"], 32));
    }

    [Fact]
    public void An_unknown_theme_is_not_usable() => Assert.False(new LinuxIconTheme("absent", [_root]).IsUsable);

    [Theory]
    [InlineData("<g transform=\"translate(-384.57-515.8)\">", "<g transform=\"translate(-384.57 -515.8)\">")]
    [InlineData("<g transform=\"scale(1-1)\">", "<g transform=\"scale(1 -1)\">")]
    [InlineData("<linearGradient gradientTransform='translate(3.-4)'/>", "<linearGradient gradientTransform='translate(3. -4)'/>")]
    [InlineData("<g transform=\"matrix(1 0 0 1e-5 -3 -4)\">", "<g transform=\"matrix(1 0 0 1e-5 -3 -4)\">")]
    [InlineData("<path id=\"a-1\" d=\"m1-2\"/>", "<path id=\"a-1\" d=\"m1-2\"/>")]
    public void Svg_transform_numbers_are_separated(string svg, string expected) =>
        Assert.Equal(expected, LinuxSvgText.SeparateTransformNumbers(svg));
}
