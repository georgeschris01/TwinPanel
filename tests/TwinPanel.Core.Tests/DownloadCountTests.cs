// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.About;
using Xunit;

namespace TwinPanel.Core.Tests;

/// <summary>Lecture de la réponse de GitHub, avec un exemple fabriqué : ces tests ne vont jamais sur Internet.</summary>
public class DownloadCountTests
{
    private const string TwoReleases = """
        [
          { "tag_name": "v0.8.9", "assets": [ { "name": "TwinPanel-Setup-0.8.9.exe", "download_count": 11 } ] },
          { "tag_name": "linux-v0.1.13-beta", "assets": [
            { "name": "twinpanel_0.1.13-beta_amd64.deb", "download_count": 12 },
            { "name": "twinpanel-0.1.13-beta.x86_64.rpm", "download_count": 5 },
            { "name": "TwinPanel-0.1.13-beta-x86_64.AppImage", "download_count": 30 },
            { "name": "SHA256SUMS.txt", "download_count": 99 } ] },
          { "tag_name": "v0.8.8", "assets": [ { "name": "TwinPanel-Setup-0.8.8.exe", "download_count": 4 } ] },
          { "tag_name": "sans-fichier", "assets": [] }
        ]
        """;

    [Fact]
    public void Windows_counts_every_windows_installer_of_every_version() =>
        Assert.Equal((15L, 4), DownloadCount.ParsePage(TwoReleases, windows: true));

    [Fact]
    public void Linux_counts_deb_rpm_and_appimage_but_not_other_files() =>
        Assert.Equal((47L, 4), DownloadCount.ParsePage(TwoReleases, windows: false));

    [Theory]
    [InlineData("TwinPanel-Setup-0.8.10.exe", true, true)]
    [InlineData("twinpanel_0.1.14-beta_amd64.deb", true, false)]
    [InlineData("twinpanel_0.1.14-beta_amd64.deb", false, true)]
    [InlineData("TwinPanel-0.1.14-beta-x86_64.AppImage", false, true)]
    [InlineData("TwinPanel-Setup-0.8.10.exe", false, false)]
    public void Each_file_belongs_to_its_system(string name, bool windows, bool expected) =>
        Assert.Equal(expected, DownloadCount.IsForSystem(name, windows));

    [Fact]
    public void The_address_is_the_public_list_of_releases_of_the_project() =>
        Assert.Equal("https://api.github.com/repos/georgeschris01/TwinPanel/releases?per_page=100&page=2", DownloadCount.ApiUri(2).ToString());

    [Theory]
    [InlineData("pas du JSON")]
    [InlineData("""{ "message": "Not Found" }""")]
    public void An_unexpected_answer_gives_nothing(string json) =>
        Assert.Null(DownloadCount.ParsePage(json, windows: true));
}
