// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.IO.Compression;
using System.Text;
using TwinPanel.Core.Search;
using TwinPanel.Core.Selection;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace TwinPanel.Core.Tests;

public class DocumentSearchTests
{
    /// <summary>Zip en mémoire : (chemin dans le zip, contenu). Stocké sans compression pour l'un, compressé pour les autres.</summary>
    private static byte[] Zip(params (string Path, string Content)[] parts)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, content) in parts)
            {
                using var w = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        return ms.ToArray();
    }

    private static string? Text(byte[] data, string name) => DocumentText.Extract(new MemoryStream(data), name);

    private static byte[] Docx(string body) => Zip(
        ("[Content_Types].xml", "<Types/>"),
        ("word/styles.xml", "<w:styles><w:style w:styleId=\"Destination\"/></w:styles>"),
        ("word/document.xml", $"<w:document><w:body>{body}</w:body></w:document>"));

    [Fact]
    public void Bare_word_in_search_mask_means_contains()
    {
        var search = new MaskMatcher("destin", bareMeansContains: true);
        Assert.True(search.IsMatch("Le Destin.epub"));
        Assert.True(search.IsMatch("destinations.pdf"));
        Assert.False(search.IsMatch("roman.epub"));

        var mixed = new MaskMatcher("destin;*.pdf | brouillon", bareMeansContains: true);
        Assert.True(mixed.IsMatch("rapport.pdf"));
        Assert.True(mixed.IsMatch("destin.txt"));
        Assert.False(mixed.IsMatch("destin brouillon.txt"));

        // Ailleurs (sélection par masque), un mot sans joker reste un nom exact.
        Assert.False(new MaskMatcher("destin").IsMatch("Le Destin.epub"));
        Assert.True(new MaskMatcher("*.jpg", bareMeansContains: true).IsMatch("a.JPG"));
        Assert.False(new MaskMatcher("*.jpg", bareMeansContains: true).IsMatch("a.jpg.txt"));
    }

    [Fact]
    public void Word_text_is_read_even_when_a_word_is_split_in_pieces()
    {
        var docx = Docx("<w:p><w:r><w:t>Le des</w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>tin</w:t></w:r></w:p><w:p><w:r><w:t>Fin&amp;suite</w:t></w:r></w:p>");
        var text = Text(docx, "livre.docx")!;
        Assert.Contains("Le destin\nFin&suite", text); // mot recollé, paragraphes séparés, &amp; décodé
        Assert.DoesNotContain("Destination", text); // nom de style : pas du texte
    }

    [Fact]
    public void Epub_text_comes_from_the_pages_without_styles_or_tags()
    {
        var epub = Zip(
            ("mimetype", "application/epub+zip"),
            ("OEBPS/content.opf", "<package><metadata><dc:title>Mon livre</dc:title></metadata></package>"),
            ("OEBPS/style.css", "p { destin: rouge }"),
            ("OEBPS/chap1.xhtml", "<html><head><style>.destinA{}</style></head><body><p>Il croyait au&nbsp;destin.</p><p>L&#8217;autre</p></body></html>"));
        var text = Text(epub, "livre.EPUB")!;
        Assert.Contains("Il croyait au destin.", text);
        Assert.Contains("L’autre", text);
        Assert.Contains("Mon livre", text);
        Assert.DoesNotContain("destinA", text);
        Assert.DoesNotContain("rouge", text);
    }

    [Fact]
    public void Excel_powerpoint_and_opendocument_texts_are_read()
    {
        Assert.Contains("Budget destin", Text(Zip(("xl/sharedStrings.xml", "<sst><si><t>Budget destin</t></si></sst>")), "a.xlsx")!);
        Assert.Contains("Diapo destin", Text(Zip(("ppt/slides/slide1.xml", "<p:sld><a:p><a:r><a:t>Diapo destin</a:t></a:r></a:p></p:sld>")), "a.pptx")!);
        Assert.Contains("Texte destin", Text(Zip(("content.xml", "<office:text><text:p>Texte<text:s/>destin</text:p></office:text>")), "a.odt")!);
    }

    [Fact]
    public void Pdf_text_is_read()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page.AddText("Le destin du heros", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        var text = Text(builder.Build(), "livre.pdf")!;
        Assert.Contains("destin", text);
    }

    [Fact]
    public void Damaged_document_gives_null_and_unknown_extensions_are_not_documents()
    {
        Assert.Null(Text(Encoding.UTF8.GetBytes("pas un zip"), "x.epub"));
        Assert.Null(Text(Encoding.UTF8.GetBytes("pas un pdf"), "x.pdf"));
        Assert.True(DocumentText.IsDocument("Livre.EPUB"));
        Assert.True(DocumentText.IsDocument("a.docx"));
        Assert.False(DocumentText.IsDocument("a.txt"));
        Assert.False(DocumentText.IsDocument("a.zip"));
    }

    [Fact]
    public async Task Search_finds_the_word_in_a_document_only_when_documents_are_read()
    {
        var dir = Directory.CreateTempSubdirectory("tp-docsearch-");
        try
        {
            File.WriteAllBytes(Path.Combine(dir.FullName, "roman.docx"), Docx("<w:p><w:r><w:t>des</w:t></w:r><w:r><w:t>tin</w:t></w:r></w:p>"));
            File.WriteAllText(Path.Combine(dir.FullName, "notes.txt"), "un destin");
            File.WriteAllText(Path.Combine(dir.FullName, "autre.txt"), "rien");

            async Task<List<string>> Run(SearchCriteria c)
            {
                var names = new List<string>();
                await foreach (var e in FileSearch.SearchAsync(c)) names.Add(e.Name);
                names.Sort(StringComparer.Ordinal);
                return names;
            }

            var withDocs = await Run(new SearchCriteria { Root = dir.FullName, ContainsText = "destin", IncludeFolders = false });
            Assert.Equal(["notes.txt", "roman.docx"], withDocs);

            var raw = await Run(new SearchCriteria { Root = dir.FullName, ContainsText = "destin", IncludeFolders = false, SearchDocuments = false });
            Assert.Equal(["notes.txt"], raw); // le mot est compressé dans le docx

            var byName = await Run(new SearchCriteria { Root = dir.FullName, NameMask = "rom" });
            Assert.Equal(["roman.docx"], byName);
        }
        finally { dir.Delete(recursive: true); }
    }
}
