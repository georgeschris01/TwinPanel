// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using Xunit;

namespace TwinPanel.Core.Tests;

public class ServerFolderSizesTests
{
    [Fact]
    public void Parse_ReadsEveryFolder()
    {
        var output = "100\t/media/disque/a/x\n250\t/media/disque/a\n40\t/media/disque/b\n300\t/media/disque\n";
        var r = ServerFolderSizes.Parse(output, "/media/disque/");
        Assert.NotNull(r);
        Assert.Equal(300, r["/media/disque"]);
        Assert.Equal(250, r["/media/disque/a"]);
        Assert.Equal(100, r["/media/disque/a/x"]);
        Assert.Equal(40, r["/media/disque/b"]);
    }

    [Fact]
    public void Parse_Null_WhenTheFolderLineIsMissing()
    {
        Assert.Null(ServerFolderSizes.Parse("", "/media"));
        Assert.Null(ServerFolderSizes.Parse("du: option invalide -- 'b'\n", "/media"));
        Assert.Null(ServerFolderSizes.Parse("12\t/media/autre\n", "/media"));
    }

    [Fact]
    public void Parse_IgnoresBrokenLines_AndWindowsLineEnds()
    {
        var r = ServerFolderSizes.Parse("abc\t/x\r\n\r\n12\t/media/a b\r\n20\t/media\r\n", "/media");
        Assert.NotNull(r);
        Assert.Equal(12, r["/media/a b"]);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Normalize_KeepsRoot()
    {
        Assert.Equal("/", ServerFolderSizes.Normalize("/"));
        Assert.Equal("/", ServerFolderSizes.Normalize("//"));
        Assert.Equal("/media/x", ServerFolderSizes.Normalize("/media//x/"));
    }

    [Fact]
    public void Command_LimitsDepth_AndHidesErrors()
    {
        Assert.Equal("du -b -d 2 -- '/m' 2>/dev/null", ServerFolderSizes.Command("'/m'", 2));
        Assert.Equal("du -b -d 8 -- '/m' 2>/dev/null", ServerFolderSizes.Command("'/m'", 99));
    }
}
