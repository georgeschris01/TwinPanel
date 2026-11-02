// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.IO.Compression;
using TwinPanel.Core.Archives;

namespace TwinPanel.Core.Tests;

/// <summary>Archives protégées : jamais de contenu sans le bon mot de passe, et jamais une archive chiffrée réécrite en clair.</summary>
public class ArchivePasswordTests
{
    /// <summary>Mot de passe factice des petites archives ci-dessous, fabriquées avec 7-Zip (« contenu de test » dans note.txt).</summary>
    private const string FixturePassword = "essai-test";
    private const string Password = "pàss wörd-€";
    private const string ZipCrypto = "UEsDBBQAAQAAAEpyYV256VU/GwAAAA8AAAAIAAAAbm90ZS50eHSey8L4B6AiTzhjSTAaGhZhcGhdeV9HdCRurKhQSwECPwAUAAEAAABKcmFduelVPxsAAAAPAAAACAAkAAAAAAAAACAAAAAAAAAAbm90ZS50eHQKACAAAAAAAAEAGABqkK11A2rdAQAAAAAAAAAAAAAAAAAAAABQSwUGAAAAAAEAAQBaAAAAQQAAAAAA";
    private const string Encrypted7z = "N3q8ryccAAQH+15xIAAAAAAAAABqAAAAAAAAAGX7nltnRNJGCzHXuJrDfa6dCIfHUaOuhd3oK+3WEXZJzRZrgQEEBgABCSAABwsBAAIkBvEHARJTDxbVJ6Ffz6oH+fOSO/iITuohIQEAAQAMEw8ACAoBuelVPwAABQEZAQAREwBuAG8AdABlAC4AdAB4AHQAAAAZABQKAQBqkK11A2rdARUGAQAgAAAAAAA=";
    private const string HiddenNames7z = "N3q8ryccAARndnWdkAAAAAAAAAAuAAAAAAAAAIELvrCJL07Yw720fvEeVjY6EnlJYffAbEzvmQ6fix/6+UdypfE5sXHVTshddKyB+DhMWp2A5Z5e8potdPst/WWdNrU5VnDA1hBu3GsaqrmtGUxIr7HX5G70a/9Plphe9iWYe0cgZziPBDeosAgWGFRVoGd0fY+553licrXFNCr9F72HAugjN67AsYviUCbyvLoCsXEXBiABCXAABwsBAAEkBvEHARJTD9O2MfaOnK+B3V6uPy8FmyoMagoBMP5ffAAA";
    private static readonly DateTime OldDate = new(2020, 5, 6, 7, 8, 10);

    private static string Fixture(TempDir t, string name, string base64)
    {
        var path = Path.Combine(t.Path, name);
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    /// <summary>photos/été.txt, photos/vide.txt (0 octet), notes.txt et le dossier vide/, dans un zip chiffré.</summary>
    private static async Task<string> Sample(TempDir t)
    {
        t.File("src/photos/été.txt", "AAAA");
        t.File("src/photos/vide.txt", string.Empty);
        t.File("src/notes.txt", "texte");
        t.Dir("src/vide");
        var src = Path.Combine(t.Path, "src");
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) File.SetLastWriteTime(file, OldDate);
        var archive = Path.Combine(t.Path, "a.zip");
        await ArchiveService.CreateAsync([Path.Combine(src, "photos"), Path.Combine(src, "notes.txt"), Path.Combine(src, "vide")], archive, ArchiveKind.Zip, password: Password);
        return archive;
    }

    private static async Task<Dictionary<string, string>> Content(TempDir t, string archive)
    {
        var dest = Path.Combine(t.Path, "lu-" + Guid.NewGuid().ToString("N")[..6]);
        await ArchiveService.ExtractAsync(archive, [""], "", dest, ConflictPolicy.Overwrite);
        var found = new Dictionary<string, string>();
        foreach (var item in ArchiveIndex.Load(archive).All)
            found[item.Key] = item.IsDirectory ? "/" : File.ReadAllText(Path.Combine(dest, item.Key.Replace('/', Path.DirectorySeparatorChar)));
        return found;
    }

