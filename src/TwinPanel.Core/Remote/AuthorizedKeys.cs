// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Installe une clé PUBLIQUE dans ~/.ssh/authorized_keys sur un serveur (comme la commande ssh-copy-id),
/// par une connexion SFTP / SCP déjà ouverte (en général par mot de passe). La clé privée ne quitte jamais le PC.
/// </summary>
public static class AuthorizedKeys
{
    /// <summary>« ssh-ed25519 AAAA… » : type et clé, sans le commentaire (pour comparer).</summary>
    public static string KeyPart(string publicKeyLine)
    {
        var parts = publicKeyLine.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : publicKeyLine.Trim();
    }

    /// <summary>Retourne false si la clé était déjà présente (rien n'est modifié).</summary>
    public static async Task<bool> InstallAsync(IRemoteFileSystem fs, string publicKeyLine, CancellationToken ct)
    {
        var line = publicKeyLine.Trim();
        if (!line.StartsWith("ssh-", StringComparison.Ordinal) && !line.StartsWith("ecdsa-", StringComparison.Ordinal))
            throw new RemoteException(L.T("Ce n'est pas une clé publique SSH."));

        var sshDir = RemotePath.Combine(fs.HomeDirectory, ".ssh");
        var file = RemotePath.Combine(sshDir, "authorized_keys");

        if (await fs.StatAsync(sshDir, ct).ConfigureAwait(false) is null)
        {
            await fs.CreateDirectoryAsync(sshDir, ct).ConfigureAwait(false);
            if (fs.SupportsPermissions) await fs.SetPermissionsAsync(sshDir, 700, ct).ConfigureAwait(false);
        }

        var existing = string.Empty;
        if (await fs.StatAsync(file, ct).ConfigureAwait(false) is { IsDirectory: false })
        {
            using var ms = new MemoryStream();
            await fs.DownloadAsync(file, ms, ct).ConfigureAwait(false);
            existing = Encoding.UTF8.GetString(ms.ToArray());
        }

        var wanted = KeyPart(line);
        if (existing.Split('\n').Any(l => KeyPart(l.Trim()) == wanted && l.Trim().Length > 0)) return false;

        var content = existing.Length == 0 || existing.EndsWith('\n') ? existing + line + "\n" : existing + "\n" + line + "\n";
        var part = file + RemoteTransfer.PartSuffix;
        using (var input = new MemoryStream(Encoding.UTF8.GetBytes(content)))
            await fs.UploadAsync(input, part, ct).ConfigureAwait(false);
        // Droits avant la mise en place : sshd refuse un authorized_keys lisible par d'autres.
        if (fs.SupportsPermissions) await fs.SetPermissionsAsync(part, 600, ct).ConfigureAwait(false);
        await fs.RenameAsync(part, file, overwrite: true, ct).ConfigureAwait(false);
        return true;
    }
}
