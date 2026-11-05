// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.About;

/// <summary>Une bibliothèque d'un autre développeur livrée avec TwinPanel.</summary>
public sealed record ThirdPartyComponent(string Name, string Role, string License, string Url);

/// <summary>
/// Bibliothèques livrées avec TwinPanel (fenêtre « À propos » → « Bibliothèques utilisées »).
/// Elles restent sous leur propre licence : la licence de TwinPanel ne s'applique pas à leur code.
/// À tenir à jour avec COMPOSANTS.md quand une bibliothèque est ajoutée ou retirée.
/// </summary>
public static class ThirdPartyComponents
{
    public static IReadOnlyList<ThirdPartyComponent> Shipped { get; } =
    [
        new(L.T("Avalonia (avec DataGrid, ColorPicker, thèmes)"), L.T("Toute l'interface graphique"), "MIT", "https://avaloniaui.net"),
        new("SkiaSharp / Skia", L.T("Dessin de l'interface"), "MIT / BSD-3", "https://github.com/mono/SkiaSharp"),
        new("HarfBuzzSharp / HarfBuzz", L.T("Mise en forme du texte"), "MIT / « Old MIT »", "https://github.com/harfbuzz/harfbuzz"),
        new("ANGLE", L.T("Rendu graphique accéléré sous Windows"), "BSD-3", "https://chromium.googlesource.com/angle/angle"),
        new(L.T("Police Inter"), L.T("Police de l'interface"), "SIL Open Font License 1.1", "https://rsms.me/inter"),
        new("CommunityToolkit.Mvvm", L.T("Code des écrans"), "MIT", "https://github.com/CommunityToolkit/dotnet"),
        new("SharpCompress", L.T("Lecture des archives zip, 7z, rar…"), "MIT", "https://github.com/adamhathcock/sharpcompress"),
        new("SharpZipLib", L.T("Écriture des zip protégés par un mot de passe (AES-256)"), "MIT", "https://github.com/icsharpcode/SharpZipLib"),
        new("SSH.NET", L.T("Connexions SFTP et SCP"), "MIT", "https://github.com/sshnet/SSH.NET"),
        new("BouncyCastle", L.T("Cryptographie SSH, création des clés"), "MIT", "https://www.bouncycastle.org"),
        new("FluentFTP", L.T("Connexions FTP et FTPS"), "MIT", "https://github.com/robinrodricks/FluentFTP"),
        new("MediaInfo (libmediainfo)", L.T("Infos des vidéos et des musiques"), "BSD-2", "https://mediaarea.net/MediaInfo"),
        new(L.T("libcurl (dans MediaInfo)"), L.T("Lecture réseau de MediaInfo"), L.T("Licence curl"), "https://curl.se"),
        new(L.T("OpenSSL (dans MediaInfo)"), L.T("Chiffrement de MediaInfo"), "Apache-2.0", "https://www.openssl.org"),
        new(L.T("libssh2 (dans MediaInfo)"), L.T("Accès SSH de MediaInfo"), "BSD-3", "https://libssh2.org"),
        new(L.T("Brotli (dans MediaInfo)"), L.T("Décompression de MediaInfo"), "MIT", "https://github.com/google/brotli"),
        new("ATL .NET", L.T("Tags des musiques et des MP4"), "MIT", "https://github.com/Zeugma440/atldotnet"),
        new("Ude.NetStandard", L.T("Détection de l'encodage des textes (utilisé par ATL .NET)"), L.T("MPL-1.1 (ou GPL / LGPL) ; code source à ce lien"), "https://github.com/yinyue200/ude"),
        new("MetadataExtractor", L.T("EXIF / GPS des photos"), "Apache-2.0", "https://github.com/drewnoakes/metadata-extractor-dotnet"),
        new(L.T("XmpCore (dans MetadataExtractor)"), L.T("Données XMP des photos"), "BSD (Adobe)", "https://github.com/drewnoakes/xmp-core-dotnet"),
        new("Svg.Skia, ShimSkiaSharp, Svg.Model, Svg.Animation, Svg.SceneGraph", L.T("Dessin des icônes SVG sous Linux"), "MIT", "https://github.com/wieslawsoltes/Svg.Skia"),
        new("Svg.Custom", L.T("Lecture des fichiers SVG"), "Ms-PL", "https://github.com/wieslawsoltes/Svg.Skia"),
        new("ExCSS", L.T("Styles des fichiers SVG"), "MIT", "https://github.com/TylerBrinks/ExCSS"),
        new(".NET 10 (runtime)", L.T("Faire tourner le programme"), "MIT", "https://dotnet.microsoft.com"),
        new("System.Security.Cryptography.ProtectedData", L.T("Chiffrement des mots de passe enregistrés (Windows)"), "MIT", "https://github.com/dotnet/runtime"),
        new("Tmds.DBus.Protocol, MicroCom.Runtime, Microsoft.Extensions.Logging.Abstractions, Microsoft.Extensions.DependencyInjection.Abstractions", L.T("Composants internes"), "MIT", "https://github.com/dotnet/runtime"),
    ];

    /// <summary>Non livrés : servent à fabriquer TwinPanel, ou sont installés par l'utilisateur s'il le souhaite.</summary>
    public static IReadOnlyList<ThirdPartyComponent> NotShipped { get; } =
    [
        new("xUnit", L.T("Tests automatiques (développement seulement)"), "Apache-2.0", "https://xunit.net"),
        new("Inno Setup", L.T("Fabrication de l'installateur"), L.T("Licence Inno Setup"), "https://jrsoftware.org/isinfo.php"),
        new("mkvpropedit (MKVToolNix)", L.T("Facultatif, installé par l'utilisateur : pistes des MKV"), "GPL-2.0", "https://mkvtoolnix.download"),
        new("exiftool", L.T("Facultatif, installé par l'utilisateur : EXIF des photos"), "Artistic / GPL", "https://exiftool.org"),
    ];
}
