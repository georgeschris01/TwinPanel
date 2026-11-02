// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using ICSharpCode.SharpZipLib.Zip;

namespace TwinPanel.Core.Archives;

/// <summary>Écriture d'un zip chiffré en AES-256, le seul chiffrement que TwinPanel crée (les noms des fichiers restent lisibles : c'est le format zip).</summary>
internal static class EncryptedZip
{
    private const int CompressionLevel = 6;
    private const int AesBits = 256;

    public static ZipOutputStream Open(Stream output, string password)
    {
        var zip = new ZipOutputStream(output) { Password = password, UseZip64 = UseZip64.Dynamic };
        zip.SetLevel(CompressionLevel);
        return zip;
    }

    public static void AddFolder(ZipOutputStream zip, string key, DateTime modified, int attributes = 0)
    {
        zip.PutNextEntry(new ZipEntry(key + "/") { DateTime = modified, IsUnicodeText = true, ExternalFileAttributes = attributes });
        zip.CloseEntry();
    }

    /// <summary>Ouvre un fichier dans l'archive : ses données s'écrivent ensuite dans <paramref name="zip"/>, puis CloseEntry().</summary>
    /// <param name="size">Taille exacte : au-delà de 4 Go, le zip a besoin de la connaître dès l'en-tête.</param>
    public static void BeginFile(ZipOutputStream zip, string key, long size, DateTime modified, int attributes = 0, string? comment = null)
    {
        zip.PutNextEntry(new ZipEntry(key)
        {
            AESKeySize = AesBits,
            DateTime = modified,
            IsUnicodeText = true,
            Size = size,
            ExternalFileAttributes = attributes,
            Comment = comment,
        });
    }
}
