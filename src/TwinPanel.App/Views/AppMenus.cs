// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using TwinPanel.App.Services;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

/// <summary>
/// Menus à la souris (bouton ≡ et clic droit) construits à partir du catalogue des commandes : chaque ligne
/// affiche le raccourci EN VIGUEUR (celui choisi dans les Options), et le menu est refait à chaque ouverture.
/// </summary>
public static class AppMenus
{
    /// <summary>Ligne pour une commande du catalogue (null si la commande n'existe pas).</summary>
    public static MenuItem? Cmd(MainWindowViewModel vm, string id, string? label = null, object? parameter = null)
    {
        if (CommandCatalog.Find(id) is not { } info || CommandCatalog.Resolve(vm, info) is not { } command) return null;
        var item = new MenuItem { Header = label ?? info.Label, Command = command, CommandParameter = parameter ?? info.Parameter };
        if (Gesture(CommandCatalog.GesturesFor(info, vm.Settings.Shortcuts)) is { } g) item.InputGesture = g;
        return item;
    }

    /// <summary>Ligne pour une action hors catalogue (marquage…), avec un raccourci affiché pour information.</summary>
    public static MenuItem Act(string label, Action action, string? gesture = null, bool enabled = true)
    {
        var item = new MenuItem { Header = label, IsEnabled = enabled };
        if (gesture is not null && Gesture(gesture) is { } g) item.InputGesture = g;
        item.Click += (_, _) => action();
        return item;
    }

