// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TwinPanel.Core;
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Search;
using TwinPanel.Core.Selection;
using TwinPanel.Core.Sources;

namespace TwinPanel.App.ViewModels;

/// <summary>Un onglet : un dossier affiché, son historique, son tri, ses fichiers marqués et sa surveillance.</summary>
public partial class FolderTabViewModel : ObservableObject, IDisposable
{
    private readonly FileSourceRegistry _sources;
    private readonly NavigationHistory _history = new();
    private readonly DispatcherTimer _refreshDebounce;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource _sizeCts = new();
    private IReadOnlyList<FileEntry> _rawEntries = [];
    private int _loadVersion;

    public FolderTabViewModel(FileSourceRegistry sources, SortColumn sortColumn, bool sortDescending)
    {
        _sources = sources;
        _sortColumn = sortColumn;
        _sortDescending = sortDescending;
        // Regroupe les rafales d'événements du disque en un seul rafraîchissement.
        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshDebounce.Tick += async (_, _) =>
        {
            if (PauseAutoRefresh) return; // le renommage en cours relancera le minuteur
            _refreshDebounce.Stop();
            await RefreshAsync();
        };
    }

    public event EventHandler<string>? ErrorOccurred;

    /// <summary>Ouverture d'un fichier distant (Entrée) : branché par la fenêtre principale (téléchargement puis ouverture).</summary>
    public static Func<FileEntry, Task>? RemoteFileOpener { get; set; }

    /// <summary>Entrée sur une archive d'un serveur : branché par la fenêtre principale (téléchargement avec progression).</summary>
    public static Func<FileEntry, Task>? RemoteArchiveOpener { get; set; }

    /// <summary>Fichier d'une archive ouvert depuis une copie temporaire (copie, archive) : surveillé pour ne pas perdre une modification.</summary>
    public static Action<string, string, string>? ArchiveCopyOpened { get; set; }

    /// <summary>Demande le mot de passe d'une archive ; vrai s'il est bon.</summary>
    public static Func<string, Task<bool>>? ArchivePasswordAsker { get; set; }

    /// <summary>Un onglet vient d'afficher un autre dossier : branché par la fenêtre principale (dossiers récents).</summary>
    public static Action<string>? FolderVisited { get; set; }

    /// <summary>Options → Comportement : Entrée sur une archive l'ouvre comme un dossier.</summary>
    public static bool OpenArchivesWithEnter { get; set; } = true;

    /// <summary>Une colonne média (durée, résolution…) est visible : on lit les vidéos / musiques du dossier en arrière-plan.</summary>
    public static bool LoadMediaColumns { get; set; }

    private CancellationTokenSource _mediaCts = new();

    /// <summary>Remplit les colonnes médias des vidéos et musiques du PC (MediaInfo, résultats gardés en mémoire).</summary>
    private void StartMediaColumns()
    {
        _mediaCts.Cancel();
        _mediaCts = new CancellationTokenSource();
        if (!LoadMediaColumns || IsRemote || IsInsideArchive) return;
        var items = Items.Where(i => !i.IsDirectory && i.Category is FileCategory.Video or FileCategory.Audio && !i.HasMedia).ToList();
        if (items.Count == 0) return;
        // Déjà connus : tout de suite ; les autres : lus un par un hors de l'interface.
        var todo = new List<FileItemViewModel>();
        foreach (var it in items)
        {
            if (TwinPanel.Core.Media.MediaCache.TryPeek(it.FullPath, it.Entry.Size, it.Entry.LastWriteTime, out var known)) it.SetMedia(known);
            else todo.Add(it);
        }
        if (todo.Count == 0) return;
        var ct = _mediaCts.Token;
        _ = Task.Run(() =>
        {
            if (!TwinPanel.Core.Media.MediaInfoLib.IsAvailable) return;
            foreach (var it in todo)
            {
                if (ct.IsCancellationRequested) return;
                var r = TwinPanel.Core.Media.MediaCache.Get(it.FullPath, it.Entry.Size, it.Entry.LastWriteTime);
                Dispatcher.UIThread.Post(() => it.SetMedia(r));
            }
        }, ct);
    }

    /// <summary>Options → Affichage : vraies icônes de Windows au lieu des icônes colorées de TwinPanel.</summary>
    public static bool UseShellIcons { get; set; }

    /// <summary>Met les icônes de Windows sur les lignes (connues : tout de suite ; les autres : lues sur un fil à part).</summary>
    private void StartShellIcons()
    {
        if (!UseShellIcons || !Services.ShellIcons.IsSupported) return;
        bool local = !IsRemote && !IsInsideArchive;
        foreach (var it in Items)
        {
            if (it.IsParentLink || it.ShellIcon is not null) continue;
            var (key, path, byType) = Services.ShellIcons.Describe(it.FullPath, it.Name, it.IsDirectory, local);
            if (Services.ShellIcons.TryGetCached(key, large: true, out var known)) { it.ShellIcon = known; continue; }
            var item = it;
            // Une ligne déjà remplacée reçoit son icône pour rien : sans gravité (et sans recherche coûteuse dans la liste).
            Services.ShellIcons.Request(key, path, it.IsDirectory, byType, large: true, icon => item.ShellIcon = icon);
        }
    }

    /// <summary>Options → Affichage : taille des dossiers calculée automatiquement (au lieu de « &lt;DIR&gt; »).</summary>
    public static bool ComputeFolderSizes { get; set; }

    /// <summary>
    /// Taille des dossiers d'un serveur : branché par la fenêtre principale (connexion à part, la navigation reste libre).
    /// Reçoit les dossiers à mesurer et rend chaque résultat dès qu'il est connu (chemin complet, octets),
    /// y compris la taille de leurs sous-dossiers quand le serveur la donne en même temps (« du »).
    /// </summary>
    public static Func<IReadOnlyList<FileEntry>, Action<string, long>, CancellationToken, Task>? RemoteFolderSizer { get; set; }

