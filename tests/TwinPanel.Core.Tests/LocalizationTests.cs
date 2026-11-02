// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using TwinPanel.Core.Localization;
using TwinPanel.Core.Operations;
using Xunit;

namespace TwinPanel.Core.Tests;

/// <summary>Traductions : rien d'oublié dans le code ni dans les fenêtres, et des textes utilisables.</summary>
public class LocalizationTests
{
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TwinPanel.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("TwinPanel.sln"), "src");
    }

    private static IEnumerable<string> Sources(params string[] patterns) =>
        new[] { "TwinPanel.App", "TwinPanel.Core" }
            .SelectMany(project => patterns.SelectMany(p => Directory.EnumerateFiles(Path.Combine(SourceRoot(), project), p, SearchOption.AllDirectories)))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static HashSet<string> KeysInCode()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal) { RenameMedia.Taken }; // balise traduite par L.T(variable)
        foreach (var file in Sources("*.cs"))
        {
            if (file.EndsWith("L.cs", StringComparison.Ordinal) || file.EndsWith("TExtension.cs", StringComparison.Ordinal)) continue;
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, """L\.[TF]\("((?:[^"\\]|\\.)*)"""))
                keys.Add(Regex.Unescape(m.Groups[1].Value));
            foreach (Match m in Regex.Matches(text, """L\.[TF]\(@"((?:[^"]|"")*)"""))
                keys.Add(m.Groups[1].Value.Replace("\"\"", "\"", StringComparison.Ordinal));
        }
        foreach (var file in Sources("*.axaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), """\{l:T '((?:[^'\\]|\\.)*)'\}"""))
                keys.Add(WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, @"\\(.)", "$1")));
        return keys;
    }

    private static List<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Chaque_texte_a_sa_traduction()
    {
        var english = L.LoadEnglish();
        var missing = KeysInCode().Where(k => !english.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Sans traduction :\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Aucune_traduction_orpheline()
    {
        var keys = KeysInCode();
        var unused = L.LoadEnglish().Keys.Where(k => !keys.Contains(k)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unused.Count == 0, "Plus utilisées :\n" + string.Join("\n", unused));
    }

    [Fact]
    public void Les_valeurs_a_inserer_sont_les_memes_dans_les_deux_langues()
    {
        var wrong = L.LoadEnglish().Where(p => !Placeholders(p.Key).SequenceEqual(Placeholders(p.Value))).Select(p => p.Key).ToList();
        Assert.True(wrong.Count == 0, "Valeurs différentes :\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Chaque_traduction_est_remplie_et_sans_mention_du_format_français()
    {
        var english = L.LoadEnglish();
        Assert.DoesNotContain(english, p => string.IsNullOrWhiteSpace(p.Value));
        Assert.DoesNotContain(english, p => p.Value.Contains('«') || p.Value.Contains('»'));
        Assert.DoesNotContain(english, p => Regex.IsMatch(p.Value, @"\bCtrl\+Maj\b|\bMaj\+"));
    }

    [Fact]
    public void Espaces_et_retours_a_la_ligne_gardes_au_debut_et_a_la_fin()
    {
        static (bool, bool, bool, bool) Edges(string t) => (t.StartsWith(' '), t.EndsWith(' '), t.StartsWith('\n'), t.EndsWith('\n'));
        var wrong = L.LoadEnglish().Where(p => Edges(p.Key) != Edges(p.Value)).Select(p => p.Key).ToList();
        Assert.True(wrong.Count == 0, "Bords différents : " + string.Join(" / ", wrong));
    }

    [Theory]
    [InlineData("fr", "en-US", "fr")]
    [InlineData("en", "fr-BE", "en")]
    [InlineData("", "fr-BE", "fr")]
    [InlineData("", "fr-CA", "fr")]
    [InlineData("", "nl-BE", "en")]
    [InlineData("", "en-US", "en")]
    [InlineData(null, "de-DE", "en")]
    [InlineData("xx", "fr-FR", "fr")]
    public void Langue_choisie_ou_celle_du_systeme(string? setting, string system, string expected) =>
        Assert.Equal(expected, L.Resolve(setting, CultureInfo.GetCultureInfo(system)));

    [Theory]
    [InlineData("Title", "Titre")]
    [InlineData("Artist", "Artiste")]
    [InlineData("Track", "Piste")]
    [InlineData("Year", "Année")]
    [InlineData("Resolution", "Résolution")]
    [InlineData("Duration", "Durée")]
    [InlineData("Date taken", "Date de prise de vue")]
    [InlineData("Titre", "Titre")]
    [InlineData("Album", "Album")]
    public void Balise_media_en_anglais_ou_en_français(string written, string canonical)
    {
        Assert.Equal(canonical, RenameMedia.Canonical(written));
        Assert.True(MultiRename.UsesMedia($"[{written}] - [N]"));
    }
}
