// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text.Json;
using TwinPanel.Core.About;

namespace TwinPanel.Core.Tests;

public class AboutTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TwinPanel.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("TwinPanel.sln");
    }

    /// <summary>Paquets dont des fichiers partent avec le programme, et leurs DLL natives pour Windows 64 bits.</summary>
    private static (List<string> Packages, List<string> Natives) ShippedFiles()
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "TwinPanel.App", "obj", "project.assets.json")));
        var packages = new List<string>();
        var natives = new List<string>();
        foreach (var library in assets.RootElement.GetProperty("targets").GetProperty("net10.0").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;
            var files = new List<string>();
            foreach (var kind in new[] { "runtime", "runtimeTargets" })
                if (library.Value.TryGetProperty(kind, out var list))
                    files.AddRange(list.EnumerateObject().Select(f => f.Name).Where(f => !f.EndsWith("_._", StringComparison.Ordinal)));
            if (files.Count == 0) continue;
            packages.Add(library.Name.Split('/')[0]);
            natives.AddRange(files.Where(f => f.Contains("/win-x64/native/", StringComparison.Ordinal) && f.EndsWith(".dll", StringComparison.Ordinal)).Select(f => Path.GetFileNameWithoutExtension(f)!));
        }
        return (packages, natives);
    }

    [Fact]
    public void Every_shipped_library_is_in_the_notices()
    {
        var notices = File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));
        var missing = ShippedFiles().Packages
            .Select(p => p.StartsWith("Avalonia", StringComparison.Ordinal) ? "Avalonia"
                : p.StartsWith("SkiaSharp", StringComparison.Ordinal) ? "SkiaSharp"
                : p.StartsWith("HarfBuzzSharp", StringComparison.Ordinal) ? "HarfBuzzSharp"
                : p == "MediaInfo.Core.Native" ? "MediaInfo"
                : p)
            .Where(p => !notices.Contains(p, StringComparison.Ordinal))
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_shipped_native_library_is_named_in_the_notices_and_in_About()
    {
        // Une DLL inconnue fait échouer le test : il faut alors lire sa licence et l'ajouter ici.
        var labels = new Dictionary<string, string>
        {
            ["av_libglesv2"] = "ANGLE",
            ["brotlicommon"] = "Brotli",
            ["brotlidec"] = "Brotli",
            ["brotlienc"] = "Brotli",
            ["libcrypto-4-x64"] = "OpenSSL",
            ["libcurl"] = "libcurl",
            ["libHarfBuzzSharp"] = "HarfBuzz",
            ["libmediainfo"] = "MediaInfo",
            ["libSkiaSharp"] = "Skia",
            ["libssh2"] = "libssh2",
            ["libssl-4-x64"] = "OpenSSL",
        };
        var notices = File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));
        var about = string.Join('\n', ThirdPartyComponents.Shipped.Select(c => c.Name));
        var natives = ShippedFiles().Natives;
        Assert.NotEmpty(natives);
        foreach (var native in natives)
        {
            Assert.True(labels.TryGetValue(native, out var label), $"DLL sans licence vérifiée : {native}");
            Assert.Contains(label, notices, StringComparison.Ordinal);
            Assert.Contains(label, about, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ude_is_listed_with_its_license_and_source_link()
    {
        var ude = Assert.Single(ThirdPartyComponents.Shipped, c => c.Name == "Ude.NetStandard");
        Assert.Contains("MPL", ude.License, StringComparison.Ordinal);
        var notices = File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md"));
        Assert.Contains("MOZILLA PUBLIC LICENSE", notices, StringComparison.Ordinal);
        Assert.Contains(ude.Url, notices, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_component_has_role_license_and_link()
    {
        foreach (var c in ThirdPartyComponents.Shipped.Concat(ThirdPartyComponents.NotShipped))
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.False(string.IsNullOrWhiteSpace(c.Role));
            Assert.False(string.IsNullOrWhiteSpace(c.License));
            Assert.StartsWith("https://", c.Url);
        }
    }

    [Fact]
    public void No_component_listed_twice()
    {
        var all = ThirdPartyComponents.Shipped.Concat(ThirdPartyComponents.NotShipped).Select(c => c.Name).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
