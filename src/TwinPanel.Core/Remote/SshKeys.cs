// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace TwinPanel.Core.Remote;

/// <summary>Une clé privée lue : sa description publique (ce qui peut être affiché ou copié).</summary>
public sealed record SshKeyInfo(string KeyType, string Fingerprint, string PublicKeyLine, bool HasPassphrase);

/// <summary>La clé privée est protégée par une phrase de passe qui n'a pas été donnée (ou qui est fausse).</summary>
public sealed class KeyPassphraseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Lecture, contrôle et création de clés SSH (formats OpenSSH, PEM, PuTTY .ppk lus par SSH.NET).</summary>
public static class SshKeys
{
    /// <summary>Lit une clé privée (texte) et calcule sa partie publique. Lève <see cref="KeyPassphraseException"/> si la phrase de passe manque ou est fausse.</summary>
    public static SshKeyInfo Inspect(string privateKeyText, string? passphrase, string comment = "")
    {
        using var file = Open(privateKeyText, passphrase);
        var blob = file.HostKeyAlgorithms.First().Data;
        var type = ReadFirstString(blob);
        var line = $"{type} {Convert.ToBase64String(blob)}" + (comment.Length > 0 ? " " + comment : string.Empty);
        return new SshKeyInfo(type, KnownHosts.Sha256Fingerprint(blob), line, !string.IsNullOrEmpty(passphrase) && NeedsPassphrase(privateKeyText));
    }

    /// <summary>Vrai si la clé ne s'ouvre pas sans phrase de passe.</summary>
    public static bool NeedsPassphrase(string privateKeyText)
    {
        try { using var _ = Open(privateKeyText, null); return false; }
        catch (KeyPassphraseException) { return true; }
    }

    /// <summary>Ouvre la clé pour l'authentification SSH. L'appelant libère l'objet.</summary>
    public static PrivateKeyFile Open(string privateKeyText, string? passphrase)
    {
        var bytes = Encoding.UTF8.GetBytes(privateKeyText);
        var stream = new MemoryStream(bytes, writable: false);
        try
        {
            return string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(stream) : new PrivateKeyFile(stream, passphrase);
        }
        catch (SshPassPhraseNullOrEmptyException ex)
        {
            throw new KeyPassphraseException(L.T("Cette clé est protégée par une phrase de passe."), ex);
        }
        catch (Exception ex) when (ex is SshException or CryptographicException or InvalidOperationException or FormatException or ArgumentException or IndexOutOfRangeException)
        {
            // Une phrase de passe fausse donne souvent une erreur de déchiffrement ou de format.
            if (!string.IsNullOrEmpty(passphrase) || LooksEncrypted(privateKeyText))
                throw new KeyPassphraseException(L.T("La phrase de passe est incorrecte (ou la clé est abîmée)."), ex);
            throw new InvalidDataException(L.T("Ce fichier n'est pas une clé privée reconnue (OpenSSH, PEM ou PuTTY .ppk)."), ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool LooksEncrypted(string text) =>
        text.Contains("ENCRYPTED", StringComparison.Ordinal) || text.Contains("Encryption: aes", StringComparison.Ordinal)
        || (text.Contains("OPENSSH PRIVATE KEY", StringComparison.Ordinal) && OpenSshCipher(text) is { } c && c != "none");

    private static string? OpenSshCipher(string text)
    {
        try
        {
            var body = string.Concat(text.Split('\n').Where(l => !l.StartsWith("-----", StringComparison.Ordinal)).Select(l => l.Trim()));
            var data = Convert.FromBase64String(body);
            const string magic = "openssh-key-v1\0";
            if (data.Length < magic.Length + 4 || Encoding.ASCII.GetString(data, 0, magic.Length) != magic) return null;
            int len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(magic.Length));
            return Encoding.ASCII.GetString(data, magic.Length + 4, len);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return null; }
    }

    private static string ReadFirstString(byte[] blob)
    {
        int len = BinaryPrimitives.ReadInt32BigEndian(blob);
        return Encoding.ASCII.GetString(blob, 4, len);
    }

    /// <summary>
    /// Crée une nouvelle clé Ed25519 (la plus recommandée aujourd'hui) au format OpenSSH, non chiffrée :
    /// dans TwinPanel elle est rangée dans le coffre chiffré. Retourne le texte de la clé privée.
    /// </summary>
    public static string GenerateEd25519(string comment)
    {
        var priv = new Ed25519PrivateKeyParameters(new SecureRandom());
        var seed = priv.GetEncoded();
        var pub = priv.GeneratePublicKey().GetEncoded();
        try { return WriteOpenSshEd25519(seed, pub, comment); }
        finally { CryptographicOperations.ZeroMemory(seed); }
    }

    /// <summary>Format « openssh-key-v1 » (voir PROTOCOL.key d'OpenSSH), sans chiffrement.</summary>
    internal static string WriteOpenSshEd25519(byte[] seed, byte[] pub, string comment)
    {
        var pubBlob = new SshWriter().String("ssh-ed25519").Bytes(pub).ToArray();

        var checkInt = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
        var secret = new byte[64];
        seed.CopyTo(secret, 0);
        pub.CopyTo(secret, 32);
        var priv = new SshWriter().UInt32(checkInt).UInt32(checkInt)
            .String("ssh-ed25519").Bytes(pub).Bytes(secret).String(comment);
        CryptographicOperations.ZeroMemory(secret);
        for (byte pad = 1; priv.Length % 8 != 0; pad++) priv.Byte(pad);

        var privBytes = priv.ToArray();
        var all = new SshWriter().Raw(Encoding.ASCII.GetBytes("openssh-key-v1\0"))
            .String("none").String("none").Bytes([]).UInt32(1)
            .Bytes(pubBlob).Bytes(privBytes).ToArray();
        CryptographicOperations.ZeroMemory(privBytes);
        priv.Clear();

        var b64 = Convert.ToBase64String(all);
        CryptographicOperations.ZeroMemory(all);
        var sb = new StringBuilder("-----BEGIN OPENSSH PRIVATE KEY-----\n");
        for (int i = 0; i < b64.Length; i += 70) sb.Append(b64, i, Math.Min(70, b64.Length - i)).Append('\n');
        sb.Append("-----END OPENSSH PRIVATE KEY-----\n");
        return sb.ToString();
    }

    private sealed class SshWriter
    {
        private readonly MemoryStream _ms = new();
        public int Length => (int)_ms.Length;
        public SshWriter Raw(byte[] b) { _ms.Write(b); return this; }
        public SshWriter Byte(byte b) { _ms.WriteByte(b); return this; }
        public SshWriter UInt32(uint v) { Span<byte> s = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(s, v); _ms.Write(s); return this; }
        public SshWriter Bytes(byte[] b) { UInt32((uint)b.Length); _ms.Write(b); return this; }
        public SshWriter String(string s) => Bytes(Encoding.UTF8.GetBytes(s));
        public byte[] ToArray() => _ms.ToArray();
        public void Clear() { var buf = _ms.GetBuffer(); CryptographicOperations.ZeroMemory(buf); }
    }
}