    [Fact]
    public async Task A_new_encrypted_zip_opens_only_with_its_password()
    {
        using var t = new TempDir();
        var archive = await Sample(t);

        Assert.True(ArchiveIndex.Load(archive).IsEncrypted);
        Assert.True(ArchivePasswords.NeedsPassword(archive));
        Assert.False(ArchivePasswords.Check(archive, "mauvais"));
        Assert.True(ArchivePasswords.Check(archive, Password));

        var error = await Assert.ThrowsAsync<ArchivePasswordException>(() => ArchiveService.ExtractAsync(archive, [""], "", Path.Combine(t.Path, "dest"), ConflictPolicy.Overwrite));
        Assert.False(error.Wrong);
        await Assert.ThrowsAsync<ArchivePasswordException>(() => new ArchiveFileSource().ListAsync(ArchivePath.Combine(archive, ""), false, CancellationToken.None));

        ArchivePasswords.Remember(archive, Password);
        Assert.False(ArchivePasswords.NeedsPassword(archive));
        Assert.Contains(await new ArchiveFileSource().ListAsync(ArchivePath.Combine(archive, ""), false, CancellationToken.None), e => e.Name == "notes.txt");
        var content = await Content(t, archive);
        Assert.Equal("AAAA", content["photos/été.txt"]);
        Assert.Equal(string.Empty, content["photos/vide.txt"]);
        Assert.Equal("texte", content["notes.txt"]);
        Assert.Equal("/", content["vide"]);
        Assert.Equal(OldDate, ArchiveIndex.Load(archive).All.Single(i => i.Key == "notes.txt").Modified);
        await ArchiveService.TestAsync(archive);
        ArchivePasswords.Forget(archive);
    }

    [Fact]
    public async Task A_wrong_password_is_said_and_forgotten()
    {
        using var t = new TempDir();
        var archive = await Sample(t);
        ArchivePasswords.Remember(archive, "mauvais");

        var error = await Assert.ThrowsAsync<ArchivePasswordException>(() => ArchiveService.ExtractAsync(archive, [""], "", Path.Combine(t.Path, "dest"), ConflictPolicy.Overwrite));

        Assert.True(error.Wrong);
        Assert.Null(ArchivePasswords.Get(archive));
    }