    public static KeyGesture? Gesture(string gestures)
    {
        var first = gestures.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null) return null;
        try { return KeyGesture.Parse(first); }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException) { return null; }
    }

    private static void Add(ItemCollection items, params MenuItem?[] entries)
    {
        foreach (var e in entries) if (e is not null) items.Add(e);
    }

    /// <summary>Nom choisi par l'utilisateur dans un menu : « _ » y désigne une touche de raccourci, doublé il s'affiche tel quel.</summary>
    public static string Literal(string text) => text.Replace("_", "__");

    private static MenuItem Sub(string header) => new() { Header = header };

    /// <summary>Liste des dossiers récents (Alt+Bas, clic droit sur Précédent, menu ≡).</summary>
    public static void FillRecent(ItemCollection items, MainWindowViewModel vm)
    {
        items.Clear();
        var recent = vm.RecentEntries();
        if (recent.Count == 0)
        {
            items.Add(new MenuItem { Header = L.T("(aucun dossier récent)"), IsEnabled = false });
            return;
        }
        foreach (var e in recent) items.Add(Plain(e.Label, e.Label, vm.OpenRecentCommand, e.Stored));
        items.Add(new Separator());
        items.Add(new MenuItem { Header = L.T("Vider l'historique"), Command = vm.ClearRecentCommand });
    }

    /// <summary>Ligne au texte libre (chemin, nom choisi) : un TextBlock, car dans un menu « _ » serait pris pour une lettre d'accès et disparaîtrait.</summary>
    private static MenuItem Plain(string text, string tip, ICommand command, object parameter)
    {
        var item = new MenuItem
        {
            Header = new TextBlock { Text = text, MaxWidth = 620, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
            Command = command,
            CommandParameter = parameter,
        };
        ToolTip.SetTip(item, tip);
        return item;
    }

    /// <summary>Jeux d'onglets (Ctrl+Maj+J, menu ≡ → Onglets) : enregistrer, rappeler, supprimer.</summary>
    public static void AddTabSets(ItemCollection items, MainWindowViewModel vm)
    {
        Add(items, Cmd(vm, "SaveTabSet"));
        if (vm.TabSets.Count == 0)
        {
            items.Add(new MenuItem { Header = L.T("(aucun jeu enregistré)"), IsEnabled = false });
            return;
        }
        items.Add(new Separator());
        foreach (var set in vm.TabSets) items.Add(Plain(set.Name, set.Summary, vm.OpenTabSetCommand, set));
        items.Add(new Separator());
        var remove = Sub(L.T("Supprimer un jeu"));
        foreach (var set in vm.TabSets) remove.Items.Add(Plain(set.Name, set.Summary, vm.DeleteTabSetCommand, set));
        items.Add(remove);
    }

    /// <summary>Le menu ≡ complet : Fichiers, Marquer, Commandes, Affichage, Connexions, Outils, Aide.</summary>
    public static void BuildMain(ItemCollection items, MainWindowViewModel vm, Func<bool, Task> copyToClipboard)
    {
        items.Clear();
        var tab = vm.ActivePanel.Current;

        var files = Sub(L.T("Fichiers"));
        Add(files.Items, Cmd(vm, "View"), Cmd(vm, "Edit"), Cmd(vm, "EditInternal"), Cmd(vm, "OpenWithDefaultApp"), Cmd(vm, "NewFile"));
        files.Items.Add(new Separator());
        Add(files.Items, Cmd(vm, "Copy", L.T("Copier…")), Cmd(vm, "Move", L.T("Déplacer…")), Cmd(vm, "Rename", L.T("Renommer…")), Cmd(vm, "MakeDirectory", L.T("Nouveau dossier…")), Cmd(vm, "Delete", L.T("Supprimer…")));
        files.Items.Add(new Separator());
        Add(files.Items, Cmd(vm, "MultiRenameFiles", L.T("Renommage multiple…")), Cmd(vm, "UndoRename"));
        files.Items.Add(new Separator());
        Add(files.Items, Cmd(vm, "PackArchive", L.T("Compresser…")), Cmd(vm, "UnpackArchive", L.T("Décompresser…")), Cmd(vm, "TestArchive"));
        files.Items.Add(new Separator());
        Add(files.Items, Cmd(vm, "Print"), Cmd(vm, "ShowProperties", L.T("Propriétés…")));
        files.Items.Add(new Separator());
        files.Items.Add(Act(L.T("Quitter"), () => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Close(), "Alt+F4"));
        items.Add(files);

        var mark = Sub(L.T("Marquer"));
        mark.Items.Add(Act(L.T("Tout marquer"), () => tab.MarkAll(true), "Ctrl+A"));
        mark.Items.Add(Act(L.T("Tout démarquer"), () => tab.MarkAll(false), "Ctrl+Shift+A"));
        mark.Items.Add(Act(L.T("Inverser le marquage"), tab.InvertMarks, "Multiply"));
        mark.Items.Add(new Separator());
        mark.Items.Add(Act(L.T("Marquer selon un masque…"), () => _ = vm.MarkByMaskAsync(true), "Add"));
        mark.Items.Add(Act(L.T("Démarquer selon un masque…"), () => _ = vm.MarkByMaskAsync(false), "Subtract"));
        mark.Items.Add(Act(L.T("Marquer la même extension"), tab.MarkSameExtension, "Alt+Add"));
        mark.Items.Add(new Separator());
        mark.Items.Add(Act(L.T("Copier les noms"), () => _ = copyToClipboard(true), "Ctrl+Alt+C"));
        mark.Items.Add(Act(L.T("Copier les chemins complets"), () => _ = copyToClipboard(false), "Ctrl+Shift+C"));
        items.Add(mark);

        var commands = Sub(L.T("Commandes"));
        Add(commands.Items, Cmd(vm, "Search", L.T("Rechercher des fichiers…")));
        commands.Items.Add(new Separator());
        Add(commands.Items, Cmd(vm, "CompareFolders"), Cmd(vm, "SyncFolders", L.T("Synchroniser les dossiers…")), Cmd(vm, "CompareFiles", L.T("Comparer le contenu de deux fichiers…")));
        commands.Items.Add(new Separator());
        Add(commands.Items, Cmd(vm, "CreateChecksums", L.T("Créer des sommes de contrôle…")), Cmd(vm, "VerifyChecksums"));
        commands.Items.Add(new Separator());
        Add(commands.Items, vm.IsTerminalEnabled ? Cmd(vm, "ToggleTerminal") : null, vm.IsTerminalEnabled ? Cmd(vm, "NewTerminal") : null, Cmd(vm, "OpenTerminal"), Cmd(vm, "SameFolderInOther"), Cmd(vm, "OpenInOther"), Cmd(vm, "SwapPanels"));
        items.Add(commands);

        var view = Sub(L.T("Affichage"));
        var sort = Sub(L.T("Trier par"));
        Add(sort.Items, Cmd(vm, "SortByName", L.T("Nom")), Cmd(vm, "SortByExtension", "Extension"), Cmd(vm, "SortByDate", "Date"), Cmd(vm, "SortBySize", L.T("Taille")));
        view.Items.Add(sort);
        Add(view.Items, Cmd(vm, "ToggleHidden"), Cmd(vm, "ToggleQuickView"), Cmd(vm, "ToggleTree"), Cmd(vm, "ToggleThumbnails"), Cmd(vm, "ToggleFlatView"));
        view.Items.Add(new Separator());
        var columns = Sub(L.T("Colonnes"));
        var a = vm.Appearance;
        void Col(string label, bool on, string key) =>
            columns.Items.Add(new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on, Command = new Relay(() => vm.SetColumnVisible(key, !on)) });
        Col("Extension", a.ShowExtensionColumn, "Extension");
        Col(L.T("Taille"), a.ShowSizeColumn, "Size");
        Col("Date", a.ShowDateColumn, "Date");
        Col(L.T("Droits (755)"), a.ShowModeColumn, "Mode");
        Col(L.T("Propriétaire"), a.ShowOwnerColumn, "Owner");
        Col(L.T("Groupe"), a.ShowGroupColumn, "Group");
        Col(L.T("Durée"), a.ShowDurationColumn, "MediaDuration");
        Col(L.T("Résolution"), a.ShowResolutionColumn, "MediaResolution");
        Col(L.T("Codec vidéo"), a.ShowVideoCodecColumn, "MediaVideoCodec");
        Col(L.T("Langues audio"), a.ShowAudioLanguagesColumn, "MediaAudioLanguages");
        columns.Items.Add(new Separator());
        Col(L.T("Taille des dossiers (automatique)"), a.ShowFolderSizes, "FolderSizes");
        if (TwinPanel.App.Services.ShellIcons.IsSupported) Col(TwinPanel.App.Services.ShellIcons.OptionName, a.UseShellIcons, "ShellIcons");
        columns.Items.Add(new MenuItem { Header = L.T("Ordre des colonnes par défaut"), IsEnabled = a.ColumnOrder.Count > 0, Command = new Relay(() => vm.SetColumnOrder([])) });
        view.Items.Add(columns);
        view.Items.Add(new Separator());
        Add(view.Items, Cmd(vm, "RefreshAll"), Cmd(vm, "CycleTheme"));
        view.Items.Add(new Separator());
        Add(view.Items, Cmd(vm, "GoBack"), Cmd(vm, "GoForward"), Cmd(vm, "GoUp"), Cmd(vm, "GoRoot"), Cmd(vm, "GoHome"));
        var recent = Sub(L.T("Dossiers récents"));
        FillRecent(recent.Items, vm);
        view.Items.Add(recent);
        items.Add(view);

        var tabs = Sub(L.T("Onglets"));
        Add(tabs.Items, Cmd(vm, "NewTab"), Cmd(vm, "CloseTab"));
        tabs.Items.Add(new Separator());
        AddTabSets(tabs.Items, vm);
        items.Add(tabs);

        var connections = Sub(L.T("Connexions"));
        Add(connections.Items, Cmd(vm, "OpenConnectionManager", L.T("Gestionnaire de connexions…")), Cmd(vm, "QuickConnect", L.T("Connexion rapide…")), Cmd(vm, "ShowPendingTransfers", L.T("Transferts non terminés…")), Cmd(vm, "Disconnect"));
        items.Add(connections);

        var tools = Sub(L.T("Outils"));
        Add(tools.Items, Cmd(vm, "OpenOptions", "Options…"), Cmd(vm, "ToolSettings", L.T("Éditeur et visionneuse…")));
        tools.Items.Add(new Separator());
        Add(tools.Items, Cmd(vm, "OpenOptions", L.T("Ma barre de boutons…"), "ButtonBar"), Cmd(vm, "OpenOptions", L.T("Mon menu Démarrer…"), "StartMenu"),
            Cmd(vm, "OpenOptions", L.T("Modifier les raccourcis clavier…"), "Shortcuts"));
        Add(tools.Items, Cmd(vm, "AddFavorite"));
        items.Add(tools);

        var help = Sub(L.T("Aide"));
        Add(help.Items, Cmd(vm, "ShowHelp", L.T("Guide d'utilisation…")), Cmd(vm, "ShowShortcutsHelp", L.T("Raccourcis clavier…")), Cmd(vm, "ShowAbout"));
        items.Add(help);
    }

    /// <summary>Menu du clic droit sur un élément (ou sur le vide de la liste).</summary>
    public static void BuildItem(ItemCollection items, MainWindowViewModel vm, FileItemViewModel? cur, Func<bool, Task> copyToClipboard)
    {
        items.Clear();
        bool file = cur is { IsDirectory: false };
        bool any = cur is { IsParentLink: false };
        bool isArchive = file && TwinPanel.Core.Archives.ArchivePath.IsArchiveFile(cur!.Name);

        if (any)
        {
            items.Add(Act(cur!.IsDirectory ? L.T("Ouvrir") : L.T("Ouvrir / lancer"), () => _ = vm.ActivePanel.Current.OpenCurrentAsync(), "Enter"));
            Add(items, Cmd(vm, "OpenWithDefaultApp", L.T("Ouvrir avec l'application associée")), Cmd(vm, "OpenInOther", L.T("Ouvrir dans l'autre panneau")));
            items.Add(new Separator());
        }
        if (file)
        {
            Add(items, Cmd(vm, "View", L.T("Voir")), Cmd(vm, "Edit", L.T("Éditer")), Cmd(vm, "EditInternal"));
            items.Add(new Separator());
        }
        Add(items, Cmd(vm, "PackArchive", L.T("Compresser…")));
        if (isArchive) Add(items, Cmd(vm, "UnpackArchive", L.T("Décompresser…")), Cmd(vm, "TestArchive"));
        if (file) Add(items, Cmd(vm, "Print", L.T("Imprimer")), Cmd(vm, "Share", L.T("Partager…")));
        items.Add(new Separator());
        Add(items, Cmd(vm, "Copy", L.T("Copier…")), Cmd(vm, "Move", L.T("Déplacer…")));
        if (any) Add(items, Cmd(vm, "Rename", L.T("Renommer…")));
        Add(items, Cmd(vm, "MultiRenameFiles", L.T("Renommage multiple…")), Cmd(vm, "Delete", L.T("Supprimer…")));
        items.Add(new Separator());
        Add(items, Cmd(vm, "MakeDirectory", L.T("Nouveau dossier…")), Cmd(vm, "NewFile", L.T("Nouveau fichier texte…")));
        var more = Sub(L.T("Plus"));
        more.Items.Add(Act(L.T("Copier le nom"), () => _ = copyToClipboard(true), "Ctrl+Alt+C"));
        more.Items.Add(Act(L.T("Copier le chemin complet"), () => _ = copyToClipboard(false), "Ctrl+Shift+C"));
        more.Items.Add(new Separator());
        Add(more.Items, Cmd(vm, "CompareFiles", L.T("Comparer le contenu…")), Cmd(vm, "CreateChecksums", L.T("Créer des sommes de contrôle…")));
        if (file && (cur!.Name.EndsWith(".md5", StringComparison.OrdinalIgnoreCase) || cur.Name.EndsWith(".sha1", StringComparison.OrdinalIgnoreCase) || cur.Name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)))
            Add(more.Items, Cmd(vm, "VerifyChecksums"));
        more.Items.Add(new Separator());
        Add(more.Items, vm.IsTerminalEnabled ? Cmd(vm, "NewTerminal") : null, Cmd(vm, "OpenTerminal"), Cmd(vm, "AddFavorite"));
        items.Add(more);
        items.Add(new Separator());
        Add(items, Cmd(vm, "ShowProperties", L.T("Propriétés")));
    }

    /// <summary>Commande minimale pour une case à cocher de menu.</summary>
    private sealed class Relay(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
