// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Tools;
using TwinPanel.Core.Viewer;

namespace TwinPanel.Core.Tests;

public class ExternalToolsTests
{
    [Fact]
    public void Expands_placeholders()
    {
        var args = ExternalTools.ExpandArguments("-n%L \"%1\" --name %N --dir \"%D\"", Path.Combine("C:", "x", "a b.txt"), 42);
        Assert.Contains("-n42", args);
        Assert.Contains($"\"{Path.Combine("C:", "x", "a b.txt")}\"", args);
        Assert.Contains("--name a b.txt", args);
    }

    [Fact]
    public void Template_without_file_gets_it_appended()
    {
        Assert.Equal("--new-window \"f.txt\"", ExternalTools.ExpandArguments("--new-window", "f.txt"));
        Assert.Equal("\"f.txt\"", ExternalTools.ExpandArguments("", "f.txt"));
    }

    [Fact]
    public void Detects_only_installed_editors()
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notepadPlus = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Notepad++", "notepad++.exe");
        installed.Add(notepadPlus);

        var editors = ExternalTools.DetectEditors(installed.Contains);

        Assert.DoesNotContain(editors, e => e.Name == "VSCodium");
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("Bloc-notes (Windows)", editors[0].Name);
            Assert.Contains(editors, e => e.Name == "Notepad++" && e.Path == notepadPlus);
        }
    }

    [Fact]
    public void Missing_full_path_is_not_available()
    {
        Assert.False(ExternalTools.IsAvailable(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")));
        Assert.False(ExternalTools.IsAvailable(""));
        Assert.True(ExternalTools.IsAvailable("tool.exe", p => p.EndsWith("tool.exe")) || Environment.GetEnvironmentVariable("PATH") is null);
    }
}

public class TextDecodingTests
{
    [Fact]
    public void Detects_utf8_latin1_and_utf16()
    {
        Assert.IsType<UTF8Encoding>(TextDecoding.Detect(Encoding.UTF8.GetBytes("café crème")));
        Assert.Same(Encoding.Latin1, TextDecoding.Detect(Encoding.Latin1.GetBytes("café crème")));
        Assert.Equal(Encoding.Unicode.WebName, TextDecoding.Detect(Encoding.Unicode.GetBytes("bonjour tout le monde")).WebName);
        Assert.Equal(Encoding.Unicode.WebName, TextDecoding.Detect([0xFF, 0xFE, 0x41, 0x00]).WebName);
    }

    [Fact]
    public void Decodes_lines_with_any_line_ending_and_strips_bom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("un\r\ndeux\ntrois\rquatre")).ToArray();
        var lines = TextDecoding.DecodeLines(bytes, TextDecoding.Detect(bytes));
        Assert.Equal(["un", "deux", "trois", "quatre"], lines);
    }

    [Fact]
    public void Binary_detection()
    {
        Assert.True(TextDecoding.LooksBinary([0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06]));
        Assert.False(TextDecoding.LooksBinary(Encoding.UTF8.GetBytes("texte normal")));
        Assert.False(TextDecoding.LooksBinary(Encoding.Unicode.GetBytes("texte en UTF-16 sans BOM")));
    }
}

public class HexFormatterTests
{
    [Fact]
    public void Formats_offset_bytes_and_ascii()
    {
        var line = HexFormatter.FormatLine(Encoding.ASCII.GetBytes("Hello\n"), 0x20);
        Assert.StartsWith("00000020  48 65 6C 6C 6F 0A ", line);
        Assert.EndsWith("|Hello.|", line);
    }

    [Fact]
    public void Splits_into_16_byte_lines()
    {
        var lines = HexFormatter.FormatAll(new byte[40]);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("00000010", lines[1]);
        Assert.StartsWith("00000020", lines[2]);
    }
}
