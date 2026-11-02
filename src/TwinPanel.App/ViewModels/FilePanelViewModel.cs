// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using TwinPanel.Core;
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Sources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.ViewModels;

/// <summary>Un panneau (gauche ou droite) avec ses onglets.</summary>
public partial class FilePanelViewModel : ObservableObject
{
    private readonly FileSourceRegistry _sources;

    private readonly FolderTabViewModel _placeholder;

    public FilePanelViewModel(FileSourceRegistry sources)
    {
        _sources = sources;
        // Onglet vide utilisé seulement avant l'initialisation, pour que les liaisons de l'interface ne plantent pas.
        _placeholder = new FolderTabViewModel(sources, SortColumn.Name, false);
    }

    /// <summary>Levé quand le panneau reçoit le focus.</summary>
    public event EventHandler? Activated;
    public event EventHandler<string>? ErrorOccurred;
    /// <summary>Clic sur une connexion de la barre des lecteurs : la fenêtre principale l'ouvre dans ce panneau.</summary>
    public event EventHandler<ConnectionProfile>? ConnectionRequested;

    public ObservableCollection<FolderTabViewModel> Tabs { get; } = [];
    /// <summary>Barre des lecteurs : disques puis connexions.</summary>
    public ObservableCollection<DriveButtonViewModel> DriveBar { get; } = [];
    [ObservableProperty] private bool _showDriveBar = true;
    private IReadOnlyList<DriveScanner.DriveSnapshot> _drives = [];
    private IReadOnlyList<(ConnectionProfile Profile, string? OpenAlias)> _connections = [];
    private FolderTabViewModel? _watchedTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    private FolderTabViewModel? _selectedTab;

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isEditingPath;
    [ObservableProperty] private string _pathInput = string.Empty;
    [ObservableProperty] private string _quickSearch = string.Empty;

    // ---- Arborescence (Ctrl+F8) et vue rapide (Ctrl+Q) ----------------------------------

    [ObservableProperty] private bool _showTree;
    [ObservableProperty] private FolderTreeViewModel? _tree;

    /// <summary>Vrai : ce panneau affiche l'aperçu de l'élément courant de l'autre panneau.</summary>
    [ObservableProperty] private bool _isQuickView;
    [ObservableProperty] private ViewerViewModel? _quickView;

    partial void OnShowTreeChanged(bool value)
    {
        if (value)
        {
            if (Tree is null)
            {
                Tree = new FolderTreeViewModel();
                Tree.PropertyChanged += (_, e) =>
                {
                    // Clic dans l'arborescence : on ouvre le dossier dans l'onglet courant.
                    if (e.PropertyName == nameof(FolderTreeViewModel.SelectedNode) && !Tree.IsRevealing
                        && Tree.SelectedNode is { FullPath.Length: > 0 } node
                        && !string.Equals(node.FullPath.TrimEnd('\\', '/'), Current.CurrentPath.TrimEnd('\\', '/'), PathText.Comparison))
                        _ = Current.NavigateAsync(node.FullPath);
                };
            }
            Tree.Reveal(Current.CurrentPath);
        }
    }

    partial void OnIsQuickViewChanged(bool value)
    {
        if (!value) SetQuickView(null);
    }

    /// <summary>Remplace l'aperçu (l'ancien est libéré : les images occupent de la mémoire).</summary>
    public void SetQuickView(ViewerViewModel? viewer)
    {
        var old = QuickView;
        QuickView = viewer;
        old?.Dispose();
    }

    /// <summary>L'onglet affiché (jamais null une fois initialisé).</summary>
    public FolderTabViewModel Current => SelectedTab ?? (Tabs.Count > 0 ? Tabs[0] : _placeholder);

    public bool ShowHidden { get; private set; }

    public void Activate() => Activated?.Invoke(this, EventArgs.Empty);

    // ---- Initialisation / sauvegarde ------------------------------------------------

