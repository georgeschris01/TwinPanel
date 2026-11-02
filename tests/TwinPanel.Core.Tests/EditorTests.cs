// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Tools;
using TwinPanel.Core.Viewer;

namespace TwinPanel.Core.Tests;

public class TextDocumentTests
{
    [Fact]
    public void Keeps_crlf_and_utf8_without_bom()
    {
        using var t = new TempDir();
        var p = t.File("a.txt");
        File.WriteAllBytes(p, Encoding.UTF8.GetBytes("ligne 1\r\nété\r\n"));

        var doc = TextDocument.Load(p);
        Assert.Equal("ligne 1\nété\n", doc.Text);
        Assert.Equal("\r\n", doc.NewLine);
        Assert.False(doc.HasBom);

        (doc with { Text = doc.Text + "ajout\n" }).Save(p);
        Assert.Equal(Encoding.UTF8.GetBytes("ligne 1\r\nété\r\najout\r\n"), File.ReadAllBytes(p));
    }

    [Fact]
    public void Keeps_lf_and_bom()
    {
        using var t = new TempDir();
        var p = t.File("b.txt");
        File.WriteAllBytes(p, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\nb")]);

        var doc = TextDocument.Load(p);
        Assert.True(doc.HasBom);
        Assert.Equal("\n", doc.NewLine);
        doc.Save(p);

        Assert.Equal([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\nb")], File.ReadAllBytes(p));
    }

    [Fact]
    public void Keeps_latin1()
    {
        using var t = new TempDir();
        var p = t.File("c.txt");
        File.WriteAllBytes(p, Encoding.Latin1.GetBytes("café\r\n"));

        var doc = TextDocument.Load(p);
        Assert.Equal("café\n", doc.Text);
        (doc with { Text = "crème\n" }).Save(p);

        Assert.Equal(Encoding.Latin1.GetBytes("crème\r\n"), File.ReadAllBytes(p));
    }

    [Fact]
    public void Keeps_utf16_with_bom()
    {
        using var t = new TempDir();
        var p = t.File("d.txt");
        File.WriteAllBytes(p, [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("x\r\ny")]);

        var doc = TextDocument.Load(p);
        Assert.Equal("x\ny", doc.Text);
        doc.Save(p);

        Assert.Equal([0xFF, 0xFE, .. Encoding.Unicode.GetBytes("x\r\ny")], File.ReadAllBytes(p));
    }

    [Fact]
    public void Refuses_binary_and_read_only()
    {
        using var t = new TempDir();
        var bin = t.File("e.bin");
        File.WriteAllBytes(bin, [1, 0, 2, 0, 0, 0, 3, 4, 5, 0]);
        Assert.Throws<FileOperationException>(() => TextDocument.Load(bin));

        var ro = t.File("f.txt", "x");
        var doc = TextDocument.Load(ro);
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<FileOperationException>(() => (doc with { Text = "y" }).Save(ro));
            Assert.Equal("x", File.ReadAllText(ro));
        }
        finally { File.SetAttributes(ro, FileAttributes.Normal); }
    }

    [Fact]
    public void Internal_editor_is_always_available_and_listed()
    {
        Assert.True(ExternalTools.IsAvailable(ExternalTools.InternalPath));
        Assert.Contains(ExternalTools.DetectEditors(_ => false), e => ExternalTools.IsInternal(e.Path));
    }
}

public class EncodingDetectionTests
{
    [Fact]
    public void Short_latin1_text_is_not_taken_for_utf8()
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes("café\r\n");
        Assert.Equal(28591, TextDecoding.Detect(bytes).CodePage);
        Assert.Equal(28591, TextDecoding.Detect(bytes, complete: true).CodePage);
    }

    [Fact]
    public void Utf8_cut_in_the_middle_of_a_character_is_still_utf8()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("aaaa été");
        var cut = bytes[..^1]; // le dernier « é » (2 octets) est coupé
        Assert.IsType<System.Text.UTF8Encoding>(TextDecoding.Detect(cut));
    }
}
