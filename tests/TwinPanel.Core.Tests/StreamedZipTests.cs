// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Archives;

namespace TwinPanel.Core.Tests;

/// <summary>
/// .zip fabriqué « à la volée » comme ceux de Nextcloud (plusieurs fichiers téléchargés ensemble) : fichiers stockés,
/// tailles après chaque fichier, format Zip64, et numéro de disque 65535 dans la table des matières (le vrai, 0,
/// est dans le champ Zip64). SharpCompress ne sait pas le lire : TwinPanel passe alors par le lecteur de .NET.
/// </summary>
public class StreamedZipTests
{
    [Fact]
    public async Task Nextcloud_like_zip_is_listed_and_extracted()
    {
        using var t = new TempDir();
        var zip = Path.Combine(t.Path, "Audiobooks.zip");
        File.WriteAllBytes(zip, StreamedZip(("a.txt", "bonjour"), ("Livre꞉ un.m4b", "contenu du livre")));

        var all = ArchiveIndex.Load(zip).All;
        Assert.Equal(["Livre꞉ un.m4b", "a.txt"], all.Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(7, all.Single(i => i.Key == "a.txt").Size);

        var dest = Path.Combine(t.Path, "out");
        await ArchiveService.ExtractAsync(zip, ["a.txt", "Livre꞉ un.m4b"], "", dest, ConflictPolicy.Overwrite);
        Assert.Equal("bonjour", File.ReadAllText(Path.Combine(dest, "a.txt")));
        Assert.Equal("contenu du livre", File.ReadAllText(Path.Combine(dest, "Livre꞉ un.m4b")));

        await ArchiveService.TestAsync(zip);
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    /// <summary>Même disposition que le .zip de Nextcloud, octet par octet.</summary>
    private static byte[] StreamedZip(params (string Name, string Text)[] files)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        var central = new List<(byte[] Name, uint Crc, long Size, long Offset, bool Utf8)>();
        foreach (var (name, text) in files)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            bool utf8 = nameBytes.Any(b => b >= 0x80);
            var data = Encoding.UTF8.GetBytes(text);
            uint crc = Crc32(data);
            long offset = ms.Position;
            ushort flags = (ushort)(0x8 | (utf8 ? 0x800 : 0));
            // En-tête local : tailles inconnues (0xFFFFFFFF), champ Zip64 vide, données, puis descripteur Zip64.
            w.Write(0x04034b50u); w.Write((ushort)45); w.Write(flags); w.Write((ushort)0);
            w.Write((ushort)0); w.Write((ushort)0x5821); w.Write(0u); w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
            w.Write((ushort)nameBytes.Length); w.Write((ushort)20); w.Write(nameBytes);
            w.Write((ushort)1); w.Write((ushort)16); w.Write(0L); w.Write(0L);
            w.Write(data);
            w.Write(0x08074b50u); w.Write(crc); w.Write((long)data.Length); w.Write((long)data.Length);
            central.Add((nameBytes, crc, data.Length, offset, utf8));
        }
        long cdStart = ms.Position;
        foreach (var (nameBytes, crc, size, offset, utf8) in central)
        {
            w.Write(0x02014b50u); w.Write((ushort)0x32d); w.Write((ushort)45); w.Write((ushort)(0x8 | (utf8 ? 0x800 : 0)));
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0x5821); w.Write(crc);
            w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
            w.Write((ushort)nameBytes.Length); w.Write((ushort)32); w.Write((ushort)0);
            w.Write((ushort)0xFFFF);                  // numéro de disque : 65535 (le vrai est dans le champ Zip64)
            w.Write((ushort)0); w.Write(0x81A40000u); w.Write(0xFFFFFFFFu);
            w.Write(nameBytes);
            w.Write((ushort)1); w.Write((ushort)28); w.Write(size); w.Write(size); w.Write(offset); w.Write(0u);
        }
        long cdSize = ms.Position - cdStart;
        long eocd64 = ms.Position;
        w.Write(0x06064b50u); w.Write(44L); w.Write((ushort)0x32d); w.Write((ushort)45); w.Write(0u); w.Write(0u);
        w.Write((long)central.Count); w.Write((long)central.Count); w.Write(cdSize); w.Write(cdStart);
        w.Write(0x07064b50u); w.Write(0u); w.Write(eocd64); w.Write(1u);
        w.Write(0x06054b50u); w.Write((ushort)0xFFFF); w.Write((ushort)0xFFFF);
        w.Write((ushort)central.Count); w.Write((ushort)central.Count); w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu); w.Write((ushort)0);
        return ms.ToArray();
    }
}