    public async Task InitializeAsync(PanelState state, bool showHidden, string fallbackPath)
    {
        ShowHidden = showHidden;
        RefreshDrives();
        var saved = new List<TwinPanel.Core.Settings.SavedTab>();
        int active = 0;
        for (int i = 0; i < state.Tabs.Count; i++)
        {
            var tab = TwinPanel.Core.Settings.SavedTab.Decode(state.Tabs[i]);
            if (!CanReopen(tab.Path)) continue;
            if (i == state.ActiveTab) active = saved.Count; // l'onglet actif, même si un onglet disparu le précédait
            saved.Add(tab);
        }
        if (saved.Count == 0) saved.Add(new TwinPanel.Core.Settings.SavedTab(fallbackPath));

        foreach (var t in saved)
            await AddTabAsync(t.Path, state.SortColumn, state.SortDescending, select: false, t.Lock, t.Name);

        SelectedTab = Tabs[Math.Clamp(active, 0, Tabs.Count - 1)];
    }

    private static bool CanReopen(string path) => Directory.Exists(path)
        || (TwinPanel.Core.Archives.ArchivePath.TrySplit(path, out var archive, out _) && File.Exists(archive));

    public PanelState CaptureState(bool withView = false) => new()
    {
        // Tous les onglets, y compris sur un serveur : la fenêtre principale remplace ces derniers par la fiche de connexion.
        Tabs = Tabs.Where(t => t.CurrentPath.Length > 0).Select(t => t.ToSaved(withView).Encode()).ToList(),
        ActiveTab = SelectedTab is null ? 0 : Tabs.IndexOf(SelectedTab),
        SortColumn = Current.SortColumn,
        SortDescending = Current.SortDescending,
    };

    // ---- Onglets ---------------------------------------------------------------

    [RelayCommand]
    public Task NewTab() => AddTabAsync(Current.CurrentPath, Current.SortColumn, Current.SortDescending, select: true);

