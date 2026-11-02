// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Media;

namespace TwinPanel.Core.Tests;

public class MediaTests
{
    [Fact]
    public void Mkvpropedit_arguments_only_for_changes()
    {
        var args = MkvEdit.BuildArguments(@"D:\film.mkv", "Mon film",
        [
            new MkvTrackEdit(MediaInfoStream.Audio, 2) { Language = "fr", IsDefault = true },
            new MkvTrackEdit(MediaInfoStream.Text, 1) { Name = "", IsForced = false },
            new MkvTrackEdit(MediaInfoStream.Video, 1), // rien à changer : ignorée
        ]);
        Assert.Equal(new[]
        {
            @"D:\film.mkv", "--edit", "info", "--set", "title=Mon film",
            "--edit", "track:a2", "--set", "language=fr", "--set", "flag-default=1",
            "--edit", "track:s1", "--delete", "name", "--set", "flag-forced=0",
        }, args);
        Assert.Equal(new[] { "f.mkv" }, MkvEdit.BuildArguments("f.mkv", null, []));
    }

    [Fact]
    public void Exiftool_arguments()
    {
        var args = ExifEdit.BuildArguments("a.jpg", new ExifChanges { Taken = new DateTime(2024, 5, 31, 14, 30, 0), Artist = "Utilisateur", RemoveGps = true });
        Assert.Contains("-DateTimeOriginal=2024:05:31 14:30:00", args);
        Assert.Contains("-Artist=Utilisateur", args);
        Assert.Contains("-gps:all=", args);
        Assert.Equal("a.jpg", args[^1]);
        Assert.True(new ExifChanges().IsEmpty);
    }

    [Theory]
    [InlineData("Dolby Vision, Version 1.0, Profile 8.1, dvhe.08.06, BL+RPU, HDR10 compatible / SMPTE ST 2086, HDR10 compatible", "", "DV 8.1 HDR10")]
    [InlineData("SMPTE ST 2094 App 4, Version 1, HDR10+ Profile B compatible", "PQ", "HDR10+")]
    [InlineData("", "HLG", "HLG")]
    [InlineData("", "BT.709", "")]
    public void Hdr_short_names(string hdr, string transfer, string expected) => Assert.Equal(expected, MediaAnalyzer.HdrShort(hdr, transfer));

    [Theory]
    [InlineData("fre", "fr")]
    [InlineData("fra", "fr")]
    [InlineData("eng", "en")]
    [InlineData("ger", "de")]
    [InlineData("fr-BE", "fr")]
    [InlineData("en", "en")]
    public void Language_codes_are_shortened(string code, string expected) => Assert.Equal(expected, MediaReport.ShortLanguage(code));

    [Fact]
    public void Audio_tag_support_by_extension()
    {
        Assert.True(AudioTags.CanEdit("morceau.FLAC"));
        Assert.True(AudioTags.CanEdit("film.mp4"));
        Assert.False(AudioTags.CanEdit("film.mkv"));
        Assert.True(MkvEdit.IsMkv("film.mkv"));
        Assert.True(PhotoInfo.IsPhoto("IMG_0001.JPG"));
        Assert.True(new TagChanges().IsEmpty);
        Assert.False(new TagChanges { Title = "" }.IsEmpty);
    }
}

public class InstalledProgramsTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Notepad++\\notepad++.exe\",0", "C:\\Program Files\\Notepad++\\notepad++.exe")]
    [InlineData("C:\\Apps\\code.exe", "C:\\Apps\\code.exe")]
    [InlineData("C:\\Apps\\icone.ico", null)]
    [InlineData("", null)]
    public void Exe_from_display_icon(string icon, string? expected) => Assert.Equal(expected, TwinPanel.Core.Tools.InstalledPrograms.ExeFromIcon(icon));

    [WindowsTheory]
    [InlineData("C:\\X\\unins000.exe", true)]
    [InlineData("C:\\X\\Uninstall.exe", true)]
    [InlineData("C:\\X\\sublime_text.exe", false)]
    public void Uninstallers_are_hidden(string path, bool expected) => Assert.Equal(expected, TwinPanel.Core.Tools.InstalledPrograms.LooksLikeUninstaller(path));

    [Fact]
    public void Filter_by_name_or_folder()
    {
        TwinPanel.Core.Tools.InstalledProgram[] all = [new("VSCodium", @"C:\Program Files\VSCodium\VSCodium.exe"), new("Notepad++", @"C:\Program Files\Notepad++\notepad++.exe")];
        Assert.Single(TwinPanel.Core.Tools.InstalledPrograms.Filter(all, "codium"));
        Assert.Single(TwinPanel.Core.Tools.InstalledPrograms.Filter(all, "notepad"));
        Assert.Equal(2, TwinPanel.Core.Tools.InstalledPrograms.Filter(all, "").Count());
    }
}