    [Fact]
    public async Task The_password_never_appears_in_the_archive_or_in_a_message()
    {
        using var t = new TempDir();
        var archive = await Sample(t);
        ArchivePasswords.Remember(archive, Password);
        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["notes.txt"] });
        var raw = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(archive));
        ArchivePasswords.Forget(archive);

        Assert.DoesNotContain("pàss", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, new ArchivePasswordException(archive, wrong: true).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("add")]
    public async Task An_edited_zip_stays_encrypted_with_the_same_password(string edit)
    {
        using var t = new TempDir();
        var archive = await Sample(t);
        var added = t.File("ajout.txt", "nouveau");
        ArchivePasswords.Remember(archive, Password);

        await ArchiveEditor.ApplyAsync(archive, edit switch
        {
            "delete" => new ArchiveEdits { Delete = ["notes.txt"] },
            "rename" => new ArchiveEdits { Rename = new Dictionary<string, string> { ["photos"] = "images" } },
            _ => new ArchiveEdits { Add = [new ArchiveAddition(added, "photos")] },
        });

        Assert.True(ArchiveIndex.Load(archive).IsEncrypted);
        Assert.False(ArchivePasswords.Check(archive, "mauvais"));
        Assert.True(ArchivePasswords.Check(archive, Password));
        var content = await Content(t, archive);
        Assert.Equal("AAAA", content[edit == "rename" ? "images/été.txt" : "photos/été.txt"]);
        if (edit == "delete") Assert.DoesNotContain("notes.txt", content.Keys);
        if (edit == "add") Assert.Equal("nouveau", content["photos/ajout.txt"]);
        Assert.Equal("/", content["vide"]);
        ArchivePasswords.Forget(archive);
    }

    [Fact]
    public async Task Attributes_and_comments_of_an_encrypted_zip_are_kept()
    {
        using var t = new TempDir();
        var archive = await Sample(t);
        ArchivePasswords.Remember(archive, Password);
        var before = ReadDetails(archive);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { NewFolders = ["neuf"] });

        Assert.Equal(before["notes.txt"], ReadDetails(archive)["notes.txt"]);
        ArchivePasswords.Forget(archive);
    }

    private static Dictionary<string, (int Attributes, string Comment)> ReadDetails(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return zip.Entries.ToDictionary(e => e.FullName, e => (e.ExternalAttributes, e.Comment));
    }

    [Fact]
    public async Task An_encrypted_zip_cannot_be_edited_without_its_password()
    {
        using var t = new TempDir();
        var archive = await Sample(t);
        var before = File.ReadAllBytes(archive);

        await Assert.ThrowsAsync<ArchivePasswordException>(() => ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["notes.txt"] }));

        Assert.Equal(before, File.ReadAllBytes(archive));
    }

    [Fact]
    public async Task An_emptied_encrypted_zip_stays_a_valid_archive()
    {
        using var t = new TempDir();
        var archive = await Sample(t);
        ArchivePasswords.Remember(archive, Password);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { Delete = ["photos", "notes.txt", "vide"] });

        Assert.Empty(ArchiveIndex.Load(archive).All);
        ArchivePasswords.Forget(archive);
    }

    [Fact]
    public async Task Old_zip_encryption_is_read_and_replaced_by_aes()
    {
        using var t = new TempDir();
        var archive = Fixture(t, "ancien.zip", ZipCrypto);
        Assert.False(ArchivePasswords.Check(archive, "mauvais"));
        Assert.True(ArchivePasswords.Check(archive, FixturePassword));
        ArchivePasswords.Remember(archive, FixturePassword);

        await ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { NewFolders = ["neuf"] });

        Assert.Equal("contenu de test", (await Content(t, archive))["note.txt"]);
        using (var zip = ZipFile.OpenRead(archive))
            Assert.Equal(1, zip.Entries.Count(e => e.IsEncrypted && e.FullName == "note.txt"));
        Assert.False(ArchivePasswords.Check(archive, "mauvais"));
        ArchivePasswords.Forget(archive);
    }

    [Theory]
    [InlineData("a.7z", Encrypted7z)]
    [InlineData("b.7z", HiddenNames7z)]
    public async Task An_encrypted_7z_opens_but_is_never_rewritten(string name, string base64)
    {
        using var t = new TempDir();
        var archive = Fixture(t, name, base64);
        var before = File.ReadAllBytes(archive);

        Assert.True(ArchivePasswords.NeedsPassword(archive));
        Assert.NotNull(ArchiveEditor.EncryptedRefusal(archive));
        Assert.False(ArchivePasswords.Check(archive, "mauvais"));
        Assert.True(ArchivePasswords.Check(archive, FixturePassword));

        ArchivePasswords.Remember(archive, FixturePassword);
        Assert.Equal("contenu de test", (await Content(t, archive))["note.txt"]);
        await Assert.ThrowsAsync<FileOperationException>(() => ArchiveEditor.ApplyAsync(archive, new ArchiveEdits { NewFolders = ["neuf"] }));

        Assert.Equal(before, File.ReadAllBytes(archive));
        ArchivePasswords.Forget(archive);
    }

    [Fact]
    public async Task Only_zip_can_be_created_with_a_password()
    {
        using var t = new TempDir();
        var file = t.File("a.txt", "x");
        await Assert.ThrowsAsync<FileOperationException>(() => ArchiveService.CreateAsync([file], Path.Combine(t.Path, "a.tar"), ArchiveKind.Tar, password: Password));
    }

    [Fact]
    public async Task Decrypted_copies_disappear_with_the_passwords()
    {
        using var t = new TempDir();
        var archive = await Sample(t);
        ArchivePasswords.Remember(archive, Password);

        var copy = await ArchiveService.ExtractToTempAsync(archive, "notes.txt");
        Assert.True(File.Exists(copy));

        ArchivePasswords.ForgetAll();
        Assert.False(File.Exists(copy));
        Assert.Null(ArchivePasswords.Get(archive));
    }
}