    [RelayCommand]
    public void CloseTab(FolderTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null || Tabs.Count <= 1) return;   // on garde toujours au moins un onglet
        int index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();
        SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    public void NextTab(int direction)
    {
        if (Tabs.Count < 2 || SelectedTab is null) return;
        int i = (Tabs.IndexOf(SelectedTab) + direction + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[i];
    }

    /// <summary>Remplace tous les onglets (jeu d'onglets rappelé). Une liste vide ne change rien : un panneau garde toujours un onglet.</summary>
    public async Task ReplaceTabsAsync(IReadOnlyList<TwinPanel.Core.Settings.SavedTab> tabs, int active, SortColumn sort, bool descending)
    {
        if (tabs.Count == 0) return;
        var old = Tabs.ToList();
        var added = new List<FolderTabViewModel>();
        foreach (var t in tabs)
        {
            var tab = await AddTabAsync(t.Path, sort, descending, select: false, t.Lock, t.Name);
            added.Add(tab);
            if (t.Flat) tab.ToggleFlat();
            tab.IsThumbnails = t.Thumbnails;
        }
        // Le nouvel onglet d'abord : la liste ne doit jamais se retrouver sans onglet affiché.
        SelectedTab = added[Math.Clamp(active, 0, added.Count - 1)];
        foreach (var t in old)
        {
            Tabs.Remove(t);
            t.Dispose();
        }
    }

    public async Task<FolderTabViewModel> AddTabAsync(string path, SortColumn sort, bool descending, bool select,
        TwinPanel.Core.Settings.TabLock lockMode = TwinPanel.Core.Settings.TabLock.None, string name = "", int? insertAt = null)
    {
        var tab = new FolderTabViewModel(_sources, sort, descending) { ShowHidden = ShowHidden, CustomName = name };
        tab.ErrorOccurred += (_, msg) => ErrorOccurred?.Invoke(this, msg);
        // Onglet verrouillé qui veut aller ailleurs : nouvel onglet juste à droite.
        tab.LockedNavigation += (s, target) =>
            _ = AddTabAsync(target, tab.SortColumn, tab.SortDescending, select: true, insertAt: Tabs.IndexOf(tab) + 1);
        tab.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FolderTabViewModel.CurrentPath) && s == SelectedTab)
            {
                SyncFromCurrent();
                if (ShowTree) Tree?.Reveal(tab.CurrentPath);
            }
        };
        if (insertAt is { } at && at >= 0 && at <= Tabs.Count) Tabs.Insert(at, tab); else Tabs.Add(tab);
        if (!await tab.NavigateAsync(path))
            await tab.NavigateAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (lockMode != TwinPanel.Core.Settings.TabLock.None) tab.SetLock(lockMode);
        if (select) SelectedTab = tab;
        return tab;
    }

    // ---- Menu des onglets (clic droit) ----

    public Task DuplicateTabAsync(FolderTabViewModel tab) =>
        AddTabAsync(tab.CurrentPath, tab.SortColumn, tab.SortDescending, select: true, insertAt: Tabs.IndexOf(tab) + 1);

    /// <summary>Verrouille (ou déverrouille si l'onglet l'est déjà de cette façon).</summary>
    public void ToggleLock(FolderTabViewModel tab, TwinPanel.Core.Settings.TabLock mode) =>
        tab.SetLock(tab.LockMode == mode ? TwinPanel.Core.Settings.TabLock.None : mode);

    /// <summary>Ferme tous les onglets sauf celui affiché et ceux verrouillés ; retourne le nombre fermé.</summary>
    public int CloseAllTabs(bool includeLocked)
    {
        var keep = SelectedTab ?? Tabs.FirstOrDefault();
        int closed = 0;
        foreach (var t in Tabs.ToList())
        {
            if (ReferenceEquals(t, keep) || (t.IsLocked && !includeLocked)) continue;
            Tabs.Remove(t);
            t.Dispose();
            closed++;
        }
        SelectedTab = keep;
        return closed;
    }

    /// <summary>Ferme les onglets qui montrent le même dossier qu'un onglet précédent (on garde l'affiché et les verrouillés).</summary>
    public int RemoveDuplicateTabs()
    {
        int closed = 0;
        foreach (var group in Tabs.GroupBy(t => t.CurrentPath, PathText.Comparer).Where(g => g.Count() > 1).ToList())
        {
            var list = group.ToList();
            var keep = list.FirstOrDefault(t => ReferenceEquals(t, SelectedTab)) ?? list.FirstOrDefault(t => t.IsLocked) ?? list[0];
            foreach (var t in list)
            {
                if (ReferenceEquals(t, keep) || t.IsLocked || ReferenceEquals(t, SelectedTab)) continue;
                Tabs.Remove(t);
                t.Dispose();
                closed++;
            }
        }
        return closed;
    }

    partial void OnSelectedTabChanged(FolderTabViewModel? value)
    {
        // Le lecteur en surbrillance suit le dossier affiché.
        if (_watchedTab is not null) _watchedTab.PropertyChanged -= OnTabPropertyChanged;
        _watchedTab = value;
        if (value is not null) value.PropertyChanged += OnTabPropertyChanged;
        // Verrouillé avec changement de dossier : en reprenant l'onglet, on revient à son dossier.
        if (value is { LockMode: TwinPanel.Core.Settings.TabLock.LockedAllowChange } t && t.LockedPath.Length > 0
            && !string.Equals(t.CurrentPath, t.LockedPath, PathText.Comparison))
            _ = t.NavigateAsync(t.LockedPath);
        SyncFromCurrent();
        if (ShowTree && value is not null) Tree?.Reveal(value.CurrentPath);
    }

    // ---- Barre de chemin / lecteurs -------------------------------------------------------

    /// <summary>Bouton « Arrêter » de la vue à plat.</summary>
    [RelayCommand] private void StopFlatScan() => Current.StopFlatScan();

    [RelayCommand]
    private void BeginEditPath()
    {
        PathInput = Current.CurrentPath;
        IsEditingPath = true;
    }

    [RelayCommand]
    private void CancelEditPath() => IsEditingPath = false;

    [RelayCommand]
    private async Task CommitEditPath()
    {
        var p = Environment.ExpandEnvironmentVariables(PathInput.Trim().Trim('"'));
        if (p.Length > 0 && !Path.IsPathRooted(p)) p = Path.Combine(Current.CurrentPath, p);
        if (p.Length == 0 || await Current.NavigateAsync(p)) IsEditingPath = false;
    }

    [RelayCommand]
    private Task GoToSegment(BreadcrumbSegment? segment) =>
        segment is null ? Task.CompletedTask : Current.NavigateAsync(segment.FullPath);

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FolderTabViewModel.CurrentPath)) UpdateDriveHighlight();
    }

    /// <summary>Clic sur un bouton de la barre des lecteurs.</summary>
    [RelayCommand]
    private async Task OpenDriveButton(DriveButtonViewModel? button)
    {
        if (button is null) return;
        Activate();
        if (button.Kind == DriveButtonKind.Drive) await Current.NavigateAsync(button.Key);
        else if (button.Profile is { } p) ConnectionRequested?.Invoke(this, p);
    }

    public void SetShowHidden(bool value)
    {
        ShowHidden = value;
        foreach (var t in Tabs) t.ShowHidden = value;
    }

    /// <summary>Relit les lecteurs (en arrière-plan) et met la barre à jour si un disque est apparu ou a disparu.</summary>
    public void RefreshDrives() => _ = RefreshDrivesAsync();

    private bool _scanningDrives;

    public async Task RefreshDrivesAsync()
    {
        if (_scanningDrives) return; // un lecteur réseau lent : on n'empile pas les lectures
        _scanningDrives = true;
        try
        {
            _drives = await Task.Run(DriveScanner.Scan);
            RebuildDriveBar();
        }
        finally { _scanningDrives = false; }
    }

    /// <summary>Connexions enregistrées à montrer dans la barre (avec le nom court de celles qui sont ouvertes).</summary>
    public void SetConnections(IReadOnlyList<(ConnectionProfile Profile, string? OpenAlias)> connections)
    {
        _connections = connections;
        RebuildDriveBar();
    }

    /// <summary>Couleurs des icônes de la barre des lecteurs changées dans les Options.</summary>
    public void ApplyDriveColors() => RebuildDriveBar();

    private void RebuildDriveBar()
    {
        var items = new List<DriveButtonViewModel>();
        foreach (var d in _drives)
            items.Add(new DriveButtonViewModel { Kind = DriveButtonKind.Drive, Key = d.Root, Label = d.Label, Tooltip = d.Tooltip, IconKey = d.IconKey });
        if (_connections.Count > 0) items.Add(new DriveButtonViewModel { Kind = DriveButtonKind.Separator, Key = "|" });
        foreach (var (p, alias) in _connections)
            items.Add(new DriveButtonViewModel
            {
                Kind = DriveButtonKind.Connection,
                Key = p.Id.ToString("N"),
                Label = p.DisplayButtonLabel,
                Tooltip = $"{p.Name}\n{TwinPanel.Core.Remote.RemoteProtocols.Label(p.Protocol)} — {p.Summary}" +
                          (alias is null ? L.T("\nClic : se connecter dans ce panneau") : L.T("\nConnecté — clic : afficher dans ce panneau")),
                IconKey = "IconServer",
                Profile = p,
                OpenAlias = alias,
            });

        // On ne remplace la barre que si elle a changé (pas de clignotement toutes les 3 secondes).
        if (!items.Select(i => i.Signature).SequenceEqual(DriveBar.Select(i => i.Signature)))
        {
            DriveBar.Clear();
            foreach (var i in items) DriveBar.Add(i);
        }
        UpdateDriveHighlight();
    }

    private void UpdateDriveHighlight()
    {
        var path = Current.CurrentPath;
        string? alias = TwinPanel.Core.Remote.PanelRemotePath.TryParse(path, out var remote) ? remote.Session : null;
        // Sous Linux, tout part de « / » : le bouton allumé est celui du disque le plus précis.
        var root = alias is not null || path.Length == 0 ? null
            : OperatingSystem.IsLinux() ? LocalDisks.Containing(path, DriveBar.Where(b => b.Kind == DriveButtonKind.Drive).Select(b => b.Key))
            : Path.GetPathRoot(path);
        foreach (var b in DriveBar)
            b.IsCurrent = b.Kind switch
            {
                DriveButtonKind.Drive => root is not null && string.Equals(b.Key, root, PathText.Comparison),
                DriveButtonKind.Connection => alias is not null && string.Equals(b.OpenAlias, alias, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
    }

    private void SyncFromCurrent()
    {
        if (SelectedTab is null) return;
        UpdateDriveHighlight();
        IsEditingPath = false;
        QuickSearch = string.Empty;
    }
}
