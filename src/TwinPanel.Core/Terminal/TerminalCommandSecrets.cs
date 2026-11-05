// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Terminal;

/// <summary>
/// Commandes « secrètes » du terminal (un mot de passe à taper, par exemple) : leur texte est dans le coffre chiffré,
/// jamais dans settings.json. Les réglages ne gardent que le nom et l'identifiant.
/// </summary>
public static class TerminalCommandSecrets
{
    public static string VaultId(SavedTerminalCommand command) => SecretIds.TerminalCommand(command.Id);

    /// <summary>Le texte à envoyer ; null si celui d'une commande secrète n'est plus dans le coffre. Lève <see cref="VaultLockedException"/> si le coffre est verrouillé.</summary>
    public static string? Text(SavedTerminalCommand command, SecretVault? vault) =>
        !command.Secret ? command.Command : vault?.Get(VaultId(command));

    /// <summary>
    /// Enregistre le texte, dans le coffre ou dans les réglages. Le coffre est écrit d'abord :
    /// s'il est verrouillé, la commande reste telle qu'elle était.
    /// </summary>
    public static void Store(SavedTerminalCommand command, string text, bool secret, SecretVault? vault)
    {
        if (secret)
        {
            if (vault is null) throw new InvalidOperationException(L.T("Le coffre des mots de passe n'est pas disponible."));
            if (command.Id == Guid.Empty) command.Id = Guid.NewGuid();
            vault.Set(VaultId(command), text);
            command.Command = string.Empty;
            command.Secret = true;
        }
        else
        {
            if (command.Secret) vault?.Remove(VaultId(command));
            command.Command = text;
            command.Secret = false;
        }
    }

    /// <summary>Commande supprimée : son secret part avec elle.</summary>
    public static void Forget(SavedTerminalCommand command, SecretVault? vault)
    {
        if (command.Secret) vault?.Remove(VaultId(command));
    }
}
