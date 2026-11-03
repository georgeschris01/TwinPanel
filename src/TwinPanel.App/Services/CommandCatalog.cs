// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Windows.Input;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Services;

/// <summary>Une commande de TwinPanel : utilisable par un raccourci, un bouton personnel ou le menu Démarrer.</summary>
/// <param name="Id">Identifiant stable (enregistré dans les réglages).</param>
/// <param name="CommandName">Propriété « …Command » de la fenêtre principale qui l'exécute.</param>
/// <param name="DefaultGestures">Raccourcis par défaut, séparés par « ; » (vide : aucun).</param>
public sealed record CommandInfo(string Id, string Label, string Category, string DefaultGestures, string? CommandName = null, object? Parameter = null)
{
    public string Command => CommandName ?? Id;
    public override string ToString() => $"{Category} — {Label}";
}

/// <summary>Toutes les commandes proposées à la personnalisation.</summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandInfo> All { get; } =
    [
        // Navigation
        new("GoBack", L.T("Dossier précédent"), "Navigation", "Alt+Left"),
        new("GoForward", L.T("Dossier suivant"), "Navigation", "Alt+Right"),
        new("ShowHistory", L.T("Dossiers récents (historique)"), "Navigation", "Alt+Down"),
        new("GoUp", L.T("Dossier parent"), "Navigation", "Ctrl+PageUp"),
        new("GoRoot", L.T("Racine du lecteur / du serveur"), "Navigation", "Ctrl+OemPipe"),
        new("GoHome", L.T("Dossier personnel"), "Navigation", ""),
        new("RefreshAll", L.T("Rafraîchir les deux panneaux"), "Navigation", "Ctrl+R"),
        new("NewTab", L.T("Nouvel onglet"), "Navigation", "Ctrl+T"),
        new("CloseTab", L.T("Fermer l'onglet"), "Navigation", "Ctrl+W"),
        new("ShowTabSets", L.T("Jeux d'onglets"), "Navigation", "Ctrl+Shift+J"),
        new("SaveTabSet", L.T("Enregistrer les onglets…"), "Navigation", ""),
        new("SwapPanels", L.T("Échanger les panneaux"), "Navigation", "Ctrl+U"),
        new("SameFolderInOther", L.T("Même dossier dans l'autre panneau"), "Navigation", "Ctrl+Shift+O"),
        new("OpenInOther", L.T("Ouvrir le dossier sous le curseur dans l'autre panneau"), "Navigation", "Ctrl+Shift+Right;Ctrl+Shift+Left"),
        // Affichage
        new("ToggleHidden", L.T("Afficher / masquer les fichiers cachés"), L.T("Affichage"), "Ctrl+H"),
        new("SortByName", L.T("Trier par nom"), L.T("Affichage"), "Ctrl+F3", "SortBy", "Name"),
        new("SortByExtension", L.T("Trier par extension"), L.T("Affichage"), "Ctrl+F4", "SortBy", "Extension"),
        new("SortByDate", L.T("Trier par date"), L.T("Affichage"), "Ctrl+F5", "SortBy", "Date"),
        new("SortBySize", L.T("Trier par taille"), L.T("Affichage"), "Ctrl+F6", "SortBy", "Size"),
        new("ToggleQuickView", L.T("Vue rapide"), L.T("Affichage"), "Ctrl+Q"),
        new("ToggleTree", L.T("Arborescence des dossiers"), L.T("Affichage"), "Ctrl+F8"),
        new("ToggleThumbnails", L.T("Miniatures (images et vidéos)"), L.T("Affichage"), "Ctrl+Shift+F1"),
        new("ToggleFlatView", L.T("Vue à plat (fichiers de tous les sous-dossiers)"), L.T("Affichage"), "Ctrl+B"),
        new("CycleTheme", L.T("Thème suivant (système, clair, sombre)"), L.T("Affichage"), ""),
        // Fichiers
        new("View", L.T("Voir (visionneuse)"), L.T("Fichiers"), "F3"),
        new("Edit", L.T("Éditer"), L.T("Fichiers"), "F4"),
        new("EditInternal", L.T("Éditer dans l'éditeur intégré"), L.T("Fichiers"), ""),
        new("NewFile", L.T("Nouveau fichier texte"), L.T("Fichiers"), "Shift+F4"),
        new("Copy", L.T("Copier"), L.T("Fichiers"), "F5"),
        new("Move", L.T("Déplacer"), L.T("Fichiers"), "F6"),
        new("MakeDirectory", L.T("Nouveau dossier"), L.T("Fichiers"), "F7"),
        new("Delete", L.T("Supprimer"), L.T("Fichiers"), "F8"),
        new("Rename", L.T("Renommer"), L.T("Fichiers"), "Shift+F6"),
        new("MultiRenameFiles", L.T("Renommage multiple"), L.T("Fichiers"), "Ctrl+M"),
        new("UndoRename", L.T("Annuler le dernier renommage multiple"), L.T("Fichiers"), "Ctrl+Z"),
        new("OpenWithDefaultApp", L.T("Ouvrir avec l'application associée"), L.T("Fichiers"), "Alt+F3"),
        new("Print", L.T("Imprimer"), L.T("Fichiers"), ""),
        new("Share", L.T("Partager…"), L.T("Fichiers"), ""),
        new("ShowProperties", L.T("Propriétés (droits, propriétaire, attributs, sécurité)"), L.T("Fichiers"), "Alt+Enter"),
        // Outils
        new("Search", L.T("Rechercher des fichiers"), L.T("Outils"), "Alt+F7"),
        new("CompareFolders", L.T("Comparer les dossiers"), L.T("Outils"), "Shift+F2"),
        new("CompareFiles", L.T("Comparer le contenu de deux fichiers"), L.T("Outils"), "Ctrl+Shift+F2"),
        new("SyncFolders", L.T("Synchroniser les dossiers"), L.T("Outils"), "Ctrl+Shift+S"),
        new("CreateChecksums", L.T("Créer des sommes de contrôle"), L.T("Outils"), ""),
        new("VerifyChecksums", L.T("Vérifier des sommes de contrôle"), L.T("Outils"), ""),
        new("PackArchive", L.T("Créer une archive"), L.T("Outils"), "Alt+F5"),
        new("UnpackArchive", L.T("Extraire l'archive"), L.T("Outils"), "Alt+F9"),
        new("TestArchive", L.T("Tester l'archive"), L.T("Outils"), ""),
        new("OpenTerminal", L.T("Ouvrir un terminal ici"), L.T("Outils"), "Ctrl+Shift+T"),
        new("ToggleTerminal", L.T("Terminal intégré : afficher / masquer"), L.T("Outils"), "F12"),
        new("NewTerminal", L.T("Nouveau terminal intégré ici"), L.T("Outils"), "Ctrl+F12"),
        new("DuplicateTerminal", L.T("Dupliquer le terminal intégré"), L.T("Outils"), ""),
        new("ToggleTerminalSplit", L.T("Deux terminaux côte à côte"), L.T("Outils"), "Ctrl+Shift+F12"),
        new("SwitchTerminalSide", L.T("Terminal intégré : changer de côté"), L.T("Outils"), "Alt+F12"),
        new("SearchTerminal", L.T("Terminal intégré : chercher dans le texte"), L.T("Outils"), ""),
        new("OpenOptions", "Options", L.T("Outils"), ""),
        new("ToolSettings", L.T("Éditeur et visionneuse"), L.T("Outils"), ""),
        // Connexions
        new("OpenConnectionManager", L.T("Gestionnaire de connexions"), L.T("Connexions"), "Ctrl+F"),
        new("QuickConnect", L.T("Connexion rapide"), L.T("Connexions"), "Ctrl+N"),
        new("ShowPendingTransfers", L.T("Transferts non terminés"), L.T("Connexions"), ""),
        new("Disconnect", L.T("Déconnecter le panneau actif"), L.T("Connexions"), "Ctrl+Shift+F"),
        new("ChangePermissions", L.T("Droits (ouvre les Propriétés)"), L.T("Connexions"), ""),
        // Favoris
        new("AddFavorite", L.T("Ajouter le dossier aux favoris"), L.T("Favoris"), ""),
        // Aide
        new("ShowHelp", L.T("Guide d'utilisation"), L.T("Aide"), "F1"),
        new("ShowShortcutsHelp", L.T("Liste des raccourcis clavier"), L.T("Aide"), ""),
        new("ShowAbout", L.T("À propos de TwinPanel"), L.T("Aide"), ""),
    ];

    /// <summary>Touches gérées directement par les panneaux (non modifiables), pour information.</summary>
    public static IReadOnlyList<(string Keys, string Action)> FixedKeys { get; } =
    [
        ("Tab", L.T("Passer à l'autre panneau")),
        (L.T("Ctrl+Tab / Ctrl+Maj+Tab"), L.T("Onglet suivant / précédent")),
        (L.T("Entrée / Retour arrière"), L.T("Ouvrir / dossier parent")),
        (L.T("Inser, Espace, Maj+flèches"), L.T("Marquer (Espace sur un dossier : calcule sa taille)")),
        ("Num+ / Num- / Num* / Ctrl+A", L.T("Marquer selon un masque / démarquer / inverser / tout")),
        (L.T("Lettres"), L.T("Recherche rapide dans la liste")),
        ("Ctrl+D", L.T("Menu des favoris")),
        ("Ctrl+L", L.T("Modifier le chemin")),
        (L.T("Ctrl+Entrée / Ctrl+Maj+Entrée"), L.T("Nom / chemin complet dans la ligne de commande")),
        ("Ctrl+C / Ctrl+V", L.T("Dans le terminal : copier la sélection (sans sélection : interrompre) / coller")),
        (L.T("Ctrl+Maj+F"), L.T("Dans le terminal : chercher dans le texte")),
    ];

    public static CommandInfo? Find(string? id) => id is null ? null : All.FirstOrDefault(c => c.Id == id);

    /// <summary>La commande exécutable correspondante dans la fenêtre principale.</summary>
    public static ICommand? Resolve(MainWindowViewModel vm, CommandInfo info) =>
        vm.GetType().GetProperty(info.Command + "Command")?.GetValue(vm) as ICommand;

    /// <summary>Raccourcis en vigueur : ceux de l'utilisateur, sinon ceux par défaut.</summary>
    public static string GesturesFor(CommandInfo info, IReadOnlyDictionary<string, string> overrides) =>
        overrides.TryGetValue(info.Id, out var g) ? g : info.DefaultGestures;
}
