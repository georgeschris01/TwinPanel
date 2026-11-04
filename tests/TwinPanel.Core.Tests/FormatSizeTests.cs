// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Xunit;

namespace TwinPanel.Core.Tests;

public class FormatSizeTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void FormatSize_UsesTerabytes_From1024Gigabytes()
    {
        var tera = FileEntry.FormatSize(3413 * Gb);
        Assert.True(tera.EndsWith(" To", StringComparison.Ordinal) || tera.EndsWith(" TB", StringComparison.Ordinal), tera);
        Assert.StartsWith("3", tera, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSize_StaysInGigabytes_Below1024()
    {
        var giga = FileEntry.FormatSize(1023 * Gb);
        Assert.True(giga.EndsWith(" Go", StringComparison.Ordinal) || giga.EndsWith(" GB", StringComparison.Ordinal), giga);
    }
}
