// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // « Quitter » : même chemin que la croix ou Alt+F4 (confirmation si des copies sont en cours).
        QuitButton.Click += (_, _) => Close();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        CommandBox.AddHandler(KeyDownEvent, OnCommandBoxKeyDown, RoutingStrategies.Tunnel);
        FavoritesFlyout.Opening += (_, _) => BuildFavoritesMenu();
        ((MenuFlyout)ConnectionsButton.Flyout!).Opening += (_, _) => BuildConnectionsMenu();
        ((MenuFlyout)TerminalButton.Flyout!).Opening += (_, _) => BuildTerminalMenu();
        WireTerminalAddButton(AddLeftButton, right: false);
        WireTerminalAddButton(AddRightButton, right: true);
        RecentFlyout.Opening += (_, _) =>
        {
            if (Vm is { } vm) AppMenus.FillRecent(RecentFlyout.Items, vm);
        };
        // La liste refermée, le clavier revient aux fichiers.
        RecentFlyout.Closed += (_, _) => FocusActiveList();
        _tabSetsFlyout.Closed += (_, _) => FocusActiveList();

        Opened += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            await vm.InitializeAsync();
            LeftPanel.FocusList();
            await vm.OfferPendingTransfersAsync();
        };
        Activated += (_, _) => Vm?.SetAppActive(true);
        Deactivated += (_, _) => Vm?.SetAppActive(false);
        Closing += async (_, e) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            // Options → Comportement : on ne quitte pas sans prévenir au milieu d'une copie.
            if (!_closeConfirmed && vm.HasActiveJobs && vm.Settings.Behavior.ConfirmExitWithJobs)
            {
                e.Cancel = true;
                var dialogs = new TwinPanel.App.Services.DialogService(this);
                var kept = vm.HasKeptTransfers ? L.T("\nLes transferts avec un serveur pourront reprendre au prochain démarrage.") : string.Empty;
                if (await dialogs.ConfirmAsync(L.T("Quitter"), L.F("Des copies ou suppressions sont en cours : elles seront interrompues.{0}\nQuitter quand même ?", kept)))
                {
                    _closeConfirmed = true;
                    Close();
                }
                else Program.RestartAfterExit = false;
                return;
            }
            RememberTerminalHeight();
            vm.SaveSettings(Width, Height, WindowState == WindowState.Maximized);
            vm.Terminal.CloseAll();
        };
        ((MenuFlyout)StartMenuButton.Flyout!).Opening += (_, _) => BuildStartMenu();
        ((MenuFlyout)MainMenuButton.Flyout!).Opening += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) AppMenus.BuildMain(((MenuFlyout)MainMenuButton.Flyout!).Items, vm, CopyPathsAsync);
        };
        DataContextChanged += (_, _) =>
        {
            if (Vm is not { } vm) return;
            vm.ShortcutsChanged += (_, _) => RebuildKeyBindings();
            vm.RestartRequested += (_, _) =>
            {
                Program.RestartAfterExit = true;
                Close();
            };
            vm.AppearanceChanged += (_, _) => ApplyAppearance();
            WireTerminal(vm);
            vm.HistoryMenuRequested += (_, _) => RecentFlyout.ShowAt(BackHost);
            vm.TabSetsMenuRequested += (_, _) =>
            {
                _tabSetsFlyout.Items.Clear();
                AppMenus.AddTabSets(_tabSetsFlyout.Items, vm);
                _tabSetsFlyout.ShowAt(MainMenuButton);
            };
            RebuildKeyBindings();
            ApplyAppearance();
        };
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private MenuFlyout FavoritesFlyout => (MenuFlyout)FavoritesButton.Flyout!;
    private MenuFlyout RecentFlyout => (MenuFlyout)BackHost.ContextFlyout!;
    private readonly MenuFlyout _tabSetsFlyout = new() { Placement = PlacementMode.BottomEdgeAlignedLeft };

    /// <summary>Une liste refermée : le clavier revient aux fichiers.</summary>
    private void FocusActiveList()
    {
        if (Vm is { } vm) (vm.ActivePanel == vm.Left ? LeftPanel : RightPanel).FocusList();
    }

    /// <summary>Raccourcis en vigueur de la liste des dossiers récents.</summary>
    private List<KeyGesture> _historyGestures = [];

    /// <summary>Raccourcis en vigueur pour afficher / masquer le terminal : ils marchent aussi depuis le terminal.</summary>
    private List<KeyGesture> _terminalGestures = [];

    private void WireTerminal(MainWindowViewModel vm)
    {
        foreach (var screen in new[] { TerminalScreen, TerminalScreen2 })
        {
            bool right = screen == TerminalScreen2;
            screen.PassThrough = e => _terminalGestures.Any(g => g.Matches(e));
            screen.ConfirmPaste = text => new TwinPanel.App.Services.DialogService(this).ConfirmAsync(L.T("Coller dans le terminal"),
                L.F("Ce texte fait {0} lignes : le programme peut exécuter chacune dès le collage.\nColler quand même ?", text.Split('\n').Length));
            screen.FocusChanged += focused =>
            {
                vm.Terminal.HasFocus = TerminalScreen.IsFocused || TerminalScreen2.IsFocused;
                if (focused) vm.Terminal.ActivatePane(right);
            };
            screen.GridChanged += (columns, rows) =>
            {
                vm.Terminal.Columns = columns;
                vm.Terminal.Rows = rows;
            };
        }
        vm.Terminal.FocusFilesRequested += FocusActiveList;
        vm.Terminal.FocusTerminalRequested += () => Dispatcher.UIThread.Post(() =>
        {
            var screen = vm.Terminal.ActiveIsRight ? TerminalScreen2 : TerminalScreen;
            screen.ScrollToEnd();
            screen.Focus();
        }, DispatcherPriority.Loaded);
        vm.Terminal.CopyRequested += text => _ = Clipboard?.SetTextAsync(text);
        vm.Terminal.PropertyChanging += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalPanelViewModel.IsOpen) && vm.Terminal.IsOpen) RememberTerminalHeight();
        };
        vm.Terminal.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalPanelViewModel.IsOpen)) LayOutTerminal(vm);
            // La largeur du second terminal n'existe qu'en côte à côte, sinon il garderait sa moitié.
            if (e.PropertyName == nameof(TerminalPanelViewModel.IsSplit))
            {
                TerminalPanes.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                TerminalPanes.ColumnDefinitions[2].Width = vm.Terminal.IsSplit ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
                PlaceTerminalStrips(vm);
            }
        };
        vm.TerminalCommands.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalCommandsViewModel.IsOpen)) PlaceTerminalStrips(vm);
        };
        PlaceTerminalStrips(vm);
    }

    /// <summary>Les boutons du panneau sont posés en haut à droite : la dernière barre d'onglets leur laisse la place.</summary>
    private void PlaceTerminalStrips(MainWindowViewModel vm)
    {
        // Le panneau des commandes, quand il est ouvert, a déjà de la place au-dessus de lui.
        var last = vm.TerminalCommands.IsOpen ? new Thickness(4, 2) : new Thickness(4, 2, TerminalButtonsWidth, 2);
        LeftStrip.Margin = vm.Terminal.IsSplit ? new Thickness(4, 2) : last;
        RightStrip.Margin = last;
    }

    private const double TerminalButtonsWidth = 225;

    /// <summary>La hauteur du panneau est celle que l'utilisateur a choisie en tirant la séparation.</summary>
    private void RememberTerminalHeight()
    {
        if (Vm is { Terminal.IsOpen: true } vm) vm.Settings.Terminal.IntegratedHeight = WorkArea.RowDefinitions[2].ActualHeight;
    }

    private void LayOutTerminal(MainWindowViewModel vm)
    {
        var row = WorkArea.RowDefinitions[2];
        if (vm.Terminal.IsOpen)
        {
            row.MinHeight = MinTerminalHeight;
            row.Height = new GridLength(Math.Clamp(vm.Settings.Terminal.IntegratedHeight, MinTerminalHeight, Math.Max(MinTerminalHeight, WorkArea.Bounds.Height - MinFilesHeight)));
            WorkArea.RowDefinitions[0].MinHeight = MinFilesHeight;
        }
        else
        {
            row.MinHeight = 0;
            row.Height = GridLength.Auto;
            WorkArea.RowDefinitions[0].MinHeight = 0;
        }
    }

    private const double MinTerminalHeight = 110;
    private const double MinFilesHeight = 120;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        // Dans le terminal, ces touches sont celles du programme qui y tourne ; dans le panneau des commandes, celles du texte.
        if (e.Source is TerminalView || CommandsPane.IsKeyboardFocusWithin) return;

        // Ici et pas dans les raccourcis de la fenêtre : la liste garde les touches fléchées (Alt+Bas n'y arriverait pas).
        if (_historyGestures.Any(g => g.Matches(e)))
        {
            vm.ShowHistoryCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            if (e.KeyModifiers == KeyModifiers.None)
            {
                // Tab : passe à l'autre panneau.
                (vm.ActivePanel == vm.Left ? RightPanel : LeftPanel).FocusList();
                e.Handled = true;
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+Tab / Ctrl+Maj+Tab : onglet suivant / précédent.
                vm.ActivePanel.NextTab(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
        {
            FavoritesButton.Flyout?.ShowAt(FavoritesButton);
            e.Handled = true;
        }
        else if (e.Key == Key.L && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.ActivePanel.BeginEditPathCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void OnCommandBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Vm is not { } vm) return;
        e.Handled = true;
        await vm.RunCommandLineAsync(keepOpen: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    /// <summary>Le menu des favoris est reconstruit à chaque ouverture (liste modifiable).</summary>
    private void BuildFavoritesMenu()
    {
        if (Vm is not { } vm) return;
        var items = FavoritesFlyout.Items;
        items.Clear();

        items.Add(new MenuItem { Header = L.T("Ajouter le dossier actuel"), Command = vm.AddFavoriteCommand });

        if (vm.Favorites.Count == 0)
        {
            items.Add(new MenuItem { Header = L.T("(aucun favori)"), IsEnabled = false });
            return;
        }

        items.Add(new Separator());
        foreach (var fav in vm.Favorites)
        {
            var item = new MenuItem
            {
                Header = fav.Name,
                Command = vm.OpenFavoriteCommand,
                CommandParameter = fav,
            };
            ToolTip.SetTip(item, fav.Path);
            items.Add(item);
        }

        items.Add(new Separator());
        var remove = new MenuItem { Header = L.T("Retirer un favori") };
        foreach (var fav in vm.Favorites)
            remove.Items.Add(new MenuItem { Header = fav.Name, Command = vm.RemoveFavoriteCommand, CommandParameter = fav });
        items.Add(remove);
    }

    /// <summary>Bouton Terminal : reconstruit à chaque ouverture (serveurs et programmes installés peuvent changer).</summary>
    private void BuildTerminalMenu()
    {
        if (Vm is not { } vm) return;
        var items = ((MenuFlyout)TerminalButton.Flyout!).Items;
        items.Clear();
        items.Add(TerminalItem(vm, "ToggleTerminal", L.T("Afficher / masquer le terminal"), vm.ToggleTerminalPanelCommand));
        items.Add(TerminalItem(vm, "NewTerminal", L.T("Nouveau terminal ici"), vm.NewTerminalCommand));
        var duplicate = TerminalItem(vm, "DuplicateTerminal", L.T("Dupliquer ce terminal"), vm.DuplicateTerminalCommand);
        duplicate.IsEnabled = vm.Terminal.Selected?.Reopen is not null;
        items.Add(duplicate);
        var split = TerminalItem(vm, "ToggleTerminalSplit", L.T("Deux terminaux côte à côte"), vm.ToggleTerminalSplitCommand);
        split.ToggleType = MenuItemToggleType.CheckBox;
        split.IsChecked = vm.Terminal.IsSplit;
        items.Add(split);
        items.Add(new Separator());
        AddTerminalPlaces(items, vm);
    }

    /// <summary>Bouton + d'un côté : ouvre ce qu'on y choisit, de ce côté.</summary>
    private void BuildTerminalAddMenu(Button button, bool right)
    {
        if (Vm is not { } vm) return;
        vm.Terminal.ActivatePane(right);
        var items = ((MenuFlyout)button.Flyout!).Items;
        items.Clear();
        items.Add(TerminalItem(vm, "NewTerminal", L.T("Nouveau terminal ici"), vm.NewTerminalCommand));
        items.Add(new Separator());
        AddTerminalPlaces(items, vm);

        // Le menu qui se ferme rend le clavier à l'autre côté : chaque choix redit le sien.
        foreach (var item in items.OfType<MenuItem>().Where(i => i.Command is not null))
        {
            var command = item.Command!;
            var parameter = item.CommandParameter;
            item.CommandParameter = null;
            item.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
            {
                vm.Terminal.ActivatePane(right);
                command.Execute(parameter);
            });
        }
    }

    private void WireTerminalAddButton(Button button, bool right)
    {
        var flyout = (MenuFlyout)button.Flyout!;
        flyout.Opening += (_, _) => BuildTerminalAddMenu(button, right);
        flyout.Closed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (Vm is not { } vm || !vm.Terminal.IsOpen) return;
            if (right && !vm.Terminal.IsSplit) return;
            vm.Terminal.ActivatePane(right);
            vm.Terminal.RequestFocus();
        }, DispatcherPriority.Background);
    }

    private static void AddTerminalPlaces(ItemCollection items, MainWindowViewModel vm)
    {
        items.Add(new MenuItem { Header = L.T("Sur ce PC"), IsEnabled = false });
        foreach (var shell in OptionsViewModel.IntegratedShells)
        {
            if (shell.Value == TwinPanel.Core.Settings.TerminalShell.Custom && string.IsNullOrWhiteSpace(vm.Settings.Terminal.IntegratedShellPath)) continue;
            items.Add(new MenuItem { Header = shell.Label, Command = vm.NewShellTerminalCommand, CommandParameter = shell.Value });
        }

        items.Add(new Separator());
        items.Add(new MenuItem { Header = L.T("Sur un serveur"), IsEnabled = false });
        var servers = vm.TerminalServers;
        if (servers.Count == 0) items.Add(new MenuItem { Header = L.T("(aucun serveur SFTP / SCP enregistré)"), IsEnabled = false });
        foreach (var profile in servers)
        {
            var header = vm.IsServerConnected(profile) ? L.F("{0}  ● connecté", profile.Name) : profile.Name;
            items.Add(new MenuItem { Header = header, Command = vm.NewServerTerminalCommand, CommandParameter = profile });
        }
    }

    private static MenuItem TerminalItem(MainWindowViewModel vm, string id, string header, System.Windows.Input.ICommand command)
    {
        var item = new MenuItem { Header = header, Command = command };
        if (TwinPanel.App.Services.CommandCatalog.Find(id) is { } info && AppMenus.Gesture(TwinPanel.App.Services.CommandCatalog.GesturesFor(info, vm.Settings.Shortcuts)) is { } gesture)
            item.InputGesture = gesture;
        return item;
    }

    /// <summary>Menu des connexions : gestionnaire, connexion rapide, connexions ouvertes, connexions enregistrées.</summary>
    private void BuildConnectionsMenu()
    {
        if (Vm is not { } vm) return;
        var items = ((MenuFlyout)ConnectionsButton.Flyout!).Items;
        items.Clear();
        items.Add(new MenuItem { Header = L.T("Gestionnaire de connexions…"), InputGesture = new KeyGesture(Key.F, KeyModifiers.Control), Command = vm.OpenConnectionManagerCommand });
        items.Add(new MenuItem { Header = L.T("Connexion rapide…"), InputGesture = new KeyGesture(Key.N, KeyModifiers.Control), Command = vm.QuickConnectCommand });

        var open = vm.RemoteSessions?.Sessions ?? [];
        if (open.Count > 0)
        {
            items.Add(new Separator());
            items.Add(new MenuItem { Header = L.T("Connexions ouvertes"), IsEnabled = false });
            foreach (var s in open)
            {
                var item = new MenuItem { Header = $"{s.Profile.Name}  ({s.Scheme}://{s.Alias})" };
                item.Items.Add(new MenuItem { Header = L.T("Afficher dans le panneau actif"), Command = vm.ShowSessionCommand, CommandParameter = s });
                item.Items.Add(new MenuItem { Header = L.T("Déconnecter"), Command = vm.DisconnectCommand, CommandParameter = s });
                items.Add(item);
            }
            items.Add(new MenuItem { Header = L.T("Déconnecter le panneau actif"), InputGesture = new KeyGesture(Key.F, KeyModifiers.Control | KeyModifiers.Shift), Command = vm.DisconnectCommand });
            items.Add(new MenuItem { Header = L.T("Propriétés / droits…"), Command = vm.ShowPropertiesCommand, InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Enter, Avalonia.Input.KeyModifiers.Alt) });
        }

        var saved = vm.Connections?.Profiles ?? [];
        if (saved.Count > 0)
        {
            items.Add(new Separator());
            // Les plus récemment utilisées d'abord (15 au maximum ; toutes sont dans le gestionnaire).
            foreach (var p in saved.OrderByDescending(p => p.LastUsedUtc ?? DateTime.MinValue).ThenBy(p => p.Name).Take(15))
            {
                var item = new MenuItem { Header = $"{p.Name}  —  {p.Summary}", Command = vm.ConnectProfileCommand, CommandParameter = p };
                ToolTip.SetTip(item, TwinPanel.Core.Remote.RemoteProtocols.Label(p.Protocol) + (p.Group.Length > 0 ? $" — {p.Group}" : string.Empty));
                items.Add(item);
            }
        }
    }

    private bool _closeConfirmed;

    /// <summary>Raccourcis clavier : ceux du catalogue, ou ceux choisis dans Options → Raccourcis.</summary>
    private void RebuildKeyBindings()
    {
        if (Vm is not { } vm) return;
        HangWatchdog.Step("Raccourcis : reconstruction");
        KeyBindings.Clear();
        _historyGestures = [];
        _terminalGestures = [];
        foreach (var info in TwinPanel.App.Services.CommandCatalog.All)
        {
            var command = TwinPanel.App.Services.CommandCatalog.Resolve(vm, info);
            if (command is null) continue;
            if (info.Id == "ShowHistory")
            {
                foreach (var part in TwinPanel.App.Services.CommandCatalog.GesturesFor(info, vm.Settings.Shortcuts).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (AppMenus.Gesture(part) is { } gesture) _historyGestures.Add(gesture);
                continue;
            }
            foreach (var text in TwinPanel.App.Services.CommandCatalog.GesturesFor(info, vm.Settings.Shortcuts)
                         .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var gesture = KeyGesture.Parse(text);
                    KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command, CommandParameter = info.Parameter! });
                    if (info.Id is "ToggleTerminal" or "ToggleTerminalSplit") _terminalGestures.Add(gesture);
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
                {
                    vm.StatusMessage = $"Raccourci ignoré (illisible) pour « {info.Label} » : {text}";
                }
            }
        }
    }

    /// <summary>Options → Affichage : police, taille, lignes et colonnes.</summary>
    private void ApplyAppearance()
    {
        if (Vm is not { } vm) return;
        var a = vm.Settings.Appearance;
        var size = Math.Clamp(a.FontSize, 9, 28);
        HangWatchdog.Step($"Apparence : police « {a.FontFamily} » {size}");
        // Pour TOUTES les fenêtres (Options, connexions, questions…) :
        // - AppFontSize / AppFontFamily : texte des fenêtres (style « :is(Window) » dans App.axaml) ;
        // - ControlContentThemeFontSize : boutons, listes, onglets, cases du thème Fluent ;
        // - PanelFontSize : cellules des listes de fichiers.
        if (Avalonia.Application.Current is { } app)
        {
            app.Resources["AppFontSize"] = size;
            app.Resources["ControlContentThemeFontSize"] = size;
            app.Resources["PanelFontSize"] = MainWindowViewModel.PanelFontSize(a);
            // Titres des onglets (rubriques des Options, onglets du gestionnaire de connexions…), un peu plus grands que le texte.
            app.Resources["TabItemHeaderFontSize"] = size + 2;
            app.Resources["AppFontFamily"] = string.IsNullOrWhiteSpace(a.FontFamily)
                ? new Avalonia.Media.FontFamily("fonts:Inter#Inter")
                : new Avalonia.Media.FontFamily(a.FontFamily);
        }
        LeftPanel.ApplyAppearance(a);
        RightPanel.ApplyAppearance(a);
        HangWatchdog.Step("Apparence : appliquée");
    }

    /// <summary>Menu Démarrer personnel, reconstruit à chaque ouverture (les sous-menus viennent du champ « Sous-menu »).</summary>
    /// <summary>Noms (ou chemins complets) des éléments marqués dans le presse-papiers.</summary>
    private async Task CopyPathsAsync(bool namesOnly)
    {
        if (DataContext is MainWindowViewModel vm && Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.GetPathsForClipboard(namesOnly));
    }

    private void BuildStartMenu()
    {
        if (Vm is not { } vm) return;
        var items = ((MenuFlyout)StartMenuButton.Flyout!).Items;
        items.Clear();
        var groups = new Dictionary<string, MenuItem>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var c in vm.StartMenu)
        {
            var target = items;
            if (c.Group.Length > 0)
            {
                if (!groups.TryGetValue(c.Group, out var parent))
                {
                    parent = new MenuItem { Header = c.Group };
                    groups[c.Group] = parent;
                    items.Add(parent);
                }
                target = parent.Items;
            }
            if (c.Kind == TwinPanel.Core.Settings.CustomCommandKind.Separator) { target.Add(new Separator()); continue; }
            var item = new MenuItem
            {
                Header = c.Label.Length > 0 ? c.Label : c.Target,
                Command = vm.RunCustomCommandCommand,
                CommandParameter = c,
                Icon = CustomIcons.For(c) is { } g ? new PathIcon { Data = g, Width = 14, Height = 14 } : null,
            };
            if (c.Tooltip.Length > 0) ToolTip.SetTip(item, c.Tooltip);
            target.Add(item);
        }
        if (vm.StartMenu.Count == 0) items.Add(new MenuItem { Header = L.T("(menu vide)"), IsEnabled = false });
        items.Add(new Separator());
        items.Add(new MenuItem { Header = L.T("Modifier ce menu…"), Command = vm.OpenOptionsCommand, CommandParameter = "StartMenu" });
    }
}
