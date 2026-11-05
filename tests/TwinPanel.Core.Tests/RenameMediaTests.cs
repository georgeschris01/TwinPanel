// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Media;
using TwinPanel.Core.Operations;

namespace TwinPanel.Core.Tests;

/// <summary>Ctrl+M selon les médias : un tag ne doit jamais donner un nom invalide ou à trous.</summary>
public class RenameMediaTests
{
    private static FileEntry Entry(string name, bool isDirectory = false) =>
        new(name, Path.Combine(@"C:\Musique", name), isDirectory, 10, new DateTime(2026, 1, 1), FileAttributes.Normal);

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Media(FileEntry entry, params (string Key, string Value)[] values) =>
        new() { [entry.FullPath] = values.ToDictionary(v => v.Key, v => v.Value) };

    private static RenamePreview One(FileEntry entry, string mask, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? media) =>
        MultiRename.Preview([entry], new RenameOptions { NameMask = mask }, media)[0];

    [Fact]
    public void Tags_build_the_name()
    {
        var entry = Entry("piste01.mp3");

        var result = One(entry, "[Artiste] - [Piste] - [Titre]", Media(entry, ("Artiste", "Daft Punk"), ("Piste", "03"), ("Titre", "Around the World")));

        Assert.Equal("Daft Punk - 03 - Around the World.mp3", result.NewName);
        Assert.True(result.Changes);
    }

    [Theory]
    [InlineData("[Titre]", "Pas de [Titre] : non renommé")]
    [InlineData("[Titre] [Album] [Titre]", "Pas de [Titre], [Album] : non renommé")]
    public void A_missing_value_leaves_the_file_alone_without_blocking_the_others(string mask, string message)
    {
        var (good, bad) = (Entry("a.mp3"), Entry("b.mp3"));
        var media = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [good.FullPath] = new Dictionary<string, string> { ["Titre"] = "Bon", ["Album"] = "Disque" },
            [bad.FullPath] = new Dictionary<string, string>(),
        };

        var results = MultiRename.Preview([good, bad], new RenameOptions { NameMask = mask }, media);

        Assert.True(results[0].Changes);
        Assert.Null(results[1].Error);
        Assert.False(results[1].Changes);
        Assert.Equal("b.mp3", results[1].NewName);
        Assert.Equal(message, results[1].Problem);
    }

    [Fact]
    public void A_skipped_file_keeps_its_name_when_the_rename_runs()
    {
        using var dir = new TempDir();
        var (a, b) = (Path.Combine(dir.Path, "a.txt"), Path.Combine(dir.Path, "b.txt"));
        File.WriteAllText(a, "1");
        File.WriteAllText(b, "2");
        FileEntry Real(string path) => new(Path.GetFileName(path), path, false, 1, DateTime.Now, FileAttributes.Normal);
        var media = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [a] = new Dictionary<string, string> { ["Titre"] = "Nouveau" },
            [b] = new Dictionary<string, string>(),
        };

        var log = MultiRename.Execute(MultiRename.Preview([Real(a), Real(b)], new RenameOptions { NameMask = "[Titre]" }, media));

        Assert.Single(log.Items);
        Assert.True(File.Exists(Path.Combine(dir.Path, "Nouveau.txt")));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public void Two_files_with_the_same_tag_are_reported()
    {
        var (a, b) = (Entry("a.mp3"), Entry("b.mp3"));
        var media = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [a.FullPath] = new Dictionary<string, string> { ["Titre"] = "Même" },
            [b.FullPath] = new Dictionary<string, string> { ["Titre"] = "Même" },
        };

        var results = MultiRename.Preview([a, b], new RenameOptions { NameMask = "[Titre]" }, media);

        Assert.All(results, r => Assert.NotNull(r.Error));
    }

    [Fact]
    public void A_renamed_file_cannot_take_the_name_of_a_skipped_one()
    {
        var (first, kept) = (Entry("a.mp3"), Entry("Pris.mp3"));
        var media = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [first.FullPath] = new Dictionary<string, string> { ["Titre"] = "Pris" },
            [kept.FullPath] = new Dictionary<string, string>(),
        };

        var results = MultiRename.Preview([first, kept], new RenameOptions { NameMask = "[Titre]" }, media);

        Assert.NotNull(results[0].Error);
    }

    [Fact]
    public void Forbidden_characters_of_a_tag_are_replaced()
    {
        var entry = Entry("x.mp3");

        var result = One(entry, "[Artiste] - [Titre]", Media(entry, ("Artiste", "AC/DC"), ("Titre", "Quoi? \"Oui\"...")));

        Assert.Equal("AC_DC - Quoi_ _Oui_.mp3", result.NewName);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Server_files_get_a_clear_error_instead_of_a_guess()
    {
        var result = One(Entry("x.mp3"), "[Titre]", media: null);

        Assert.Equal("x.mp3", result.NewName);
        Assert.Contains("fichiers du PC", result.Error);
    }

    [Fact]
    public void Masks_without_media_work_as_before()
    {
        Assert.Equal("x-1.mp3", One(Entry("x.mp3"), "[N]-[C]", media: null).NewName);
        Assert.False(MultiRename.UsesMedia("[N]-[C][YMD]"));
        Assert.True(MultiRename.UsesMedia("[N][Date de prise de vue]"));
    }

    [Fact]
    public void Folders_have_no_tags()
    {
        var folder = Entry("Album", isDirectory: true);

        var result = One(folder, "[Titre]", new Dictionary<string, IReadOnlyDictionary<string, string>> { [folder.FullPath] = new Dictionary<string, string>() });

        Assert.False(result.Changes);
        Assert.NotNull(result.Skipped);
    }

    [Theory]
    [InlineData("3", "03")]
    [InlineData("3/12", "03")]
    [InlineData("12", "12")]
    [InlineData(" 7 ", "07")]
    [InlineData("A1", "A1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Track_numbers_sort_in_order(string? track, string expected) => Assert.Equal(expected, RenameMedia.FormatTrack(track));

    [Theory]
    [InlineData(187, "3m07s")]
    [InlineData(59, "0m59s")]
    [InlineData(5025, "1h23m45s")]
    public void Durations_are_valid_in_a_name(int seconds, string expected) => Assert.Equal(expected, RenameMedia.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Tags_are_read_from_a_real_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "son.wav");
        File.WriteAllBytes(path, Wav());
        AudioTags.Write(path, new TagChanges { Title = "Essai/Un", Artist = "Moi", TrackNumber = "4", Genre = "Rock" });
        var info = new FileInfo(path);

        var values = RenameMedia.Read(path, info.Length, info.LastWriteTime);

        Assert.Equal("Essai_Un", values[RenameMedia.Title]);
        Assert.Equal("Moi", values[RenameMedia.Artist]);
        Assert.Equal("04", values[RenameMedia.Track]);
        Assert.Equal("Rock", values[RenameMedia.Genre]);
        Assert.False(values.ContainsKey(RenameMedia.Album));
    }

    [Fact]
    public void A_file_without_media_gives_no_values()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "notes.txt");
        File.WriteAllText(path, "bonjour");

        Assert.Empty(RenameMedia.Read(path, 7, DateTime.Now));
    }

    /// <summary>Une seconde de silence en PCM 8 bits : assez pour que le fichier soit reconnu.</summary>
    private static byte[] Wav()
    {
        const int rate = 8000;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8);
        w.Write(36 + rate);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate);
        w.Write((short)1);
        w.Write((short)8);
        w.Write("data"u8);
        w.Write(rate);
        w.Write(new byte[rate]);
        return ms.ToArray();
    }
}
