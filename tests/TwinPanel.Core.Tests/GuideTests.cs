// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Help;
using Xunit;

namespace TwinPanel.Core.Tests;

/// <summary>Le guide est lu dans les README : les deux langues doivent rester au même niveau.</summary>
public class GuideTests
{
    private static string RootFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TwinPanel.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("TwinPanel.sln"), name));
    }

    private static IReadOnlyList<GuideChapter> French => GuideDocument.Parse(RootFile("README.md"));
    private static IReadOnlyList<GuideChapter> English => GuideDocument.Parse(RootFile("README.en.md"));

    [Fact]
    public void Le_guide_a_ses_chapitres_dans_les_deux_langues()
    {
        Assert.True(French.Count >= 10);
        Assert.Equal(French.Count, English.Count);
        Assert.All(French.Concat(English), c => Assert.NotEmpty(c.Blocks));
    }

    [Fact]
    public void Les_deux_langues_ont_la_meme_structure()
    {
        for (int i = 0; i < French.Count; i++)
        {
            var fr = French[i].Blocks.Select(b => (b.Kind, b.Rows?.Count)).ToList();
            var en = English[i].Blocks.Select(b => (b.Kind, b.Rows?.Count)).ToList();
            Assert.True(fr.SequenceEqual(en), $"Chapitre différent : {French[i].Title} / {English[i].Title}");
        }
    }

    [Fact]
    public void Une_seule_page_montre_les_raccourcis_en_vigueur_sans_le_tableau_fixe()
    {
        foreach (var guide in new[] { French, English })
        {
            var chapter = Assert.Single(guide, c => c.Blocks.Any(b => b.Kind == GuideBlockKind.Shortcuts));
            Assert.DoesNotContain(chapter.Blocks, b => b.Kind == GuideBlockKind.Table);
        }
    }

    [Fact]
    public void Le_guide_parle_des_gestes_souris()
    {
        var mouse = French[1];
        foreach (var word in new[] { "Clic droit court", "Clic milieu", "Boutons latéraux", "Double-clic dans le vide", "Ctrl + molette", "Glisser-déposer", "Glisser un onglet", "Glisser un titre" })
            Assert.True(mouse.Contains(word), word);
    }

    [Fact]
    public void Dans_le_guide_affiche_le_programme_imité_n_est_cité_que_pour_l_import_des_connexions()
    {
        foreach (var file in new[] { "README.md", "README.en.md" })
        {
            // L'histoire du projet, avant le guide, reste sur GitHub : seul le guide s'affiche dans le programme.
            var text = RootFile(file);
            int start = text.IndexOf("<!-- guide -->", StringComparison.Ordinal);
            int end = text.IndexOf("<!-- /guide -->", StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            var lines = text[start..end].Split('\n').Where(l => l.Contains("Total Commander", StringComparison.Ordinal)).ToList();
            var line = Assert.Single(lines);
            Assert.Contains("FileZilla", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Lecture_d_un_guide()
    {
        const string text = """
            avant
            <!-- guide -->
            ## Un

            Première ligne
            suite.

            ### Détail

            - point
            | Touche | Action |
            |---|---|
            | `*.* \| *.tmp` | exclure |
            ## Deux
            <!-- raccourcis -->
            | caché | caché |
            <!-- /raccourcis -->
            fin
            <!-- /guide -->
            après
            """;
        var guide = GuideDocument.Parse(text);
        Assert.Equal(["Un", "Deux"], guide.Select(c => c.Title));
        Assert.Equal("Première ligne suite.", guide[0].Blocks[0].Text);
        Assert.Equal(GuideBlockKind.Heading, guide[0].Blocks[1].Kind);
        Assert.Equal(GuideBlockKind.Bullet, guide[0].Blocks[2].Kind);
        Assert.Equal("`*.* | *.tmp`", guide[0].Blocks[3].Rows![1][0]);
        Assert.Equal([GuideBlockKind.Shortcuts, GuideBlockKind.Paragraph], guide[1].Blocks.Select(b => b.Kind));
    }

    [Fact]
    public void Gras_et_code()
    {
        Assert.Equal(
            [new GuideSpan("a "), new GuideSpan("b", Bold: true), new GuideSpan(" c "), new GuideSpan("d", Code: true), new GuideSpan(" e")],
            GuideDocument.Spans("a **b** c `d` e"));
        Assert.Equal([new GuideSpan("a ** b")], GuideDocument.Spans("a ** b"));
    }

    [Theory]
    [InlineData("MOLETTE")]
    [InlineData("deplacer")]
    [InlineData("détail")]
    public void Recherche_sans_tenir_compte_des_majuscules_ni_des_accents(string query)
    {
        var chapter = new GuideChapter("Détail", [new GuideBlock(GuideBlockKind.Table, string.Empty, [["Ctrl + molette", "Déplacer"]])]);
        Assert.True(chapter.Contains(query));
        Assert.False(chapter.Contains("introuvable"));
    }

    [Fact]
    public void Les_astuces_tournent_en_boucle()
    {
        Assert.True(Tips.All.Count >= 20);
        Assert.Equal(Tips.All.Count, Tips.All.Distinct().Count());
        Assert.Equal(Tips.All[0], Tips.At(Tips.All.Count));
        Assert.Equal(Tips.All[1], Tips.At(Tips.All.Count + 1));
    }
}
