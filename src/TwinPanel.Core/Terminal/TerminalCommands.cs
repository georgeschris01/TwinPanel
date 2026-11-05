// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Terminal;

/// <summary>Règles des commandes enregistrées du terminal.</summary>
public static class TerminalCommands
{
    /// <summary>Celles à proposer : les communes, plus celles du serveur du terminal affiché.</summary>
    public static IEnumerable<SavedTerminalCommand> Visible(IEnumerable<SavedTerminalCommand> all, Guid? serverId) =>
        all.Where(c => c.ServerId is null || c.ServerId == serverId);

    /// <summary>Ce qu'il faut envoyer au terminal pour écrire la commande telle quelle et la lancer.</summary>
    public static string Input(string command, bool bracketedPaste) =>
        TerminalKeys.Paste(command.TrimEnd('\r', '\n'), bracketedPaste) + "\r";
}
