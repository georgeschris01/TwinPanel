// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TwinPanel.Core;
using TwinPanel.App.ViewModels;
using TwinPanel.Core.Navigation;

namespace TwinPanel.App.Views;

public partial class PanelView : UserControl
{
    private static readonly TimeSpan QuickSearchTimeout = TimeSpan.FromSeconds(2);

    private FilePanelViewModel? _vm;
    private FolderTabViewModel? _tab;
    private DateTime _lastQuickKey;
    private bool _suppressTextInput;

    public PanelView()
    {
        InitializeComponent();

        // Tunnel : on intercepte les touches et les clics avant que la DataGrid ne les traite.
        foreach (var list in new Control[] { FileGrid, ThumbList })
        {
            list.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Tunnel);
            list.AddHandler(TextInputEvent, OnGridTextInput, RoutingStrategies.Tunnel);
            list.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);
            list.AddHandler(PointerReleasedEvent, OnGridPointerReleased, RoutingStrategies.Tunnel);
            list.AddHandler(PointerMovedEvent, OnGridPointerMoved, RoutingStrategies.Tunnel);
            list.AddHandler(PointerCaptureLostEvent, (_, _) => EndRightDrag(), RoutingStrategies.Bubble);
            list.DoubleTapped += OnGridDoubleTapped;
            // Ctrl + molette : texte des panneaux, ou taille des miniatures.
            list.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
        }
        ThumbList.AttachScrollBar(ThumbBar);
        TabList.AddHandler(PointerReleasedEvent, OnTabPointerReleased, RoutingStrategies.Tunnel);

        // Glisser-déposer : départ depuis la liste, arrivée n'importe où dans le panneau (liste, dossier, onglet, lecteur).
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => ShowDropRow(null));
        AddHandler(DragDrop.DropEvent, OnDrop);
        FileGrid.Sorting += OnSorting;
        FileGrid.ColumnReordered += OnColumnReordered;
        // Boutons latéraux de la souris : dossier précédent / suivant (dans le panneau sous la souris).
        AddHandler(PointerPressedEvent, OnSideButtons, RoutingStrategies.Tunnel);

        // Onglets : glisser pour les ranger (ou les envoyer dans l'autre panneau), flèches ‹ ›, molette.
        TabList.AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Tunnel);
        TabList.AddHandler(PointerMovedEvent, OnTabPointerMoved, RoutingStrategies.Tunnel);
        TabScroller.AddHandler(PointerWheelChangedEvent, OnTabWheel, RoutingStrategies.Tunnel);
        // Lecteurs réseau et connexions : glisser pour les ranger.
        DriveBarList.AddHandler(PointerPressedEvent, OnDrivePointerPressed, RoutingStrategies.Tunnel);
        DriveBarList.AddHandler(PointerMovedEvent, OnDrivePointerMoved, RoutingStrategies.Tunnel);
        TabScroller.LayoutUpdated += (_, _) => UpdateTabArrows();
        TabScroller.ScrollChanged += (_, _) => UpdateTabArrows();
        TabLeftButton.Click += (_, _) => ScrollTabs(-1);
        TabRightButton.Click += (_, _) => ScrollTabs(1);
        PathBox.LostFocus += (_, _) => Dispatcher.UIThread.Post(ClosePathBoxIfLeft);
        FileGrid.LayoutUpdated += (_, _) => FitNameColumn();

        AddHandler(GotFocusEvent, (_, _) => _vm?.Activate(), RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, (_, _) =>
        {
            _wasActive = _vm?.IsActive == true;
            _vm?.Activate();
        }, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => AttachPanel(DataContext as FilePanelViewModel);
    }

    private MainWindowViewModel? Main => TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    /// <summary>Options → Affichage : hauteur des lignes et colonnes visibles.</summary>
    public void ApplyAppearance(TwinPanel.Core.Settings.AppearanceSettings a)
    {
        // Texte agrandi (Ctrl + molette) : les lignes grandissent avec lui.
        var text = MainWindowViewModel.PanelFontSize(a);
        FileGrid.RowHeight = Math.Clamp(Math.Max(a.RowHeight, Math.Ceiling(text * 1.75)), 18, 72);
        // Repérées par leur clé de tri : le titre change quand on trie (« Taille ▲ »).
        foreach (var column in FileGrid.Columns)
        {
            switch (column.SortMemberPath)
            {
                case "Folder": column.IsVisible = _tab?.IsFlat == true; break;
                case "Extension": column.IsVisible = a.ShowExtensionColumn; break;
                case "Size": column.IsVisible = a.ShowSizeColumn; break;
                case "Date": column.IsVisible = a.ShowDateColumn; break;
                case "Mode": column.IsVisible = a.ShowModeColumn; break;
                case "Owner": column.IsVisible = a.ShowOwnerColumn; break;
                case "Group": column.IsVisible = a.ShowGroupColumn; break;
                case "MediaDuration": column.IsVisible = a.ShowDurationColumn; break;
                case "MediaResolution": column.IsVisible = a.ShowResolutionColumn; break;
                case "MediaVideoCodec": column.IsVisible = a.ShowVideoCodecColumn; break;
                case "MediaAudioLanguages": column.IsVisible = a.ShowAudioLanguagesColumn; break;
            }
        }
        ThumbList.Configure(a.ThumbnailSize, text, a.ThumbnailsOnServers ? Math.Clamp(a.ThumbnailMaxServerMb, 1, 200) * 1024L * 1024 : -1);
        ApplyColumnOrder(a.ColumnOrder);
        ApplyTabRows(a.TabRows);
    }

    // ---- Ordre des colonnes (titres glissés à la souris) ------------------------------------

    private static readonly string[] DefaultColumnOrder =
        ["Name", "Folder", "Extension", "Size", "Date", "Mode", "Owner", "Group", "MediaDuration", "MediaResolution", "MediaVideoCodec", "MediaAudioLanguages"];
    private bool _applyingOrder;

    private void ApplyColumnOrder(IReadOnlyList<string> saved)
    {
        var order = saved.Where(k => DefaultColumnOrder.Contains(k)).Distinct().ToList();
        foreach (var k in DefaultColumnOrder) if (!order.Contains(k)) order.Add(k);
        _applyingOrder = true;
        try
        {
            // Colonne 0 : l'icône, toujours en premier.
            if (FileGrid.Columns.FirstOrDefault(c => string.IsNullOrEmpty(c.SortMemberPath)) is { } icon && icon.DisplayIndex != 0) icon.DisplayIndex = 0;
            int index = 1;
            foreach (var key in order)
            {
                if (FileGrid.Columns.FirstOrDefault(c => c.SortMemberPath == key) is not { } col) continue;
                if (col.DisplayIndex != index) col.DisplayIndex = index;
                index++;
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException) { /* grille pas encore prête : l'ordre sera remis au prochain affichage */ }
        finally { _applyingOrder = false; }
    }

    // ---- Largeur de la colonne Nom ------------------------------------------------------------
    // Une colonne « * » bloque les autres : la grille ne laisse élargir une colonne qu'autant que les colonnes
    // à sa droite peuvent rétrécir, donc plus rien quand la fenêtre est étroite. La colonne Nom a donc une
    // largeur fixe, recalculée ici : elle remplit la place libre, jamais moins que sa largeur minimale
    // (celle que l'utilisateur lui a donnée en la tirant, 150 au départ). Au-delà : barre de défilement.

    private const double NameMinWidth = 150;
    private double _nameMin = NameMinWidth;
    private double _nameSet = -1;

    private void FitNameColumn()
    {
        if (FileGrid.Columns.FirstOrDefault(c => c.SortMemberPath == "Name") is not { } name || name.ActualWidth <= 0) return;
        var presenter = FileGrid.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>().FirstOrDefault();
        double available = presenter?.Bounds.Width ?? 0;
        if (available <= 0) return;
        // Largeur changée par quelqu'un d'autre que nous : l'utilisateur a tiré la colonne Nom.
        if (_nameSet >= 0 && Math.Abs(name.ActualWidth - _nameSet) > 0.5) _nameMin = Math.Max(60, name.ActualWidth);
        double others = FileGrid.Columns.Where(c => c.IsVisible && !ReferenceEquals(c, name)).Sum(c => c.ActualWidth);
        double width = Math.Floor(Math.Max(_nameMin, available - others - 1));
        _nameSet = width;
        if (Math.Abs(name.ActualWidth - width) > 0.5) name.Width = new DataGridLength(width);
    }

    private void OnColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (_applyingOrder || Main is not { } main) return;
        var order = FileGrid.Columns.Where(c => !string.IsNullOrEmpty(c.SortMemberPath))
            .OrderBy(c => c.DisplayIndex).Select(c => c.SortMemberPath!).ToList();
        // Après la fin du glisser : l'autre panneau prend le même ordre (et l'icône revient en tête si besoin).
        Dispatcher.UIThread.Post(() => main.SetColumnOrder(order));
    }

    // ---- Souris : boutons latéraux, double-clic sur le vide, Ctrl + molette -----------------

    private void OnSideButtons(object? sender, PointerPressedEventArgs e)
    {
        if (_tab is null || _vm is null) return;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsXButton1Pressed) { _vm.Activate(); _ = _tab.GoBackAsync(); e.Handled = true; }
        else if (props.IsXButton2Pressed) { _vm.Activate(); _ = _tab.GoForwardAsync(); e.Handled = true; }
    }

    private async void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_tab is null) return;
        var source = e.Source as Visual;
        // Titres et barres de défilement : rien.
        if (source?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is not null
            || source?.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(includeSelf: true) is not null) return;
        // Double-clic dans le vide sous la liste : dossier parent.
        bool onItem = IsThumbs ? ThumbList.ItemAt(e.GetPosition(ThumbList)) is not null
            : source?.FindAncestorOfType<DataGridRow>(includeSelf: true) is not null;
        if (!onItem) { await _tab.GoUpAsync(); return; }
        await _tab.OpenCurrentAsync();
    }

    private void OnGridWheel(object? sender, PointerWheelEventArgs e)
    {
        CancelClickRename();
        FinishInlineRename(commit: true); // la case suivrait mal une ligne qui défile
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || Main is not { } main || e.Delta.Y == 0) return;
        if (IsThumbs) main.ChangeThumbnailSize(e.Delta.Y > 0 ? 1 : -1);
        else main.ChangePanelZoom(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    /// <summary>Clic milieu sur un dossier : il s'ouvre dans un nouvel onglet (à droite de l'onglet affiché, en arrière-plan).</summary>
    private bool HandleMiddleClick(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ActiveList).Properties.IsMiddleButtonPressed || _vm is null || _tab is null) return false;
        e.Handled = true;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) { if (IsThumbs) Main?.ChangeThumbnailSize(0); else Main?.ChangePanelZoom(0); return true; } // Ctrl + clic milieu : taille normale
        if (ItemUnder(e) is not { IsDirectory: true } dir) return true;
        var panel = _vm;
        var tab = _tab;
        _ = panel.AddTabAsync(dir.FullPath, tab.SortColumn, tab.SortDescending, select: false, insertAt: panel.Tabs.IndexOf(tab) + 1);
        if (Main is { } main) main.StatusMessage = L.F("« {0} » ouvert dans un nouvel onglet.", (dir.IsParentLink ? L.T("dossier parent") : dir.Name));
        return true;
    }

    // ---- Onglets : plusieurs lignes ou flèches -------------------------------------------------

    private TwinPanel.Core.Settings.TabRowMode _tabRows = TwinPanel.Core.Settings.TabRowMode.Arrows;

    private void ApplyTabRows(TwinPanel.Core.Settings.TabRowMode mode)
    {
        if (mode == _tabRows && TabList.ItemsPanel is not null && TabList.Classes.Contains("wrap") == (mode == TwinPanel.Core.Settings.TabRowMode.Wrap)) return;
        _tabRows = mode;
        bool wrap = mode == TwinPanel.Core.Settings.TabRowMode.Wrap;
        TabList.Classes.Set("wrap", wrap);
        // Plusieurs lignes : largeur limitée à celle du panneau (pas de défilement) pour que les onglets passent à la ligne.
        TabScroller.HorizontalScrollBarVisibility = wrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
        TabList.ItemsPanel = wrap
            ? new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal })
            : new Avalonia.Controls.Templates.FuncTemplate<Panel?>(() => new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal });
        UpdateTabArrows();
    }

    private void UpdateTabArrows()
    {
        bool overflow = _tabRows == TwinPanel.Core.Settings.TabRowMode.Arrows && TabScroller.Extent.Width > TabScroller.Viewport.Width + 1;
        if (TabLeftButton.IsVisible != overflow) TabLeftButton.IsVisible = overflow;
        if (TabRightButton.IsVisible != overflow) TabRightButton.IsVisible = overflow;
        if (!overflow) return;
        bool canLeft = TabScroller.Offset.X > 0.5;
        bool canRight = TabScroller.Offset.X < TabScroller.Extent.Width - TabScroller.Viewport.Width - 0.5;
        if (TabLeftButton.IsEnabled != canLeft) TabLeftButton.IsEnabled = canLeft;
        if (TabRightButton.IsEnabled != canRight) TabRightButton.IsEnabled = canRight;
    }

    private void ScrollTabs(int direction)
    {
        double step = Math.Max(120, TabScroller.Viewport.Width * 0.6);
        double max = Math.Max(0, TabScroller.Extent.Width - TabScroller.Viewport.Width);
        TabScroller.Offset = new Vector(Math.Clamp(TabScroller.Offset.X + direction * step, 0, max), 0);
    }

    private void OnTabWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_tabRows != TwinPanel.Core.Settings.TabRowMode.Arrows) return;
        double max = Math.Max(0, TabScroller.Extent.Width - TabScroller.Viewport.Width);
        if (max <= 0) return;
        double delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        TabScroller.Offset = new Vector(Math.Clamp(TabScroller.Offset.X - delta * 60, 0, max), 0);
        e.Handled = true;
    }

    /// <summary>L'onglet affiché reste visible dans la barre (après un changement d'onglet ou un ajout).</summary>
    private void RevealSelectedTab() => Dispatcher.UIThread.Post(() =>
    {
        if (_vm?.SelectedTab is { } t && TabList.ContainerFromItem(t) is Control c) c.BringIntoView();
    }, DispatcherPriority.Background);

    // ---- Onglets : glisser-déposer -------------------------------------------------------------

    private static readonly DataFormat<string> TabDragFormat = DataFormat.CreateStringApplicationFormat("TwinPanel.Onglet");
    private static (string Token, FolderTabViewModel Tab, FilePanelViewModel Panel)? s_tabDrag;
    private FolderTabViewModel? _tabDragCandidate;
    private PointerPressedEventArgs? _tabDragPress; // un glisser démarre depuis l'appui, même si la souris a bougé depuis
    private Point _tabDragStart;

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _tabDragCandidate = null;
        var props = e.GetCurrentPoint(TabList).Properties;
        if (!props.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return; // bouton × de l'onglet
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not FolderTabViewModel tab) return;
        _tabDragCandidate = tab;
        _tabDragPress = e;
        _tabDragStart = e.GetPosition(TabList);
    }

    private async void OnTabPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_tabDragCandidate is not { } tab || _tabDragPress is not { } press || _vm is null) return;
        if (!e.GetCurrentPoint(TabList).Properties.IsLeftButtonPressed) { _tabDragCandidate = null; return; }
        var d = e.GetPosition(TabList) - _tabDragStart;
        if (Math.Abs(d.X) + Math.Abs(d.Y) < 8) return;
        _tabDragCandidate = null;
        var token = Guid.NewGuid().ToString("N");
        s_tabDrag = (token, tab, _vm);
        try { await DragDrop.DoDragDropAsync(press, DragData(TabDragFormat, token), DragDropEffects.Move); }
        finally { s_tabDrag = null; }
    }

    private static bool IsTabDrag(DragEventArgs e) =>
        s_tabDrag is { } d && e.DataTransfer.TryGetValue(TabDragFormat) == d.Token;

    private static DataTransfer DragData(DataFormat<string> format, string token)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(format, token));
        return data;
    }

    /// <summary>Place où l'onglet serait posé : position dans la barre ; ailleurs dans l'autre panneau : à la fin ; null : nulle part.</summary>
    private int? TabDropIndex(DragEventArgs e)
    {
        if (_vm is null || s_tabDrag is not { } drag) return null;
        bool samePanel = ReferenceEquals(drag.Panel, _vm);
        var onBar = e.GetPosition(TabScroller);
        if (!new Rect(TabScroller.Bounds.Size).Contains(onBar)) return samePanel ? null : _vm.Tabs.Count;
        var p = e.GetPosition(TabList);
        int? afterInRow = null;
        for (int i = 0; i < _vm.Tabs.Count; i++)
        {
            if (TabList.ContainerFromIndex(i) is not Control c || c.TranslatePoint(new Point(0, 0), TabList) is not { } topLeft) continue;
            var r = new Rect(topLeft, c.Bounds.Size);
            if (p.Y < r.Top - 2 || p.Y > r.Bottom + 2) continue; // autre ligne
            if (p.X < r.Center.X) return i;
            afterInRow = i + 1;
        }
        return afterInRow ?? _vm.Tabs.Count;
    }

    // ---- Barre des lecteurs : ranger les lecteurs réseau et les connexions en les glissant ----------

    private static readonly DataFormat<string> DriveDragFormat = DataFormat.CreateStringApplicationFormat("TwinPanel.Lecteur");
    private static (string Token, DriveButtonViewModel Button)? s_driveDrag;
    private DriveButtonViewModel? _driveDragCandidate;
    private PointerPressedEventArgs? _driveDragPress;
    private Point _driveDragStart;

    private void OnDrivePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _driveDragCandidate = null;
        if (!e.GetCurrentPoint(DriveBarList).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext is not DriveButtonViewModel { MoveGroup: not null } button) return;
        _driveDragCandidate = button;
        _driveDragPress = e;
        _driveDragStart = e.GetPosition(DriveBarList);
    }

    private async void OnDrivePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_driveDragCandidate is not { } button || _driveDragPress is not { } press) return;
        if (!e.GetCurrentPoint(DriveBarList).Properties.IsLeftButtonPressed) { _driveDragCandidate = null; return; }
        var d = e.GetPosition(DriveBarList) - _driveDragStart;
        if (Math.Abs(d.X) + Math.Abs(d.Y) < 8) return;
        _driveDragCandidate = null;
        // Le bouton lâche la souris : pas de clic (connexion, changement de disque) à la fin du glisser.
        e.Pointer.Capture(null);
        var token = Guid.NewGuid().ToString("N");
        s_driveDrag = (token, button);
        try { await DragDrop.DoDragDropAsync(press, DragData(DriveDragFormat, token), DragDropEffects.Move); }
        finally { s_driveDrag = null; }
    }

    private static bool IsDriveDrag(DragEventArgs e) =>
        s_driveDrag is { } d && e.DataTransfer.TryGetValue(DriveDragFormat) == d.Token;

    /// <summary>Place du bouton glissé parmi ceux de son groupe (0 à nombre de boutons) ; null : pas sur ce groupe.</summary>
    private int? DriveDropIndex(DragEventArgs e)
    {
        if (_vm is null || s_driveDrag is not { } drag) return null;
        var p = e.GetPosition(DriveBarList);
        if (!new Rect(DriveBarList.Bounds.Size).Contains(p)) return null;
        int j = 0;
        int? afterInRow = null;
        Rect? first = null, last = null;
        for (int i = 0; i < _vm.DriveBar.Count; i++)
        {
            var b = _vm.DriveBar[i];
            if (b.MoveGroup != drag.Button.MoveGroup) continue;
            if (DriveBarList.ContainerFromIndex(i) is Control c && c.TranslatePoint(new Point(0, 0), DriveBarList) is { } topLeft)
            {
                var r = new Rect(topLeft, c.Bounds.Size);
                first ??= r;
                last = r;
                if (p.Y >= r.Top - 2 && p.Y <= r.Bottom + 2)
                {
                    if (p.X < r.Center.X && p.X >= r.Left - 12) return j;
                    if (p.X >= r.Center.X) afterInRow = j + 1;
                }
            }
            j++;
        }
        // Juste après le dernier bouton du groupe (sur sa ligne) : à la fin ; plus loin (autre groupe) : nulle part.
        if (afterInRow is { } after && last is { } l && (after < j || p.X <= l.Right + 12)) return after;
        return null;
    }

    private bool DropDriveButton(DragEventArgs e)
    {
        if (!IsDriveDrag(e) || s_driveDrag is not { } drag || _vm is null) return false;
        e.Handled = true;
        if (DriveDropIndex(e) is { } index) Main?.MoveDriveButton(drag.Button, index, [.. _vm.DriveBar]);
        return true;
    }

    private async Task<bool> DropTabAsync(DragEventArgs e)
    {
        if (!IsTabDrag(e) || s_tabDrag is not { } drag || _vm is null) return false;
        e.Handled = true;
        if (TabDropIndex(e) is not { } index) return true;
        var tab = drag.Tab;
        if (ReferenceEquals(drag.Panel, _vm))
        {
            int from = _vm.Tabs.IndexOf(tab);
            if (from < 0) return true;
            if (index > from) index--;
            index = Math.Clamp(index, 0, _vm.Tabs.Count - 1);
            if (index != from) _vm.Tabs.Move(from, index);
            _vm.SelectedTab = tab;
            RevealSelectedTab();
            return true;
        }
        // Vers l'autre panneau : même dossier, tri, verrou et nom ; l'onglet quitte son panneau (sauf s'il y est seul).
        var saved = tab.ToSaved();
        await _vm.AddTabAsync(saved.Path, tab.SortColumn, tab.SortDescending, select: true, saved.Lock, saved.Name, insertAt: index);
        if (drag.Panel.Tabs.Count > 1) drag.Panel.CloseTab(tab);
        _vm.Activate();
        RevealSelectedTab();
        return true;
    }

    // ---- Glisser-déposer (bouton gauche maintenu) -----------------------------------------
    // Lâcher = copier, Maj = déplacer (comme Total Commander) ; la fenêtre F5 / F6 confirme toujours.
    // Vers l'Explorateur et les autres programmes : les fichiers du PC (pas ceux d'un serveur ou d'une archive).

    private static readonly DataFormat<string> DragFormat = DataFormat.CreateStringApplicationFormat("TwinPanel.Glisser");
    private static (string Token, IReadOnlyList<TwinPanel.Core.FileEntry> Entries, FilePanelViewModel Panel, string FromDir)? s_drag;
    private FileItemViewModel? _dragCandidate;
    private PointerPressedEventArgs? _dragPress;
    private Point _dragStart;

    private async Task StartDragAsync(FileItemViewModel item, PointerPressedEventArgs press)
    {
        if (_tab is null || _vm is null) return;
        // Un élément marqué entraîne tous les marqués ; sinon seulement celui qu'on tient.
        var entries = item.IsMarked ? _tab.MarkedItems.Select(i => i.Entry).ToList() : new List<TwinPanel.Core.FileEntry> { item.Entry };
        if (entries.Count == 0) return;
        var token = Guid.NewGuid().ToString("N");
        var data = DragData(DragFormat, token);
        var (panel, fromDir) = (_vm, _tab.CurrentPath);
        if (!_tab.IsRemote && !_tab.IsInsideArchive) await AddFilesAsync(data, entries);
        s_drag = (token, entries, panel, fromDir);
        try { await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy | DragDropEffects.Move); }
        finally { s_drag = null; }
    }

    /// <summary>Pour l'Explorateur et les autres programmes.</summary>
    private async Task AddFilesAsync(DataTransfer data, IReadOnlyList<TwinPanel.Core.FileEntry> entries)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        foreach (var entry in entries)
        {
            IStorageItem? item = entry.IsDirectory
                ? await storage.TryGetFolderFromPathAsync(entry.FullPath)
                : await storage.TryGetFileFromPathAsync(entry.FullPath);
            if (item is not null) data.Add(DataTransferItem.CreateFile(item));
        }
    }

    private bool IsOurDrag(DragEventArgs e) =>
        s_drag is { } d && e.DataTransfer.TryGetValue(DragFormat) == d.Token;

    /// <summary>Dossier visé sous la souris : onglet, bouton de lecteur, dossier de la liste, sinon le dossier du panneau.</summary>
    private string? DropTarget(DragEventArgs e, bool quiet)
    {
        if (_vm is null) return null;
        var source = e.Source as Visual;
        if (source?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is FolderTabViewModel tab) return tab.CurrentPath;
        if (source?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext is DriveButtonViewModel drive)
            return quiet ? (drive.Kind == DriveButtonKind.Drive ? drive.Key : drive.IsConnected ? "?" : null) : Main?.DriveDropTarget(drive);
        if (DropFolderRowAt(e.GetPosition(ActiveList)) is { } row)
        {
            // Un dossier qu'on est en train de glisser ne peut pas se recevoir lui-même.
            if (s_drag is { } d && d.Entries.Any(x => x.FullPath == row.FullPath)) return null;
            return row.FullPath;
        }
        return _vm.Current.CurrentPath;
    }

    /// <summary>
    /// Dossier de la liste qui recevra les fichiers : seulement quand la souris est sur sa colonne Nom (ou son icône).
    /// Sur Ext, Taille, Date… d'une ligne de dossier, le dépôt se fait dans le dossier affiché, comme sur le vide.
    /// En miniatures : toute la vignette.
    /// </summary>
    private FileItemViewModel? DropFolderRowAt(Point p)
    {
        if (IsThumbs) return ThumbList.ItemAt(p) is { IsDirectory: true } thumb ? thumb : null;
        var hit = FileGrid.InputHitTest(p) as Visual;
        if (hit?.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext is not FileItemViewModel { IsDirectory: true } row) return null;
        if (hit.FindAncestorOfType<DataGridCell>(includeSelf: true) is not { } cell) return null;
        var column = DataGridColumn.GetColumnContainingElement(cell);
        return column is not null && (column.SortMemberPath == "Name" || string.IsNullOrEmpty(column.SortMemberPath)) ? row : null;
    }

    private FileItemViewModel? _dropRow;

    /// <summary>Bordure sur le dossier visé pendant le glisser (null : aucun).</summary>
    private void ShowDropRow(FileItemViewModel? row)
    {
        if (ReferenceEquals(row, _dropRow)) return;
        if (_dropRow is not null) _dropRow.IsDropTarget = false;
        _dropRow = row;
        if (row is not null) row.IsDropTarget = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (IsDriveDrag(e))
        {
            ShowDropRow(null);
            e.DragEffects = DriveDropIndex(e) is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        if (IsTabDrag(e))
        {
            ShowDropRow(null);
            e.DragEffects = TabDropIndex(e) is null ? DragDropEffects.None : DragDropEffects.Move;
            e.Handled = true;
            return;
        }
        bool ours = IsOurDrag(e);
        bool files = !ours && e.DataTransfer.Contains(DataFormat.File);
        var target = (ours || files) ? DropTarget(e, quiet: true) : null;
        var row = target is null ? null : DropFolderRowAt(e.GetPosition(ActiveList));
        ShowDropRow(row is not null && string.Equals(row.FullPath, target, PathText.Comparison) ? row : null);
        // Lâcher au point de départ ne fait simplement rien : ce n'est pas interdit, et le curseur ne doit pas le laisser croire
        // (le glissement commence toujours là, sur l'élément qu'on tient).
        bool nothingToDo = ours && (target is null || (s_drag is { } d && string.Equals(target, d.FromDir, PathText.Comparison)));
        if (nothingToDo) e.DragEffects = DragDropEffects.Move;
        else if (target is null) e.DragEffects = DragDropEffects.None;
        else e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>
    /// Survol du reste de la fenêtre (barres, séparation) pendant qu'on glisse des fichiers : lâcher n'y fait rien, sans curseur « interdit ».
    /// </summary>
    public static void AllowPassingOver(Control window)
    {
        DragDrop.SetAllowDrop(window, true);
        window.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (s_drag is not null) e.DragEffects = DragDropEffects.Move;
        });
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        ShowDropRow(null);
        if (DropDriveButton(e)) return;
        if (await DropTabAsync(e)) return;
        if (Main is not { } main || _vm is null) return;
        // Maj = déplacer, Ctrl = copier sans demander ; sinon un petit menu demande « Copier ici / Déplacer ici ».
        bool? forced = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? true : e.KeyModifiers.HasFlag(KeyModifiers.Control) ? false : null;
        if (IsOurDrag(e) && s_drag is { } d)
        {
            e.Handled = true;
            var target = DropTarget(e, quiet: false);
            if (target is null || string.Equals(target, d.FromDir, PathText.Comparison)) return;
            var (entries, panel) = (d.Entries, d.Panel);
            if (forced is { } m) await main.DropAsync(panel, entries, target, m);
            else AskCopyOrMove(entries.Count, target, move => main.DropAsync(panel, entries, target, move));
            return;
        }
        if (e.DataTransfer.TryGetFiles() is not { } dropped) return;
        e.Handled = true;
        var paths = dropped.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0) return;
        var dest = DropTarget(e, quiet: false);
        if (dest is null) return;
        var list = new List<TwinPanel.Core.FileEntry>();
        foreach (var p in paths)
        {
            try
            {
                list.Add(Directory.Exists(p)
                    ? new TwinPanel.Core.FileEntry(Path.GetFileName(p.TrimEnd('\\', '/')), p, true, 0, Directory.GetLastWriteTime(p), FileAttributes.Directory)
                    : new TwinPanel.Core.FileEntry(Path.GetFileName(p), p, false, new FileInfo(p).Length, File.GetLastWriteTime(p), File.GetAttributes(p)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (list.Count == 0) return;
        if (forced is { } mv) await main.DropAsync(null, list, dest, mv);
        else AskCopyOrMove(list.Count, dest, move => main.DropAsync(null, list, dest, move));
    }

    /// <summary>Menu sous la souris après un lâcher : copier, déplacer ou annuler.</summary>
    private void AskCopyOrMove(int count, string target, Func<bool, Task> run)
    {
        var name = TwinPanel.Core.Remote.PanelRemotePath.TryParse(target, out var rp)
            ? (TwinPanel.Core.Remote.RemotePath.FileName(rp.Path) is { Length: > 0 } n ? n : rp.Session)
            : Path.GetFileName(target.TrimEnd('\\', '/')) is { Length: > 0 } ln ? ln : target;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = L.F("{0} vers « {1} »", (count == 1 ? L.T("1 élément") : L.F("{0} éléments", count)), name), IsEnabled = false });
        menu.Items.Add(new Separator());
        var copy = new MenuItem { Header = L.T("_Copier ici") };
        copy.Click += async (_, _) => await run(false);
        var move = new MenuItem { Header = L.T("_Déplacer ici") };
        move.Click += async (_, _) => await run(true);
        menu.Items.Add(copy);
        menu.Items.Add(move);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = L.T("Annuler") });
        menu.Open(ActiveList);
    }

    // ---- Bouton droit façon Total Commander ---------------------------------------------
    // Clic droit court : marque / démarque l'élément. Bouton droit maintenu en glissant : marque (ou démarque)
    // tout ce que la souris survole. Bouton droit maintenu sans bouger (½ s) puis relâché : menu de l'élément
    // (ouvert au relâchement : sinon relâcher le bouton sur le menu choisirait sa première ligne).
    // Touche Menu ou Maj+F10 : menu sans changer le marquage.

    private static readonly TimeSpan MenuHoldDelay = TimeSpan.FromMilliseconds(500);
    private bool _rightDragging;
    private bool _rightMarkValue;
    private bool _rightMoved;
    private bool _rightHoldReached;
    private bool _rightWasMarked;
    private FileItemViewModel? _rightStart, _rightLast;
    private IDisposable? _menuTimer;

    private FileItemViewModel? RowAt(Point p) => IsThumbs ? ThumbList.ItemAt(p)
        : (FileGrid.InputHitTest(p) as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext as FileItemViewModel;

    /// <summary>Élément sous la souris, dans la vue affichée.</summary>
    private FileItemViewModel? ItemUnder(PointerEventArgs e) => IsThumbs ? ThumbList.ItemAt(e.GetPosition(ThumbList))
        : (e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true)?.DataContext as FileItemViewModel;

    private bool IsThumbs => _tab?.IsThumbnails == true;

    /// <summary>La vue affichée : liste ou miniatures. Souris et clavier se comportent de la même façon dans les deux.</summary>
    private Control ActiveList => IsThumbs ? ThumbList : FileGrid;

    private void Reveal(FileItemViewModel item)
    {
        if (IsThumbs) ThumbList.Reveal(item);
        else FileGrid.ScrollIntoView(item, null);
    }

    private void StartRightDrag(FileItemViewModel item, PointerPressedEventArgs e)
    {
        if (_tab is null) return;
        _tab.CurrentItem = item;
        ActiveList.Focus();
        _rightDragging = true;
        _rightMoved = false;
        _rightHoldReached = false;
        _rightStart = _rightLast = item;
        _rightWasMarked = item.IsMarked;
        _rightMarkValue = !item.IsMarked;
        _tab.SetMark(item, _rightMarkValue);
        e.Pointer.Capture(ActiveList);
        _menuTimer?.Dispose();
        _menuTimer = DispatcherTimer.RunOnce(() =>
        {
            if (!_rightDragging || _rightMoved || _rightStart is not { } it || _tab is null) return;
            // Maintenu sans bouger : c'est une ouverture de menu, pas un marquage → l'élément reprend son état d'avant
            // (le menu agit sur les éléments marqués, sinon sur celui sous le curseur).
            if (!it.IsParentLink) _tab.SetMark(it, _rightWasMarked);
            _rightHoldReached = true;
        }, MenuHoldDelay);
        e.Handled = true;
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is { } candidate && _dragPress is { } press && _tab is not null)
        {
            if (!e.GetCurrentPoint(ActiveList).Properties.IsLeftButtonPressed) { _dragCandidate = null; return; }
            var d = e.GetPosition(ActiveList) - _dragStart;
            if (Math.Abs(d.X) + Math.Abs(d.Y) < 8) return;
            _dragCandidate = null;
            CancelClickRename();
            _ = StartDragAsync(candidate, press);
            return;
        }
        if (!_rightDragging || _tab is null) return;
        var p = e.GetPosition(ActiveList);
        var item = RowAt(p);
        // Souris au-dessus / au-dessous de la liste : on avance d'une ligne (défilement pendant le marquage).
        if (item is null && _rightLast is not null)
        {
            int i = _tab.Items.IndexOf(_rightLast);
            if (p.Y < 0 && i > 0) item = _tab.Items[i - 1];
            else if (p.Y > ActiveList.Bounds.Height && i >= 0 && i < _tab.Items.Count - 1) item = _tab.Items[i + 1];
            if (item is not null) Reveal(item);
        }
        if (item is null || ReferenceEquals(item, _rightLast)) return;
        _rightMoved = true;
        _menuTimer?.Dispose();
        // Toutes les lignes entre la précédente et celle-ci (un geste rapide en saute) prennent le même état.
        int a = _tab.Items.IndexOf(_rightLast!), b = _tab.Items.IndexOf(item);
        if (a >= 0 && b >= 0)
            for (int k = Math.Min(a, b); k <= Math.Max(a, b); k++) _tab.SetMark(_tab.Items[k], _rightMarkValue);
        _rightLast = item;
        _tab.CurrentItem = item;
        e.Handled = true;
    }

    private void EndRightDrag()
    {
        _rightDragging = false;
        _menuTimer?.Dispose();
        _menuTimer = null;
    }

    /// <summary>Clic droit sur un onglet : dupliquer, renommer, verrouiller, fermer, doublons.</summary>
    private void OnTabPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton is not (MouseButton.Right or MouseButton.Middle) || _vm is null || Main is not { } main) return;
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is not FolderTabViewModel tab) return;
        e.Handled = true;
        var panel = _vm;
        // Clic milieu : fermer l'onglet (confirmation s'il est verrouillé).
        if (e.InitialPressMouseButton == MouseButton.Middle) { _ = main.CloseTabAsync(panel, tab); return; }
        var menu = new ContextMenu();
        void Add(string header, Action action, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }
        Add(L.T("Dupliquer cet onglet"), () => _ = panel.DuplicateTabAsync(tab));
        Add(L.T("Renommer…"), () => _ = main.RenameTabAsync(tab));
        menu.Items.Add(new Separator());
        Add(tab.LockMode == TwinPanel.Core.Settings.TabLock.Locked ? L.T("Déverrouiller cet onglet") : L.T("Verrouiller cet onglet"),
            () => panel.ToggleLock(tab, TwinPanel.Core.Settings.TabLock.Locked));
        Add(tab.LockMode == TwinPanel.Core.Settings.TabLock.LockedAllowChange ? L.T("Déverrouiller cet onglet") : L.T("Verrouiller cet onglet avec changement de dossier"),
            () => panel.ToggleLock(tab, TwinPanel.Core.Settings.TabLock.LockedAllowChange));
        menu.Items.Add(new Separator());
        Add(L.T("Fermer cet onglet"), () => _ = main.CloseTabAsync(panel, tab), panel.Tabs.Count > 1);
        Add(L.T("Fermer tous les onglets"), () => _ = main.CloseAllTabsAsync(panel), panel.Tabs.Count > 1);
        Add(L.T("Supprimer les doublons"), () => main.RemoveDuplicateTabs(panel), panel.Tabs.Count > 1);
        menu.Open(TabList);
    }

    /// <summary>Menu des éléments (clic droit maintenu, touche Menu, Maj+F10) ; onEmpty : clic droit sur le vide de la liste.</summary>
    public void ShowItemMenu() => ShowItemMenu(onEmpty: false);

    private void ShowItemMenu(bool onEmpty)
    {
        if (Main is not { } m || _tab is null) return;
        var menu = new ContextMenu();
        AppMenus.BuildItem(menu.Items, m, onEmpty ? null : _tab.CurrentItem, CopyToClipboardAsync);
        menu.Open(ActiveList);
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left) _dragCandidate = null;
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (_rightDragging)
        {
            bool menu = _rightHoldReached && !_rightMoved;
            EndRightDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
            if (menu) Dispatcher.UIThread.Post(ShowItemMenu);
            return;
        }
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is null) return;
        ShowColumnMenu();
        e.Handled = true;
    }

    /// <summary>Clic droit sur les titres des colonnes : choisir les colonnes affichées.</summary>
    private void ShowColumnMenu()
    {
        if (Main is not { } main) return;
        var a = main.Appearance;
        var menu = new ContextMenu();
        void Toggle(string label, bool value, string key)
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = value };
            item.Click += (_, _) => main.SetColumnVisible(key, !value);
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuItem { Header = L.T("Colonnes affichées"), IsEnabled = false });
        Toggle("Extension", a.ShowExtensionColumn, "Extension");
        Toggle(L.T("Taille"), a.ShowSizeColumn, "Size");
        Toggle("Date", a.ShowDateColumn, "Date");
        Toggle(L.T("Droits (755)"), a.ShowModeColumn, "Mode");
        Toggle(L.T("Propriétaire"), a.ShowOwnerColumn, "Owner");
        Toggle(L.T("Groupe"), a.ShowGroupColumn, "Group");
        menu.Items.Add(new Separator());
        Toggle(L.T("Durée (vidéos, musiques)"), a.ShowDurationColumn, "MediaDuration");
        Toggle(L.T("Résolution"), a.ShowResolutionColumn, "MediaResolution");
        Toggle(L.T("Codec vidéo (et HDR / Dolby Vision)"), a.ShowVideoCodecColumn, "MediaVideoCodec");
        Toggle(L.T("Langues audio"), a.ShowAudioLanguagesColumn, "MediaAudioLanguages");
        menu.Items.Add(new Separator());
        Toggle(L.T("Taille des dossiers (calcul automatique)"), a.ShowFolderSizes, "FolderSizes");
        if (TwinPanel.App.Services.ShellIcons.IsSupported) Toggle(TwinPanel.App.Services.ShellIcons.OptionName, a.UseShellIcons, "ShellIcons");
        var reset = new MenuItem { Header = L.T("Ordre des colonnes par défaut"), IsEnabled = a.ColumnOrder.Count > 0 };
        reset.Click += (_, _) => main.SetColumnOrder([]);
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        var names = new MenuItem { Header = L.T("Propriétaire / groupe en noms"), ToggleType = MenuItemToggleType.Radio, IsChecked = !a.OwnerAsNumber };
        names.Click += (_, _) => main.SetOwnerAsNumber(false);
        var numbers = new MenuItem { Header = L.T("Propriétaire / groupe en numéros (uid / gid)"), ToggleType = MenuItemToggleType.Radio, IsChecked = a.OwnerAsNumber };
        numbers.Click += (_, _) => main.SetOwnerAsNumber(true);
        menu.Items.Add(names);
        menu.Items.Add(numbers);
        menu.Open(ActiveList);
    }

    /// <summary>Donne le focus à la liste (Tab pour changer de panneau).</summary>
    public void FocusList()
    {
        ActiveList.Focus();
        if (_tab?.CurrentItem is { } e) Reveal(e);
    }

    // ---- Abonnements ViewModel ------------------------------------------------------

    private void AttachPanel(FilePanelViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnPanelPropertyChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnPanelPropertyChanged;
        AttachTab(_vm?.Current);
    }

    private void AttachTab(FolderTabViewModel? tab)
    {
        FinishInlineRename(commit: true);
        if (_tab is not null)
        {
            _tab.PropertyChanged -= OnTabPropertyChanged;
            _tab.ListUpdating -= RememberTopRow;
            _tab.ListUpdated -= RestoreTopRow;
        }
        bool wasThumbs = IsThumbs;
        _tab = tab;
        if (_tab is not null)
        {
            _tab.PropertyChanged += OnTabPropertyChanged;
            _tab.ListUpdating += RememberTopRow;
            _tab.ListUpdated += RestoreTopRow;
        }
        UpdateSortHeaders();
        UpdateFolderColumn();
        ThumbList.SetTab(IsThumbs ? _tab : null);
        if (wasThumbs != IsThumbs) RefocusAfterViewChange();
    }

    /// <summary>
    /// Passage liste ↔ miniatures : la vue masquée perdrait le clavier. Attendu après la mise en page,
    /// pour que la nouvelle vue connaisse sa taille. Seulement dans le panneau actif.
    /// </summary>
    private void RefocusAfterViewChange() => Dispatcher.UIThread.Post(() =>
    {
        if (_vm is not null && ReferenceEquals(Main?.ActivePanel, _vm)) FocusList();
    }, DispatcherPriority.Background);

    /// <summary>La colonne « Dossier » n'existe qu'en vue à plat.</summary>
    private void UpdateFolderColumn()
    {
        if (FileGrid.Columns.FirstOrDefault(c => c.SortMemberPath == "Folder") is { } column) column.IsVisible = _tab?.IsFlat == true;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FilePanelViewModel.Current):
                AttachTab(_vm?.Current);
                RevealSelectedTab();
                break;
            case nameof(FilePanelViewModel.IsEditingPath) when _vm?.IsEditingPath == true:
                Dispatcher.UIThread.Post(() => { PathBox.Focus(); PathBox.SelectAll(); });
                break;
            case nameof(FilePanelViewModel.IsEditingPath):
                FocusList();
                break;
        }
    }

    private bool _pathMenuHooked;

    /// <summary>
    /// Chemin en modification (crayon) : refermé quand on quitte la case, mais pas quand son propre menu
    /// (clic droit : Couper, Copier, Coller…) s'ouvre ; refermé ensuite si ce menu se ferme ailleurs.
    /// </summary>
    private void ClosePathBoxIfLeft()
    {
        if (_vm is null || !_vm.IsEditingPath || PathBox.IsKeyboardFocusWithin) return;
        if (PathBox.ContextFlyout is Avalonia.Controls.Primitives.PopupFlyoutBase { IsOpen: true } flyout)
        {
            if (!_pathMenuHooked)
            {
                _pathMenuHooked = true;
                flyout.Closed += (_, _) => Dispatcher.UIThread.Post(ClosePathBoxIfLeft);
            }
            return;
        }
        if (PathBox.ContextMenu is { IsOpen: true } menu)
        {
            if (!_pathMenuHooked)
            {
                _pathMenuHooked = true;
                menu.Closed += (_, _) => Dispatcher.UIThread.Post(ClosePathBoxIfLeft);
            }
            return;
        }
        _vm.IsEditingPath = false;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FolderTabViewModel.CurrentItem) when _tab?.CurrentItem is { } item:
                Reveal(item);
                break;
            case nameof(FolderTabViewModel.SortColumn) or nameof(FolderTabViewModel.SortDescending):
                UpdateSortHeaders();
                break;
            case nameof(FolderTabViewModel.IsFlat):
                UpdateFolderColumn();
                break;
            case nameof(FolderTabViewModel.IsThumbnails):
                ThumbList.SetTab(IsThumbs ? _tab : null);
                RefocusAfterViewChange();
                break;
            case nameof(FolderTabViewModel.CurrentPath):
                // Le fil d'Ariane défile jusqu'au dossier courant (à droite).
                Dispatcher.UIThread.Post(() => CrumbScroller.ScrollToEnd(), DispatcherPriority.Background);
                break;
        }
    }

    // ---- Tri ---------------------------------------------------------------------------

    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        e.Handled = true; // on trie nous-mêmes (dossiers toujours en premier)
        if (_tab is not null && Enum.TryParse<SortColumn>(e.Column.SortMemberPath, out var column))
            _tab.SortBy(column);
    }

    private void UpdateSortHeaders()
    {
        var labels = new Dictionary<string, string>
        {
            ["Name"] = L.T("Nom"), ["Folder"] = L.T("Dossier"), ["Extension"] = "Ext", ["Size"] = L.T("Taille"), ["Date"] = "Date",
            ["Mode"] = L.T("Droits"), ["Owner"] = L.T("Propriétaire"), ["Group"] = L.T("Groupe"),
        };
        foreach (var col in FileGrid.Columns)
        {
            if (col.SortMemberPath is not { } key || !labels.TryGetValue(key, out var label)) continue;
            bool active = _tab is not null && _tab.SortColumn.ToString() == key;
            col.Header = active ? $"{label} {(_tab!.SortDescending ? "▼" : "▲")}" : label;
        }
    }

    // ---- Souris : Ctrl+clic marque, Maj+clic marque une plage ------------------------------

    // ---- Position de défilement ---------------------------------------------------------
    // Une relecture ne doit jamais faire sauter la liste : la ligne du haut reste en haut, même si le curseur est ailleurs.

    private string? _topPath;
    private int _topIndex;

    private FileItemViewModel? TopRow() => FileGrid.GetVisualDescendants().OfType<DataGridRow>()
        .Where(r => r.IsVisible && r.Bounds.Bottom > 0 && r.DataContext is FileItemViewModel)
        .OrderBy(r => r.Bounds.Top).Select(r => r.DataContext).OfType<FileItemViewModel>().FirstOrDefault();

    private void RememberTopRow()
    {
        _topPath = null;
        if (IsThumbs || _tab is null || TopRow() is not { } top) return;
        _topIndex = _tab.Items.IndexOf(top);
        if (_topIndex > 0) _topPath = top.FullPath;
    }

    private void RestoreTopRow()
    {
        if (_topPath is not { } path || _tab is not { } tab) return;
        _topPath = null;
        int index = _topIndex;
        // Après la mise en page : c'est elle qui décide où la grille s'est placée.
        Dispatcher.UIThread.Post(() =>
        {
            if (tab != _tab || IsThumbs || tab.Items.Count == 0 || TopRow()?.FullPath == path) return;
            var target = tab.Items.FirstOrDefault(i => i.FullPath == path) ?? tab.Items[Math.Min(index, tab.Items.Count - 1)];
            // Une ligne au-dessus de la vue s'aligne en haut : on passe donc par la fin de la liste.
            FileGrid.ScrollIntoView(tab.Items[^1], null);
            FileGrid.UpdateLayout();
            FileGrid.ScrollIntoView(target, null);
        }, DispatcherPriority.Loaded);
    }

    // ---- Renommer d'un clic sur le nom ----------------------------------------------------

    /// <summary>Plus long qu'un double-clic : un double-clic ouvre le fichier, il ne doit pas le renommer.</summary>
    private static readonly TimeSpan ClickRenameDelay = TimeSpan.FromMilliseconds(600);
    private bool _wasActive;
    private IDisposable? _clickRename;
    private TextBox? _renameBox;
    private TextBlock? _renameLabel;
    private FolderTabViewModel? _renameTab;

    private void CancelClickRename()
    {
        _clickRename?.Dispose();
        _clickRename = null;
    }

    private bool IsNameClick(PointerPressedEventArgs e, FileItemViewModel item) =>
        e.KeyModifiers == KeyModifiers.None && e.ClickCount == 1 && _wasActive && !IsThumbs && !item.IsParentLink
        && ReferenceEquals(item, _tab?.CurrentItem) && e.Source is TextBlock text && text.Classes.Contains("fileName")
        && Main?.Settings.Behavior.ClickToRename == true;

    private void BeginInlineRename(FileItemViewModel item)
    {
        _clickRename = null;
        if (_renameBox is not null || _tab is not { } tab || !ReferenceEquals(item, tab.CurrentItem)) return;
        var label = FileGrid.GetVisualDescendants().OfType<DataGridRow>().FirstOrDefault(r => r.IsVisible && ReferenceEquals(r.DataContext, item))
            ?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Classes.Contains("fileName"));
        if (label?.GetVisualParent() is not Panel host) return;

        var box = new TextBox
        {
            Text = item.Name,
            MinHeight = 0,
            Padding = new Thickness(3, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Escape)) return;
            e.Handled = true;
            FinishInlineRename(commit: e.Key == Key.Enter);
        }, RoutingStrategies.Tunnel);
        _renameBox = box;
        _renameLabel = label;
        _renameTab = tab;
        tab.PauseAutoRefresh = true;
        label.IsVisible = false;
        host.Children.Add(box);
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_renameBox, box)) return;
            box.Focus();
            box.SelectionStart = 0;
            var stem = item.IsDirectory ? item.Name.Length : Path.GetFileNameWithoutExtension(item.Name).Length;
            box.SelectionEnd = stem > 0 ? stem : item.Name.Length;
            box.LostFocus += (_, _) => Dispatcher.UIThread.Post(() => FinishRenameIfLeft(box));
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Renommage dans la liste : validé quand on quitte la case, mais pas quand son propre menu
    /// (clic droit : Couper, Copier, Coller…) s'ouvre ; validé ensuite si ce menu se ferme ailleurs.
    /// </summary>
    private void FinishRenameIfLeft(TextBox box)
    {
        if (!ReferenceEquals(_renameBox, box) || box.IsKeyboardFocusWithin) return;
        if (box.ContextFlyout is Avalonia.Controls.Primitives.PopupFlyoutBase { IsOpen: true } flyout)
        {
            if (!ReferenceEquals(_renameMenuHooked, box))
            {
                _renameMenuHooked = box;
                flyout.Closed += (_, _) => Dispatcher.UIThread.Post(() => FinishRenameIfLeft(box));
            }
            return;
        }
        if (box.ContextMenu is { IsOpen: true } menu)
        {
            if (!ReferenceEquals(_renameMenuHooked, box))
            {
                _renameMenuHooked = box;
                menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => FinishRenameIfLeft(box));
            }
            return;
        }
        FinishInlineRename(commit: true);
    }

    private TextBox? _renameMenuHooked;

    private void FinishInlineRename(bool commit)
    {
        if (_renameBox is not { } box) return;
        _renameBox = null;
        var item = _renameTab?.CurrentItem;
        var name = box.Text?.Trim() ?? string.Empty;
        if (_renameTab is not null) _renameTab.PauseAutoRefresh = false;
        (box.GetVisualParent() as Panel)?.Children.Remove(box);
        if (_renameLabel is not null) _renameLabel.IsVisible = true;
        var original = _renameLabel?.DataContext as FileItemViewModel;
        var tab = _renameTab;
        _renameLabel = null;
        _renameTab = null;
        FocusList();
        if (!commit || name.Length == 0 || original is null || tab is null || name == original.Name) return;
        // Un clic ailleurs valide aussi le nom : on revient sur la ligne renommée.
        _vm?.Activate();
        if (!ReferenceEquals(item, original)) tab.CurrentItem = original;
        if (Main is { } main) _ = main.RenameInlineAsync(name);
    }

    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelClickRename();
        if (_renameBox is not null)
        {
            if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) == _renameBox) return;
            FinishInlineRename(commit: true);
        }
        if (HandleMiddleClick(e)) return;
        if (_tab is not null && e.GetCurrentPoint(ActiveList).Properties.IsRightButtonPressed
            && (e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is null
            && ItemUnder(e) is { } rightItem)
        {
            StartRightDrag(rightItem, e);
            return;
        }
        // Clic droit sur le vide de la liste (sous les fichiers) : menu du dossier (nouveau dossier, coller…).
        if (_tab is not null && e.GetCurrentPoint(ActiveList).Properties.IsRightButtonPressed
            && (e.Source as Visual)?.FindAncestorOfType<DataGridColumnHeader>(includeSelf: true) is null
            && ItemUnder(e) is null)
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() => ShowItemMenu(onEmpty: true));
            return;
        }
        if (_tab is null || !e.GetCurrentPoint(ActiveList).Properties.IsLeftButtonPressed) return;
        if (IsThumbs) ThumbList.Focus(); // la liste, elle, le fait d'elle-même
        if (ItemUnder(e) is not { } item) return;
        // Point de départ d'un éventuel glisser (bouton gauche maintenu puis souris déplacée).
        _dragCandidate = e.KeyModifiers == KeyModifiers.None && !item.IsParentLink ? item : null;
        _dragPress = e;
        _dragStart = e.GetPosition(ActiveList);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _tab.ToggleMark(item);
            _tab.CurrentItem = item;
            ActiveList.Focus();
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _tab.CurrentItem is { } from)
        {
            _tab.MarkRange(from, item, true);
            _tab.CurrentItem = item;
            ActiveList.Focus();
            e.Handled = true;
        }
        else if (IsThumbs) _tab.CurrentItem = item;
        else if (IsNameClick(e, item)) _clickRename = DispatcherTimer.RunOnce(() => BeginInlineRename(item), ClickRenameDelay);
    }

    // ---- Clavier -------------------------------------------------------------------------

    private bool QuickSearchActive => _vm is { QuickSearch.Length: > 0 } && DateTime.Now - _lastQuickKey < QuickSearchTimeout;

    private void OnGridTextInput(object? sender, TextInputEventArgs e)
    {
        if (_renameBox is not null) return; // le nom se tape dans sa case
        if (_suppressTextInput) { _suppressTextInput = false; e.Handled = true; return; }
        if (_vm is null || _tab is null || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]) || e.Text == " ") return;

        var text = QuickSearchActive ? _vm.QuickSearch + e.Text : e.Text;
        _lastQuickKey = DateTime.Now;
        if (_tab.QuickFind(text)) _vm.QuickSearch = text;   // si rien ne correspond, on garde la recherche précédente
        e.Handled = true;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (_renameBox is not null) return; // le nom se tape dans sa case
        CancelClickRename();
        if (_vm is null || _tab is null) return;
        var mods = e.KeyModifiers;
        var cur = _tab.CurrentItem;
        bool handled = true;

        switch (e.Key)
        {
            case Key.Enter when mods == KeyModifiers.None:
                _vm.QuickSearch = string.Empty;
                _ = _tab.OpenCurrentAsync();
                break;

            case Key.Apps:
            case Key.F10 when mods == KeyModifiers.Shift:
                ShowItemMenu();
                break;

            case Key.Enter when mods == KeyModifiers.Alt:
                // Alt+Entrée : la grille garderait la touche pour elle, on ouvre les Propriétés ici.
                Main?.ShowPropertiesCommand.Execute(null);
                break;

            case Key.Enter when mods.HasFlag(KeyModifiers.Control):
                // Ctrl+Entrée : nom dans la ligne de commande ; Ctrl+Maj+Entrée : chemin complet.
                Main?.AppendToCommandLine(fullPath: mods.HasFlag(KeyModifiers.Shift));
                break;

            case Key.Back when mods == KeyModifiers.None:
                if (QuickSearchActive)
                {
                    _lastQuickKey = DateTime.Now;
                    _vm.QuickSearch = _vm.QuickSearch[..^1];
                    if (_vm.QuickSearch.Length > 0) _tab.QuickFind(_vm.QuickSearch);
                }
                else _ = _tab.GoUpAsync();
                break;

            case Key.Insert when mods == KeyModifiers.None && cur is not null:
                _tab.ToggleMark(cur);
                _tab.MoveCursor(1);
                break;

            case Key.Space when mods == KeyModifiers.None && cur is not null:
                _suppressTextInput = true;
                _tab.ToggleMark(cur);
                if (cur.IsDirectory && cur.IsMarked) _ = _tab.ComputeFolderSizeAsync(cur);
                break;

            case Key.Down or Key.Up when mods == KeyModifiers.Shift && cur is not null:
                // Maj+flèches : marque en se déplaçant (miniatures : toutes les cases parcourues).
                for (int step = IsThumbs ? ThumbList.Columns : 1; step > 0; step--)
                {
                    if (_tab.CurrentItem is { } passed) _tab.SetMark(passed, true);
                    _tab.MoveCursor(e.Key == Key.Down ? 1 : -1);
                }
                break;

            case Key.Left or Key.Right or Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End
                when IsThumbs && mods == KeyModifiers.None:
                // La liste déplace son curseur elle-même ; ici, c'est à nous de le faire.
                _vm.QuickSearch = string.Empty;
                _tab.MoveCursor(e.Key switch
                {
                    Key.Left => -1,
                    Key.Right => 1,
                    Key.Up => -ThumbList.Columns,
                    Key.Down => ThumbList.Columns,
                    Key.PageUp => -ThumbList.PageItems,
                    Key.PageDown => ThumbList.PageItems,
                    Key.Home => -_tab.Items.Count,
                    _ => _tab.Items.Count,
                });
                break;

            case Key.Home or Key.End when mods == KeyModifiers.None:
                // Seule, la grille resterait sur la même ligne : elle changerait seulement de colonne.
                _vm.QuickSearch = string.Empty;
                _tab.MoveCursor(e.Key == Key.Home ? -_tab.Items.Count : _tab.Items.Count);
                break;

            case Key.Add when mods == KeyModifiers.Alt:
                _suppressTextInput = true;
                _tab.MarkSameExtension();
                break;
            case Key.Add when mods == KeyModifiers.None:
                _suppressTextInput = true;
                if (Main is { } m1) _ = m1.MarkByMaskAsync(true);
                break;
            case Key.Subtract when mods == KeyModifiers.None:
                _suppressTextInput = true;
                if (Main is { } m2) _ = m2.MarkByMaskAsync(false);
                break;
            case Key.Multiply when mods == KeyModifiers.None:
                _suppressTextInput = true;
                _tab.InvertMarks();
                break;

            case Key.A when mods == KeyModifiers.Control:
                _tab.MarkAll(true);
                break;
            case Key.A when mods == (KeyModifiers.Control | KeyModifiers.Shift):
            case Key.Subtract when mods == KeyModifiers.Control:
                _suppressTextInput = e.Key == Key.Subtract;
                _tab.MarkAll(false);
                break;

            case Key.C when mods == (KeyModifiers.Control | KeyModifiers.Shift):
                _ = CopyToClipboardAsync(namesOnly: false);
                break;
            case Key.C when mods == (KeyModifiers.Control | KeyModifiers.Alt):
                _ = CopyToClipboardAsync(namesOnly: true);
                break;

            case Key.Delete when mods == KeyModifiers.None:
                // Suppr uniquement dans la liste (jamais pendant la saisie d'un chemin ou d'une commande).
                Main?.DeleteCommand.Execute(null);
                break;

            case Key.Escape:
                _vm.QuickSearch = string.Empty;
                handled = _tab.IsScanning;
                _tab.StopFlatScan();
                break;

            case Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End:
                _vm.QuickSearch = string.Empty;
                handled = false;
                break;

            default:
                handled = false;
                break;
        }
        if (handled) e.Handled = true;
    }

    private async Task CopyToClipboardAsync(bool namesOnly)
    {
        if (Main is not { } main || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        var text = main.GetPathsForClipboard(namesOnly);
        if (text.Length == 0) return;
        await clipboard.SetTextAsync(text);
        main.StatusMessage = namesOnly ? L.T("Noms copiés dans le presse-papiers.") : L.T("Chemins copiés dans le presse-papiers.");
    }
}