    // Tailles déjà calculées, pour tous les onglets et toute la séance (clé : chemin complet).
    // Reprises tant que la date du dossier ne change pas ; date inconnue (sous-dossier mesuré par « du ») : notée au premier affichage.
    private static readonly Dictionary<string, (DateTime? Date, long Bytes)> s_folderSizes = new(PathText.Comparer);
    private const int FolderSizeMemory = 200_000;

    private CancellationTokenSource _folderCts = new();
    private bool _folderJobRunning, _folderJobAgain;
    private string _folderJobPath = string.Empty;
    private readonly HashSet<string> _folderJobPending = new(PathText.Comparer);
    private static readonly TimeSpan SizeSortDelay = TimeSpan.FromMilliseconds(400);
    private bool _sizeSortPending;

    /// <summary>
    /// Calcule en arrière-plan, un par un, la taille des dossiers affichés. Annulé quand on change de dossier,
    /// mais pas par une relecture du même dossier (rafraîchissement automatique) : un gros dossier irait sinon
    /// recommencer à chaque fois sans jamais finir.
    /// </summary>
    private void StartFolderSizes()
    {
        bool continuing = _folderJobRunning && string.Equals(_folderJobPath, CurrentPath, PathText.Comparison);
        if (!continuing)
        {
            _folderCts.Cancel();
            _folderCts = new CancellationTokenSource();
            _folderJobRunning = false;
            _folderJobPending.Clear();
        }
        var todo = new List<FileItemViewModel>();
        foreach (var it in Items)
        {
            if (!it.IsDirectory || it.IsParentLink || it.ComputedSize is not null) continue;
            // Lien / jonction : non suivi (évite les boucles et les doubles comptes).
            if ((it.Entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            if (KnownFolderSize(it.Entry) is { } known) it.ComputedSize = known;
            else todo.Add(it);
        }
        if (todo.Count == 0 || !ComputeFolderSizes || IsInsideArchive) { UpdateSelectionInfo(); ScheduleSummary(); return; }
        if (continuing)
        {
            // Calcul en cours : les lignes relues reprennent leur « … » ; un dossier apparu entre-temps passera ensuite.
            foreach (var it in todo)
                if (_folderJobPending.Contains(it.FullPath)) it.IsComputingSize = true;
                else _folderJobAgain = true;
            ScheduleSummary();
            return;
        }
        var ct = _folderCts.Token;
        _folderJobRunning = true;
        _folderJobAgain = false;
        _folderJobPath = CurrentPath;
        foreach (var t in todo) { _folderJobPending.Add(t.FullPath); t.IsComputingSize = true; } // « … » : en attente
        _ = RunFolderSizesAsync(todo, ct);
        ScheduleSummary();
    }

    private async Task RunFolderSizesAsync(List<FileItemViewModel> todo, CancellationToken ct)
    {
        try
        {
            if (IsRemote) await RemoteFolderSizesAsync(todo, ct);
            else await LocalFolderSizesAsync(todo, ct);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _folderJobRunning = false;
                foreach (var it in Items) if (_folderJobPending.Contains(it.FullPath)) it.IsComputingSize = false;
                _folderJobPending.Clear();
                UpdateSelectionInfo();
                ScheduleSummary();
                if (_folderJobAgain) StartFolderSizes();
            }
        }
    }

    /// <summary>Résultat d'une mesure : retenu pour toute la séance, et montré sur la ligne de ce dossier si elle est affichée.</summary>
    private void StoreFolderSize(string path, DateTime? date, long bytes)
    {
        if (s_folderSizes.Count >= FolderSizeMemory) s_folderSizes.Clear();
        s_folderSizes[path] = (date, bytes);
        _folderJobPending.Remove(path);
        foreach (var it in Items)
        {
            if (!it.IsDirectory || !PathText.Comparer.Equals(it.FullPath, path)) continue;
            it.ComputedSize = bytes;
            it.IsComputingSize = false;
            s_folderSizes[path] = (it.Entry.LastWriteTime, bytes);
        }
        ScheduleSizeSort();
        ScheduleSummary();
    }

    private void StoreFolderSize(FileItemViewModel item, long bytes) => StoreFolderSize(item.FullPath, item.Entry.LastWriteTime, bytes);

    /// <summary>Ctrl+R : les tailles de ce dossier et de tout ce qu'il contient sont recalculées.</summary>
    public void ForgetFolderSizes()
    {
        if (CurrentPath.Length == 0) return;
        var prefix = CurrentPath.TrimEnd('/', '\\');
        foreach (var key in s_folderSizes.Keys.Where(k => k.StartsWith(prefix, PathText.Comparison)).ToList()) s_folderSizes.Remove(key);
        _folderCts.Cancel();
        _folderJobRunning = false;
        _folderJobPending.Clear();
        foreach (var it in Items) { it.ComputedSize = null; it.IsComputingSize = false; }
    }

    private bool _summaryPending;

    /// <summary>Total du bas de la liste : recalculé au plus toutes les 200 ms pendant les calculs de tailles.</summary>
    private void ScheduleSummary()
    {
        if (_summaryPending) return;
        _summaryPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _summaryPending = false;
            UpdateSummary();
        }, TimeSpan.FromMilliseconds(200));
    }

    private static long? KnownFolderSize(FileEntry entry)
    {
        if (!s_folderSizes.TryGetValue(entry.FullPath, out var known)) return null;
        if (known.Date is null)
        {
            // Mesuré avec son dossier parent : sa date est notée maintenant, pour voir ensuite s'il change.
            s_folderSizes[entry.FullPath] = (entry.LastWriteTime, known.Bytes);
            return known.Bytes;
        }
        return known.Date == entry.LastWriteTime ? known.Bytes : null;
    }

    /// <summary>Tri par taille : les dossiers prennent leur place au fil des calculs, sans à-coup à chaque résultat.</summary>
    private void ScheduleSizeSort()
    {
        if (SortColumn != SortColumn.Size || _sizeSortPending) return;
        _sizeSortPending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _sizeSortPending = false;
            MoveItemsToSizeOrder();
        }, SizeSortDelay);
    }

    /// <summary>Déplace les lignes sans les recréer : un nouveau tri complet arrêterait les calculs en cours.</summary>
    private void MoveItemsToSizeOrder()
    {
        if (SortColumn != SortColumn.Size) return;
        var order = new Dictionary<FileEntry, int>();
        foreach (var entry in EntrySorter.Sort(_rawEntries, SortColumn, SortDescending, KnownFolderSize)) order[entry] = order.Count;
        var target = Items.OrderBy(i => order.GetValueOrDefault(i.Entry, int.MaxValue)).ToList();
        if (target.SequenceEqual(Items)) return;
        var cursor = CurrentItem;
        ListUpdating?.Invoke();
        for (int i = 0; i < target.Count; i++)
        {
            int from = Items.IndexOf(target[i]);
            if (from != i) Items.Move(from, i);
        }
        if (!ReferenceEquals(CurrentItem, cursor)) CurrentItem = cursor;
        ListUpdated?.Invoke();
    }

    private async Task LocalFolderSizesAsync(List<FileItemViewModel> todo, CancellationToken ct)
    {
        foreach (var it in todo)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var r = await FolderSize.CalculateAsync(it.FullPath, ct);
                if (!ct.IsCancellationRequested) StoreFolderSize(it, r.Bytes);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { /* dossier illisible : reste <DIR> */ }
        }
    }

    private async Task RemoteFolderSizesAsync(List<FileItemViewModel> todo, CancellationToken ct)
    {
        if (RemoteFolderSizer is null) return;
        try
        {
            await RemoteFolderSizer(todo.Select(t => t.Entry).ToList(), (path, bytes) => Dispatcher.UIThread.Post(() =>
            {
                if (!ct.IsCancellationRequested) StoreFolderSize(path, null, bytes);
            }), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* connexion perdue : les dossiers restent <DIR> */ }
        // Les résultats arrivent par Post : la fin du calcul passe après eux.
        var done = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() => done.SetResult());
        await done.Task;
    }

    /// <summary>Vrai : l'onglet montre des miniatures au lieu de la liste (Ctrl+Maj+F1). Chaque onglet a son mode.</summary>
    [ObservableProperty] private bool _isThumbnails;

    // ---- Vue à plat (Ctrl+B) -------------------------------------------------------------------

    /// <summary>Fichiers d'un serveur et de ses sous-dossiers : branché par la fenêtre principale (connexion à part).</summary>
    public static Func<string, bool, CancellationToken, IAsyncEnumerable<FileEntry>>? RemoteFlatLister { get; set; }

    /// <summary>Nombre de fichiers au-delà duquel la lecture s'arrête.</summary>
    public static int FlatLimit { get; set; } = FlatListing.DefaultLimit;

    /// <summary>Vrai : la liste montre tous les fichiers des sous-dossiers. Le dossier de l'onglet ne change pas.</summary>
    [ObservableProperty] private bool _isFlat;
    [ObservableProperty] private bool _isScanning;

    private CancellationTokenSource? _flatCts;
    private int _flatVersion;
    private string _flatNote = string.Empty;

    public void ToggleFlat()
    {
        if (CurrentPath.Length == 0) return;
        if (IsInsideArchive)
        {
            ErrorOccurred?.Invoke(this, L.T("La vue à plat n'est pas disponible à l'intérieur d'une archive."));
            return;
        }
        if (!IsFlat)
        {
            IsFlat = true;
            _ = ScanFlatAsync(CurrentItem?.FullPath);
            return;
        }
        CancelFlatScan();
        LeaveFlat();
        _ = LoadAsync(CurrentPath, CurrentItem?.Name, recordHistory: false);
    }

    /// <summary>Échap ou bouton « Arrêter » : on garde ce qui a déjà été trouvé.</summary>
    public void StopFlatScan()
    {
        if (IsScanning) _flatCts?.Cancel();
    }

    /// <summary>Annule la lecture sans en attendre la fin (une navigation prend la main).</summary>
    private void CancelFlatScan()
    {
        _flatVersion++;
        _flatCts?.Cancel();
        IsScanning = false;
    }

    private void LeaveFlat()
    {
        IsFlat = false;
        _flatNote = string.Empty;
        if (SortColumn == SortColumn.Folder) SortColumn = SortColumn.Name;
    }

    /// <summary>Après un renommage : le fichier garde son dossier, avec son nouveau nom. Sinon, le fichier sous le curseur.</summary>
    private string? FlatCursor(string? selectName)
    {
        var path = CurrentItem?.FullPath;
        return path is null || selectName is null ? path : path[..(path.LastIndexOfAny(['/', '\\']) + 1)] + selectName;
    }

    private IAsyncEnumerable<FileEntry> ListFlat(string root, bool showHidden, CancellationToken ct)
    {
        if (!IsRemote) return FileSearch.SearchAsync(FlatListing.Criteria(root, showHidden), null, ct);
        return RemoteFlatLister?.Invoke(root, showHidden, ct) ?? throw new IOException(L.T("Les connexions ne sont pas prêtes."));
    }

    /// <summary>
    /// Lit les sous-dossiers hors de l'interface ; la liste se remplit par paquets, dans l'ordre d'arrivée, puis se trie à la fin.
    /// Une lecture relancée (Ctrl+R, après une opération) garde les marques et le curseur.
    /// </summary>
    private async Task ScanFlatAsync(string? keepPath)
    {
        _flatCts?.Cancel();
        var cts = _flatCts = new CancellationTokenSource();
        var ct = cts.Token;
        var version = ++_flatVersion;
        var marked = new HashSet<string>(MarkedItems.Select(i => i.FullPath), PathText.Comparer);
        var root = CurrentPath;
        bool showHidden = ShowHidden;
        int limit = FlatLimit;
        var found = new List<FileEntry>();
        long bytes = 0;

        void Flush(List<FileEntry> batch)
        {
            if (version != _flatVersion) return;
            foreach (var e in batch)
            {
                found.Add(e);
                bytes += e.Size;
                var item = MakeItem(e);
                item.IsMarked = marked.Contains(e.FullPath);
                Items.Add(item);
            }
            CurrentItem ??= Items.FirstOrDefault();
            Summary = L.F("Lecture des sous-dossiers… {0:N0} fichier(s) — {1}", found.Count, FileEntry.FormatSize(bytes));
        }

        _rawEntries = found;
        _flatNote = string.Empty;
        Items.Clear();
        CurrentItem = null;
        IsScanning = true;
        Summary = L.T("Lecture des sous-dossiers…");
        UpdateSelectionInfo();

        bool limited = false;
        string note = string.Empty;
        string? error = null;
        try
        {
            await Task.Run(async () =>
            {
                var batch = new List<FileEntry>();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await foreach (var e in ListFlat(root, showHidden, ct))
                {
                    batch.Add(e);
                    if (found.Count + batch.Count >= limit) { limited = true; break; }
                    if (batch.Count < 1000 && clock.ElapsedMilliseconds < 250) continue;
                    var ready = batch;
                    batch = [];
                    await Dispatcher.UIThread.InvokeAsync(() => Flush(ready));
                    clock.Restart();
                }
                await Dispatcher.UIThread.InvokeAsync(() => Flush(batch));
            }, ct);
        }
        catch (OperationCanceledException) { note = L.T(" — lecture arrêtée"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            note = L.T(" — lecture interrompue");
            error = ex.Message;
        }
        // Libère aussi la lecture du disque, restée en attente si la limite a été atteinte.
        cts.Cancel();
        if (version != _flatVersion) return; // une lecture plus récente a pris la main : rien de celle-ci ne doit la toucher

        _flatNote = limited ? L.F(" — limite de {0:N0} fichiers atteinte", limit) : note;
        IsScanning = false;
        ApplySort(keepPath, fullReset: true);
        if (error is not null) ErrorOccurred?.Invoke(this, error);
    }

    /// <summary>Réaffiche la liste (formats, couleurs ou police changés dans les Options).</summary>
    public void Rebuild() => ApplySort(CursorKey, fullReset: true);

    /// <summary>Repère de la ligne sous le curseur : en vue à plat, deux fichiers peuvent porter le même nom.</summary>
    private string? CursorKey => IsFlat ? CurrentItem?.FullPath : CurrentItem?.Name;

    private bool IsCursorKey(FileItemViewModel item, string key) =>
        string.Equals(IsFlat ? item.FullPath : item.Name, key, PathText.Comparison);

    private FileItemViewModel MakeItem(FileEntry e) =>
        new(e) { FolderText = IsFlat ? FlatListing.RelativeFolder(CurrentPath, e.FullPath) : string.Empty };

    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public ObservableCollection<BreadcrumbSegment> Segments { get; } = [];

    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _title = string.Empty;

    // ---- Verrou et nom personnel (clic droit sur l'onglet) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocked), nameof(DisplayTitle), nameof(LockTooltip))]
    private TwinPanel.Core.Settings.TabLock _lockMode;

    /// <summary>Nom choisi par l'utilisateur (vide : nom du dossier).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string _customName = string.Empty;

    /// <summary>
    /// Onglet de serveur montré dès le démarrage, en attente de sa connexion : « connexion… », ou « non connecté »
    /// si le serveur n'a pas répondu (clic droit → Reconnecter). Vide : onglet ordinaire.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    private string _waitingText = string.Empty;
    public bool IsWaiting => WaitingText.Length > 0;

    /// <summary>Met l'onglet en attente de son serveur ; le message s'affiche aussi en bas du panneau.</summary>
    public void SetWaiting(string pill, string summary)
    {
        WaitingText = pill;
        Summary = summary;
    }

    /// <summary>Dossier de l'onglet verrouillé.</summary>
    public string LockedPath { get; private set; } = string.Empty;
    public bool IsLocked => LockMode != TwinPanel.Core.Settings.TabLock.None;
    /// <summary>Verrouillé : le nom reste celui du dossier verrouillé, même si l'onglet affiche un autre dossier.</summary>
    public string DisplayTitle => TwinPanel.Core.Navigation.TabTitle.Display(CustomName, LockMode, LockedPath, Title);
    public string LockTooltip => LockMode switch
    {
        TwinPanel.Core.Settings.TabLock.Locked => L.F("Onglet verrouillé sur {0} (aller ailleurs ouvre un nouvel onglet)", LockedPath),
        TwinPanel.Core.Settings.TabLock.LockedAllowChange => L.F("Onglet verrouillé sur {0} (revient à ce dossier quand on le reprend)", LockedPath),
        _ => string.Empty,
    };

    /// <summary>Un onglet verrouillé a voulu aller ailleurs : le panneau ouvre ce dossier dans un nouvel onglet.</summary>
    public event EventHandler<string>? LockedNavigation;

    public void SetLock(TwinPanel.Core.Settings.TabLock mode, string? path = null)
    {
        LockedPath = mode == TwinPanel.Core.Settings.TabLock.None ? string.Empty : path ?? CurrentPath;
        LockMode = mode;
        OnPropertyChanged(nameof(LockTooltip));
        OnPropertyChanged(nameof(DisplayTitle));
    }

    /// <summary>withView : retient aussi le mode d'affichage (jeux d'onglets ; au démarrage, les onglets rouvrent en liste normale).</summary>
    public TwinPanel.Core.Settings.SavedTab ToSaved(bool withView = false) =>
        new(IsLocked ? LockedPath : CurrentPath, LockMode, CustomName, withView && IsFlat, withView && IsThumbnails);
    [ObservableProperty] private FileItemViewModel? _currentItem;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _selectionInfo = string.Empty;
    [ObservableProperty] private SortColumn _sortColumn;
    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;

    public IEnumerable<FileItemViewModel> MarkedItems => Items.Where(i => i.IsMarked);

    /// <summary>Vrai si l'onglet affiche l'intérieur d'une archive (lecture seule).</summary>
    public bool IsInsideArchive => !IsRemote && TwinPanel.Core.Archives.ArchivePath.TrySplit(CurrentPath, out _, out _);

    /// <summary>Vrai si l'onglet affiche un dossier sur un serveur (sftp://, ftp://…).</summary>
    public bool IsRemote => TwinPanel.Core.Remote.PanelRemotePath.TryParse(CurrentPath, out _);

    /// <summary>Éléments sur lesquels agir : les marqués, sinon l'élément sous le curseur ; jamais "..".</summary>
    public IReadOnlyList<FileEntry> TargetEntries
    {
        get
        {
            var marked = MarkedItems.Select(i => i.Entry).ToList();
            if (marked.Count == 0 && CurrentItem is { IsParentLink: false } cur) marked.Add(cur.Entry);
            return marked;
        }
    }

    // ---- Navigation -----------------------------------------------------------

    /// <summary>Affiche un dossier. En cas d'erreur, reste où l'on est et retourne false.</summary>
    public Task<bool> NavigateAsync(string path, string? selectName = null) => LoadAsync(path, selectName, recordHistory: true);

    /// <summary>Pas de relecture automatique pendant qu'un nom se tape dans la liste.</summary>
    public bool PauseAutoRefresh { get; set; }

    /// <summary>Juste avant et juste après une relecture : la vue garde sa position de défilement.</summary>
    public event Action? ListUpdating;
    public event Action? ListUpdated;

    private DateTime _nextPoll = DateTime.MinValue;
    private bool _polling;

    /// <summary>Relit ce dossier de serveur si c'est l'heure. Un grand dossier espace ses lectures, pour ne pas occuper la connexion.</summary>
    public async Task PollRemoteAsync(TimeSpan interval, bool force)
    {
        if (_polling || !IsRemote || IsFlat || IsLoading || PauseAutoRefresh || (!force && DateTime.UtcNow < _nextPoll)) return;
        _polling = true;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try { await LoadAsync(CurrentPath, CurrentItem?.Name, recordHistory: false, quiet: true); }
        finally
        {
            _polling = false;
            _nextPoll = DateTime.UtcNow + TimeSpan.FromTicks(Math.Max(interval.Ticks, clock.Elapsed.Ticks * PollSlowdown));
        }
    }

    private const int PollSlowdown = 10;

    /// <summary>Copie / déplacement : met le curseur sur le premier des éléments transférés encore affichés (panneau d'origine).</summary>
    public void PointAt(IEnumerable<string> names)
    {
        var name = TwinPanel.Core.Navigation.TransferCursor.FirstArrived(Items.Select(i => i.Name), names, IsRemote);
        if (name is not null && Items.FirstOrDefault(i => i.Name == name) is { } item) CurrentItem = item;
    }

    /// <summary>Fin d'une copie / d'un déplacement vers ce dossier : relu, curseur sur le premier élément arrivé.</summary>
    public async Task RevealArrivedAsync(IReadOnlyList<string> names)
    {
        if (CurrentPath.Length == 0 || IsFlat) return;
        // Une autre relecture (surveillance du dossier) peut finir après celle-ci : elle pointera aussi les arrivés.
        _arrived = names;
        _arrivedUntil = DateTime.UtcNow.AddSeconds(3);
        await LoadAsync(CurrentPath, CurrentItem?.Name, recordHistory: false, quiet: true);
        // Pas encore posé par une relecture (dossier déjà à jour, relecture dépassée) : on le pose maintenant.
        if (_arrived is not null && DateTime.UtcNow <= _arrivedUntil)
        {
            PointAt(names);
            HighlightArrived(names);
        }
    }

    private IDisposable? _arrivedTimer;

    /// <summary>Tous les éléments arrivés sont mis en valeur (pas marqués) ; ça s'efface après 5 s ou au premier clic.</summary>
    private void HighlightArrived(IEnumerable<string> names)
    {
        var set = new HashSet<string>(names, IsRemote ? StringComparer.Ordinal : PathText.Comparer);
        foreach (var item in Items) item.IsJustArrived = set.Contains(item.Name);
        _arrivedTimer?.Dispose();
        _arrivedTimer = DispatcherTimer.RunOnce(ClearArrivedHighlight, TimeSpan.FromSeconds(5));
    }

    public void ClearArrivedHighlight()
    {
        _arrivedTimer?.Dispose();
        _arrivedTimer = null;
        foreach (var item in Items) item.IsJustArrived = false;
    }

    private IReadOnlyList<string>? _arrived;
    private DateTime _arrivedUntil;

    /// <summary>Vrai juste avant que le curseur soit posé en arrivant dans un dossier : la liste le montre au milieu (lu et remis à faux par la vue).</summary>
    public bool CenterOnCursor { get; set; }

    public async Task RefreshAsync()
    {
        if (CurrentPath.Length == 0) return;
        if (IsFlat) { _ = ScanFlatAsync(CurrentItem?.FullPath); return; }
        int started = _loadVersion;
        if (await LoadAsync(CurrentPath, CurrentItem?.Name, recordHistory: false, quiet: true)) return;
        if (IsRemote) return; // serveur : le message d'erreur a déjà été donné à la navigation
        // Relecture dépassée par une autre (fin d'une copie, surveillance du dossier) : le dossier est toujours là.
        if (_loadVersion > started + 1 || Directory.Exists(CurrentPath)) return;

        // Le dossier a disparu : on remonte jusqu'au premier parent existant.
        var dir = new DirectoryInfo(CurrentPath).Parent;
        while (dir is not null && !dir.Exists) dir = dir.Parent;
        if (dir is not null) await NavigateAsync(dir.FullName);
    }

    public async Task GoUpAsync()
    {
        if (CurrentPath.Length == 0) return;
        if (_sources.Resolve(CurrentPath).GetParent(CurrentPath) is { } parent)
            await NavigateAsync(parent, selectName: Path.GetFileName(CurrentPath));
    }

    public async Task GoBackAsync()
    {
        // Le curseur revient sur l'élément qu'on avait laissé (souvent le dossier dans lequel on était entré).
        if (_history.Back() is { } p && !await LoadAsync(p, _history.CursorFor(p, CurrentPath), recordHistory: false)) _history.Undo(wasBack: true);
        UpdateHistoryFlags();
    }

    public async Task GoForwardAsync()
    {
        if (_history.Forward() is { } p && !await LoadAsync(p, _history.CursorFor(p, CurrentPath), recordHistory: false)) _history.Undo(wasBack: false);
        UpdateHistoryFlags();
    }

    /// <summary>Entrée / double-clic : ouvre le dossier ou lance le fichier.</summary>
    public async Task OpenCurrentAsync()
    {
        switch (CurrentItem)
        {
            case null: return;
            case { IsParentLink: true }: await GoUpAsync(); break;
            case { IsDirectory: true } d: await NavigateAsync(d.FullPath); break;
            case { } f when IsRemote && OpenArchivesWithEnter && TwinPanel.Core.Archives.ArchivePath.IsArchiveFile(f.Name) && RemoteArchiveOpener is not null:
                await RemoteArchiveOpener(f.Entry);
                break;
            case { } f when IsRemote:
                if (RemoteFileOpener is not null) await RemoteFileOpener(f.Entry);
                break;
            case { } f when OpenArchivesWithEnter && TwinPanel.Core.Archives.ArchivePath.IsArchiveFile(f.Name) && !IsInsideArchive:
                await NavigateAsync(f.FullPath); // Entrée sur une archive : on l'ouvre comme un dossier
                break;
            case { } f:
                try
                {
                    var path = f.FullPath;
                    if (TwinPanel.Core.Archives.ArchivePath.TrySplit(path, out var archive, out var inner))
                    {
                        path = await TwinPanel.Core.Archives.ArchiveService.ExtractToTempAsync(archive, inner);
                        ArchiveCopyOpened?.Invoke(path, archive, inner);
                    }
                    Services.ShellService.Open(path);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                           or IOException or UnauthorizedAccessException or FileOperationException)
                { ErrorOccurred?.Invoke(this, ex.Message); }
                break;
        }
    }

    public void MoveCursor(int delta)
    {
        if (Items.Count == 0) return;
        int i = CurrentItem is null ? 0 : Items.IndexOf(CurrentItem);
        CurrentItem = Items[Math.Clamp(i + delta, 0, Items.Count - 1)];
    }

    // ---- Marquage -----------------------------------------------------------------

    public void SetMark(FileItemViewModel item, bool marked)
    {
        if (item.IsParentLink) return;
        item.IsMarked = marked;
        UpdateSelectionInfo();
    }

    public void ToggleMark(FileItemViewModel item) => SetMark(item, !item.IsMarked);

    /// <summary>Marque (ou démarque) tous les éléments entre deux lignes incluses.</summary>
    public void MarkRange(FileItemViewModel from, FileItemViewModel to, bool marked)
    {
        int a = Items.IndexOf(from), b = Items.IndexOf(to);
        if (a < 0 || b < 0) return;
        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++)
            if (!Items[i].IsParentLink) Items[i].IsMarked = marked;
        UpdateSelectionInfo();
    }

    /// <summary>Num+ / Num- : marque ou démarque selon un masque (« *.jpg;*.png | *.tmp »). Les dossiers seulement si includeFolders.</summary>
    public int MarkByMask(string mask, bool marked, bool includeFolders)
    {
        var matcher = new MaskMatcher(mask);
        int n = 0;
        foreach (var i in Items)
        {
            if (i.IsParentLink || (i.IsDirectory && !includeFolders) || !matcher.IsMatch(i.Name)) continue;
            i.IsMarked = marked;
            n++;
        }
        UpdateSelectionInfo();
        return n;
    }

    public void MarkAll(bool marked)
    {
        foreach (var i in Items) if (!i.IsParentLink) i.IsMarked = marked;
        UpdateSelectionInfo();
    }

    /// <summary>Num* : inverse le marquage des fichiers (pas des dossiers, comme TC).</summary>
    public void InvertMarks()
    {
        foreach (var i in Items) if (!i.IsParentLink && !i.IsDirectory) i.IsMarked = !i.IsMarked;
        UpdateSelectionInfo();
    }

    /// <summary>Alt+Num+ : marque tous les fichiers ayant la même extension que l'élément courant.</summary>
    public void MarkSameExtension()
    {
        if (CurrentItem is not { IsDirectory: false } cur) return;
        foreach (var i in Items)
            if (!i.IsDirectory && string.Equals(i.Extension, cur.Extension, StringComparison.OrdinalIgnoreCase)) i.IsMarked = true;
        UpdateSelectionInfo();
    }

    /// <summary>Espace sur un dossier : calcule sa taille complète.</summary>
    public async Task ComputeFolderSizeAsync(FileItemViewModel item)
    {
        if (!item.IsDirectory || item.IsParentLink || item.IsComputingSize || IsRemote) return;
        item.IsComputingSize = true;
        try
        {
            var r = await FolderSize.CalculateAsync(item.FullPath, _sizeCts.Token);
            StoreFolderSize(item, r.Bytes);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ErrorOccurred?.Invoke(this, ex.Message); }
        finally
        {
            item.IsComputingSize = false;
            UpdateSelectionInfo();
            ScheduleSummary();
        }
    }

    public void UpdateSelectionInfo()
    {
        var marked = MarkedItems.ToList();
        if (marked.Count == 0) { SelectionInfo = string.Empty; return; }
        int files = marked.Count(i => !i.IsDirectory), dirs = marked.Count - files;
        long size = marked.Sum(i => i.EffectiveSize);
        SelectionInfo = L.F("Marqués : {0} fichier(s), {1} dossier(s) — {2}", files, dirs, FileEntry.FormatSize(size));
    }

    // ---- Tri / affichage ----------------------------------------------------------

    /// <summary>Même colonne : inverse l'ordre. Autre colonne : tri croissant.</summary>
    public void SortBy(SortColumn column)
    {
        if (SortColumn == column) SortDescending = !SortDescending;
        else { SortColumn = column; SortDescending = false; }
        ApplySort(keepName: CursorKey, fullReset: true);
    }

    partial void OnShowHiddenChanged(bool value) => _ = RefreshAsync();

    /// <summary>Sélectionne le premier élément dont le nom commence par le texte. Retourne false si rien ne correspond.</summary>
    public bool QuickFind(string prefix)
    {
        var match = Items.FirstOrDefault(e => !e.IsParentLink && e.Name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase));
        if (match is null) return false;
        CurrentItem = match;
        return true;
    }

    // ---- Interne ------------------------------------------------------------------------

    private async Task<bool> LoadAsync(string path, string? selectName, bool recordHistory, bool quiet = false)
    {
        IFileSource source;
        string normalized;
        try
        {
            source = _sources.Resolve(path);
            normalized = source.Normalize(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            if (!quiet) ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }

        // Onglet verrouillé : un autre dossier s'ouvre dans un nouvel onglet ; celui-ci ne bouge pas.
        if (LockMode == TwinPanel.Core.Settings.TabLock.Locked && CurrentPath.Length > 0
            && !string.Equals(normalized, CurrentPath, PathText.Comparison))
        {
            LockedNavigation?.Invoke(this, normalized);
            return recordHistory; // précédent / suivant : faux, pour que l'historique soit remis en place
        }

        if (IsFlat)
        {
            // Même dossier (Ctrl+R, fichier créé ou renommé) : on relit tout. Autre dossier : la vue à plat s'arrête.
            if (string.Equals(normalized, CurrentPath, PathText.Comparison))
            {
                _ = ScanFlatAsync(FlatCursor(selectName));
                return true;
            }
            CancelFlatScan();
        }

        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var version = ++_loadVersion;
        IsLoading = !quiet;
        try
        {
            var entries = await source.ListAsync(normalized, ShowHidden, cts.Token);
            if (version != _loadVersion) return false; // une navigation plus récente a pris la main

            bool samePath = string.Equals(normalized, CurrentPath, PathText.Comparison);
            if (!samePath)
            {
                // Changement de dossier : on arrête les calculs de taille en cours.
                _sizeCts.Cancel();
                _sizeCts = new CancellationTokenSource();
                // Pour Précédent / Suivant : l'élément sous le curseur dans le dossier qu'on quitte.
                if (!IsFlat && CurrentPath.Length > 0) _history.RememberCursor(CurrentPath, CurrentItem?.Name);
                if (IsFlat) LeaveFlat();
            }
            _rawEntries = entries;
            CurrentPath = normalized;
            Title = MakeTitle(normalized);
            if (!samePath)
            {
                RebuildSegments(normalized);
                Watch(source, normalized);
                FolderVisited?.Invoke(normalized);
            }
            if (recordHistory) _history.Push(normalized);
            UpdateHistoryFlags();
            ApplySort(selectName, fullReset: !samePath, keepView: quiet && samePath);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (TwinPanel.Core.Archives.ArchivePasswordException ex)
        {
            if (version != _loadVersion || quiet || ArchivePasswordAsker is null || !await ArchivePasswordAsker(ex.Archive)) return false;
            return await LoadAsync(path, selectName, recordHistory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            if (version == _loadVersion && !quiet) ErrorOccurred?.Invoke(this, ex.Message);
            return false;
        }
        finally
        {
            if (version == _loadVersion) IsLoading = false;
        }
    }

    private void ApplySort(string? keepName, bool fullReset, bool keepView = false)
    {
        if (keepView) ListUpdating?.Invoke();
        var sorted = EntrySorter.Sort(_rawEntries, SortColumn, SortDescending, KnownFolderSize);
        // Nom sous le curseur AVANT la mise à jour : quand la ligne courante est remplacée (fichier modifié),
        // la grille déplace sa sélection sur la ligne suivante ; on remet le curseur au bon endroit ensuite.
        keepName ??= CursorKey;
        int oldIndex = CurrentItem is null ? -1 : Items.IndexOf(CurrentItem);
        List<string> oldKeys = fullReset ? [] : [.. Items.Select(i => IsFlat ? i.FullPath : i.Name)];

        if (fullReset)
        {
            // Les marques suivent le chemin : un tri ou une relecture ne les perd pas.
            var marked = new HashSet<string>(MarkedItems.Select(i => i.FullPath), PathText.Comparer);
            Items.Clear();
            foreach (var e in sorted)
            {
                var item = MakeItem(e);
                item.IsMarked = marked.Contains(e.FullPath);
                Items.Add(item);
            }
        }
        else
        {
            SyncItems(sorted);
        }

        UpdateSummary();
        UpdateSelectionInfo();
        StartMediaColumns();
        StartFolderSizes();
        StartShellIcons();
        var keep = keepName is null ? null : Items.FirstOrDefault(e => IsCursorKey(e, keepName));
        // Fin d'une copie / d'un déplacement vers ce dossier : le curseur va sur le premier élément arrivé.
        if (_arrived is { } arrived)
        {
            if (DateTime.UtcNow > _arrivedUntil) _arrived = null;
            else if (!fullReset && TwinPanel.Core.Navigation.TransferCursor.FirstArrived(Items.Select(i => i.Name), arrived, IsRemote) is { } first)
            {
                keep = Items.First(i => i.Name == first);
                HighlightArrived(arrived);
                _arrived = null;
            }
        }
        // Arrivée dans un dossier sur un élément précis (Précédent, dossier parent…) : la vue le montre au milieu.
        CenterOnCursor = fullReset && keep is not null;
        // Même dossier relu, la ligne du curseur a disparu (déplacée, supprimée) : le curseur reste à sa place,
        // sur la ligne qui la suivait, au lieu de remonter en haut de la liste.
        if (keep is null && !fullReset && oldIndex >= 0)
        {
            // Plusieurs lignes ont pu partir ensemble : on prend la première ligne restante après le curseur.
            var present = new Dictionary<string, FileItemViewModel>();
            foreach (var i in Items) present.TryAdd(IsFlat ? i.FullPath : i.Name, i);
            if (TwinPanel.Core.Navigation.TransferCursor.NextRemaining(oldKeys, oldIndex, present.ContainsKey) is { } next)
                keep = present[next];
        }
        if (fullReset || CurrentItem is null || !Items.Contains(CurrentItem)
            || (keep is not null && !ReferenceEquals(CurrentItem, keep)))
            CurrentItem = keep ?? Items.FirstOrDefault();
        CenterOnCursor = false; // lu par la vue pendant le changement de curseur, pas au-delà
        if (keepView) ListUpdated?.Invoke();
    }

    /// <summary>
    /// Met à jour la liste sans la vider : les éléments inchangés restent les mêmes objets
    /// (marquage et taille calculée conservés), un fichier modifié garde son marquage.
    /// </summary>
    private void SyncItems(IReadOnlyList<FileEntry> target)
    {
        var markedPaths = new HashSet<string>(MarkedItems.Select(i => i.FullPath), PathText.Comparer);
        var wanted = new HashSet<FileEntry>(target);
        for (int i = Items.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Items[i].Entry)) Items.RemoveAt(i);

        for (int i = 0; i < target.Count; i++)
        {
            if (i < Items.Count && Items[i].Entry.Equals(target[i])) continue;
            var item = MakeItem(target[i]);
            item.IsMarked = markedPaths.Contains(target[i].FullPath);
            Items.Insert(i, item);
        }
        while (Items.Count > target.Count) Items.RemoveAt(Items.Count - 1);
    }

    private void Watch(IFileSource source, string path)
    {
        DisposeWatcher();
        if (!source.SupportsWatching) return;
        try
        {
            var w = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
            };
            FileSystemEventHandler onChange = (_, e) =>
            {
                // Nos propres fichiers temporaires de copie ne doivent pas faire clignoter la liste.
                if (e.Name is null || !e.Name.EndsWith(".twinpanel-part", StringComparison.Ordinal)) ScheduleRefresh();
            };
            w.Changed += onChange;
            w.Created += onChange;
            w.Deleted += onChange;
            w.Renamed += (_, _) => ScheduleRefresh();
            w.Error += (_, _) => ScheduleRefresh();
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Certains lecteurs (réseau, CD) ne supportent pas la surveillance : Ctrl+R reste disponible.
        }
    }

    private void ScheduleRefresh() => Dispatcher.UIThread.Post(() =>
    {
        if (IsFlat) return; // vue à plat : pas de relecture automatique (Ctrl+R, et après chaque opération)
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    });

    private void RebuildSegments(string path)
    {
        Segments.Clear();
        foreach (var s in Breadcrumb.Split(path)) Segments.Add(s);
    }

    private void UpdateHistoryFlags()
    {
        CanGoBack = _history.CanGoBack;
        CanGoForward = _history.CanGoForward;
    }

    private void UpdateSummary()
    {
        int files = 0, dirs = 0;
        long size = 0;
        bool computing = false;
        foreach (var e in Items)
        {
            if (e.IsParentLink) continue;
            if (e.IsDirectory)
            {
                dirs++;
                // Taille des dossiers déjà calculée (automatique ou à la demande) : comptée dans le total.
                size += e.ComputedSize ?? 0;
                computing |= e.IsComputingSize;
            }
            else { files++; size += e.Entry.Size; }
        }
        var total = FileEntry.FormatSize(size) + (computing ? " …" : string.Empty);
        Summary = IsFlat
            ? L.F("{0:N0} fichier(s) — {1}{2}", files, total, _flatNote)
            : L.F("{0} dossier(s), {1} fichier(s) — {2}", dirs, files, total);
    }

    /// <summary>Titre de l'onglet : nom du dossier ; disque du PC : lettre devant (« C: Documents », « C: » à la racine).</summary>
    public static string MakeTitle(string path) => TwinPanel.Core.Navigation.TabTitle.FromPath(path);

    private void DisposeWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        CancelFlatScan();
        _sizeCts.Cancel();
        _folderCts.Cancel();
        _refreshDebounce.Stop();
        DisposeWatcher();
        GC.SuppressFinalize(this);
    }
}
