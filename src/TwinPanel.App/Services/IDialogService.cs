// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core;
using TwinPanel.Core.Operations;

namespace TwinPanel.App.Services;

/// <summary>Réponse à la boîte F5/F6 : destination, conduite en cas de conflit (null = demander).</summary>
public sealed record TransferRequest(string Destination, ConflictPolicy? Policy);

/// <summary>Boîtes de dialogue utilisées par les ViewModels (sans dépendre des vues).</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    /// <summary>selectLength : nombre de caractères présélectionnés (ex. le nom sans l'extension).</summary>
    Task<string?> PromptAsync(string title, string message, string defaultValue = "", int? selectLength = null);
    /// <summary>Mot de passe d'une archive à ouvrir ; null si annulé.</summary>
    Task<string?> AskArchivePasswordAsync(string archiveName, bool wrong);
    /// <summary>Alt+F5 : nom de l'archive à créer et mot de passe facultatif ; null si annulé.</summary>
    Task<PackRequest?> AskPackAsync(string defaultPath, int selectLength);
    Task ShowErrorAsync(string title, string message);
    /// <summary>Retourne null si l'utilisateur annule.</summary>
    Task<ConflictPolicy?> AskConflictAsync(IReadOnlyList<string> conflicts);
    Task<TransferRequest?> AskTransferAsync(string verb, string what, string defaultDestination);
    /// <param name="move">Pour un serveur : renomme un chemin de panneau en un autre (null : disque du PC).</param>
    Task<RenameLog?> ShowMultiRenameAsync(IReadOnlyList<FileEntry> entries, Func<string, string, CancellationToken, Task>? move = null);
    /// <summary>Fenêtre de recherche ; retourne le chemin du fichier choisi, ou null.</summary>
    Task<string?> ShowSearchAsync(string startPath, TwinPanel.Core.Remote.RemoteSessionManager? sessions = null);
    Task<ViewModels.SyncRequest?> ShowSyncAsync(string left, string right, TwinPanel.Core.Remote.RemoteSessionManager? sessions = null,
        ViewModels.SyncProfileAccess? profiles = null, TwinPanel.Core.Compare.SyncProfile? start = null);
    Task ShowCompareFilesAsync(string left, string right);
    /// <summary>Choix parmi plusieurs boutons ; la dernière option sert d'« Annuler » (Échap).</summary>
    Task<T?> ChooseAsync<T>(string title, string message, params (string Label, T? Value)[] choices) where T : struct;
    /// <summary>Guide d'utilisation (fenêtre libre, une seule à la fois).</summary>
    void ShowHelp(IReadOnlyList<TwinPanel.App.Views.HelpShortcut> shortcuts, bool atShortcuts);

    /// <summary>Fenêtre « À propos » avec le logo.</summary>
    /// <param name="downloads">Lit le texte « Téléchargements de cette version… » (Internet) ; null : pas de ligne.</param>
    Task ShowAboutAsync(string title, string text, IReadOnlyList<string> details, Func<CancellationToken, Task<string>>? downloads = null);
    /// <summary>
    /// Cases à cocher (une case grisée n'est pas modifiable). Null si annulé ; sinon l'état de chaque case.
    /// Le bouton de validation n'est actif que si une case au moins est cochée.
    /// </summary>
    Task<bool[]?> ChooseItemsAsync(string title, string message, IReadOnlyList<(string Label, bool Checked, bool Enabled)> items, string okLabel);
    /// <summary>Affiche un message long (liste) avec un bouton OK.</summary>
    Task ShowListAsync(string title, string message, IReadOnlyList<string> lines);
    /// <summary>Visionneuse intégrée (fenêtre non modale).</summary>
    void ShowViewer(IReadOnlyList<string> files, int index);
    /// <summary>Réglages de l'éditeur et de la visionneuse ; retourne vrai si l'utilisateur a validé.</summary>
    /// <summary>Ouvre l'éditeur intégré (fenêtre non modale), en plaçant le curseur sur la ligne donnée.</summary>
    Task ShowEditorAsync(string filePath, int line = 1);
    Task<bool> ShowToolSettingsAsync(TwinPanel.Core.Settings.AppSettings settings);
    /// <summary>Gestionnaire de connexions ; retourne la connexion à ouvrir (« Connecter »), ou null.</summary>
    Task<TwinPanel.Core.Remote.ConnectionProfile?> ShowConnectionManagerAsync(TwinPanel.Core.Remote.ConnectionStore store,
        TwinPanel.Core.Remote.RemoteSessionManager sessions, TwinPanel.Core.Remote.IConnectionPrompts prompts);
    /// <summary>Connexion rapide (adresse, mot de passe facultatif, enregistrer ou non) ; null si annulé.</summary>
    Task<QuickConnectRequest?> AskQuickConnectAsync();
    /// <summary>Fenêtre Options sur une copie des réglages ; vrai si « OK ». page : rubrique à ouvrir (« StartMenu »…).</summary>
    /// <summary>Choix de l'imprimante ; null si annulé.</summary>
    Task<string?> ChoosePrinterAsync(IReadOnlyList<string> printers, string? selected, int fileCount);
    /// <summary>Fenêtre Propriétés (droits, propriétaire, attributs, sécurité) ; vrai si quelque chose a été modifié.</summary>
    Task<bool> ShowPropertiesAsync(IReadOnlyList<FileEntry> entries, TwinPanel.Core.Remote.RemoteSession? session);
    Task<bool> ShowOptionsAsync(TwinPanel.Core.Settings.AppSettings settings, TwinPanel.Core.Remote.ConnectionStore? connections, string? page = null);
}

/// <summary>Le mot de passe n'est jamais enregistré ni écrit dans un journal.</summary>
public sealed record PackRequest(string Path, string? Password);

public sealed record QuickConnectRequest(TwinPanel.Core.Remote.ConnectionProfile Profile, string Password, bool Save);
