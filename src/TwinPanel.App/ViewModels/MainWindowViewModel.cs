// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using Avalonia;
using Avalonia.Styling;
using TwinPanel.App.Services;
using TwinPanel.Core;
using TwinPanel.Core.Archives;
using TwinPanel.Core.Compare;
using TwinPanel.Core.Customization;
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Sources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TwinPanel.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly SettingsStore _store;
    private AppSettings _settings = new();
    private RenameLog? _lastRename;
    private readonly ToolLauncher _tools;
    private readonly Avalonia.Threading.DispatcherTimer _quickViewTimer;
    private string? _quickViewPath;
    private readonly Avalonia.Threading.DispatcherTimer _driveTimer;

    public MainWindowViewModel(IDialogService dialogs, SettingsStore store, FileSourceRegistry sources)
    {
        _dialogs = dialogs;
        _store = store;
        _recent = new RecentFolders(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "history.json"));
        _transfers = new TransferQueueStore(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "transfers.json"));
        _syncProfiles = new SyncProfileStore(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "sync-profiles.json"));
        Left = new FilePanelViewModel(sources);
        Right = new FilePanelViewModel(sources);

        foreach (var p in new[] { Left, Right })
        {
            p.Activated += (s, _) => ActivePanel = (FilePanelViewModel)s!;
            p.ErrorOccurred += async (_, msg) => await _dialogs.ShowErrorAsync(L.T("Erreur"), msg);
            // Barre des lecteurs : une connexion cliquée s'ouvre dans CE panneau.
            p.ConnectionRequested += async (s, profile) =>
            {
                ActivePanel = (FilePanelViewModel)s!;
                await ConnectAsync(profile);
            };
        }
        // Clé USB branchée ou retirée : la barre des lecteurs se met à jour (lecture en arrière-plan).
        _driveTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _driveTimer.Tick += (_, _) => { Left.RefreshDrives(); Right.RefreshDrives(); };
        _driveTimer.Start();
        _activePanel = Left;
        Left.IsActive = true;

        _tools = new ToolLauncher(dialogs, () => _settings);
        // Vue rapide : on suit l'élément sous le curseur du panneau actif (petit délai pour ne pas tout charger en défilant).
        _quickViewTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _quickViewTimer.Tick += async (_, _) => await UpdateQuickViewAsync();

        Operations.JobDismissed += (_, job) => _transfers.Remove(job.Saved);
        Operations.AttentionNeeded += async (_, message) => await _dialogs.ShowErrorAsync(L.T("Plus de place"), message);
        Operations.JobFinished += async (_, job) =>
        {
            if (job.State is JobState.Done or JobState.Cancelled) _transfers.Remove(job.Saved); // un échec reste à reprendre
            await RefreshAll();
            StatusMessage = $"{job.Title} : {job.Detail}";
        };
    }

    /// <summary>Copies, déplacements et suppressions en cours (panneau de progression).</summary>
    public OperationManager Operations { get; } = new();

    public FilePanelViewModel Left { get; }
    public FilePanelViewModel Right { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OtherPanel))]
    private FilePanelViewModel _activePanel;

    [ObservableProperty] private string _statusMessage = L.T("Prêt");
    [ObservableProperty] private string _commandLine = string.Empty;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private string _themeLabel = L.T("Système");

    public FilePanelViewModel OtherPanel => ActivePanel == Left ? Right : Left;
    public IReadOnlyList<FavoriteFolder> Favorites => _settings.Favorites;
    public AppSettings Settings => _settings;

    partial void OnActivePanelChanged(FilePanelViewModel? oldValue, FilePanelViewModel newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        newValue.IsActive = true;
        if (newValue.IsQuickView) StopQuickView();
    }

    // ---- Démarrage / fermeture ----------------------------------------------------------

    /// <summary>Charge les réglages (synchrone, avant l'affichage de la fenêtre).</summary>
    public void LoadSettings()
    {
        _isFirstStart = !File.Exists(_store.FilePath);
        _settings = _store.Load();
        _recent.Capacity = Math.Clamp(_settings.Appearance.RecentFoldersCount, 5, 200); // avant la lecture, qui s'y arrêterait
        _recent.Load();
        _transfers.Load();
        _syncProfiles.Load();
        ShowHidden = _settings.ShowHidden;   // les panneaux n'ont pas encore d'onglet : rien n'est rechargé
        OnPropertyChanged(nameof(Favorites));
        ApplyTheme(_settings.Theme);
        ApplyCustomization(refreshPanels: false);
    }

    private bool _isFirstStart;

    /// <summary>Tout premier démarrage sur cette machine : proposer de reprendre la configuration d'une autre.</summary>
    public async Task OfferFirstStartSyncAsync()
    {
        if (!_isFirstStart) return;
        _isFirstStart = false;
        if (await _dialogs.ConfirmAsync(L.T("Bienvenue dans TwinPanel"),
                L.T("Tu utilises déjà TwinPanel sur une autre machine ?\nRécupérer ma configuration : connexions, clés, mots de passe, thème et raccourcis.\n\nTu pourras aussi le faire plus tard dans Options → Synchronisation.")))
            await OpenOptions("Sync");
    }

    /// <summary>Ouvre les onglets enregistrés (après l'affichage de la fenêtre).</summary>
    public async Task InitializeAsync()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Les onglets sur un serveur sont mis de côté : rouverts après l'affichage (il faut se reconnecter).
        var (left, leftRemote) = SplitRemoteTabs(_settings.Left);
        var (right, rightRemote) = SplitRemoteTabs(_settings.Right);
        if (!_settings.Startup.RestoreTabs)
        {
            // Options → Démarrage : dossiers fixes au lieu des onglets de la dernière fois.
            left = new PanelState { Tabs = [_settings.Startup.LeftPath], SortColumn = left.SortColumn, SortDescending = left.SortDescending };
            right = new PanelState { Tabs = [_settings.Startup.RightPath], SortColumn = right.SortColumn, SortDescending = right.SortDescending };
        }
        await Left.InitializeAsync(left, ShowHidden, home);
        await Right.InitializeAsync(right, ShowHidden, home);
        if (_settings.Startup.RestoreTabs) await RestoreRemoteTabsAsync(leftRemote, rightRemote);
        Left.ShowTree = _settings.LeftTreeVisible;
        Right.ShowTree = _settings.RightTreeVisible;
        // Seulement maintenant : les onglets rouverts au démarrage ne sont pas des visites.
        FolderTabViewModel.FolderVisited = RecordVisit;
        ShowStartupTip();
    }

    public void SaveSettings(double width, double height, bool maximized)
    {
        SaveRecent();
        _transfers.Save();
        _settings.Left = WithUnopenedTabs(Left, WithSavedRemoteTabs(Left.CaptureState()));
        _settings.Right = WithUnopenedTabs(Right, WithSavedRemoteTabs(Right.CaptureState()));
        _settings.ShowHidden = ShowHidden;
        _settings.LeftTreeVisible = Left.ShowTree;
        _settings.RightTreeVisible = Right.ShowTree;
        if (!maximized) { _settings.WindowWidth = width; _settings.WindowHeight = height; }
        _settings.WindowMaximized = maximized;
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(L.F("Réglages non sauvegardés : {0}", ex.Message));
        }
    }

    // ---- Navigation (panneau actif) -----------------------------------------------------------

    [RelayCommand] private Task GoBack() => ActivePanel.Current.GoBackAsync();
    [RelayCommand] private Task GoForward() => ActivePanel.Current.GoForwardAsync();
    [RelayCommand] private Task GoUp() => ActivePanel.Current.GoUpAsync();
    [RelayCommand] private Task NewTab() => ActivePanel.NewTab();
    [RelayCommand] private void CloseTab() => ActivePanel.CloseTab(null);
    [RelayCommand] private Task GoHome() => ActivePanel.Current.NavigateAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    [RelayCommand] private Task GoRoot() =>
        TwinPanel.Core.Remote.PanelRemotePath.TryParse(ActivePanel.Current.CurrentPath, out var remote)
            ? ActivePanel.Current.NavigateAsync(remote.WithPath("/").ToString())
            : Path.GetPathRoot(ActivePanel.Current.CurrentPath) is { Length: > 0 } root ? ActivePanel.Current.NavigateAsync(root) : Task.CompletedTask;

    [RelayCommand]
    private async Task RefreshAll()
    {
        Left.RefreshDrives();
        Right.RefreshDrives();
        await Left.Current.RefreshAsync();
        await Right.Current.RefreshAsync();
    }

    /// <summary>Ctrl+U : échange les dossiers des deux panneaux.</summary>
    [RelayCommand]
    private async Task SwapPanels()
    {
        var l = Left.Current.CurrentPath;
        await Left.Current.NavigateAsync(Right.Current.CurrentPath);
        await Right.Current.NavigateAsync(l);
    }

    /// <summary>Le panneau inactif affiche le même dossier que le panneau actif.</summary>
    [RelayCommand]
    private Task SameFolderInOther() => OtherPanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath);

    /// <summary>Ouvre le dossier sélectionné dans l'autre panneau.</summary>
    [RelayCommand]
    private Task OpenInOther() =>
        ActivePanel.Current.CurrentItem is { IsDirectory: true } d
            ? OtherPanel.Current.NavigateAsync(d.FullPath)
            : OtherPanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath);

    [RelayCommand]
    private void SortBy(string column)
    {
        if (Enum.TryParse<SortColumn>(column, out var c)) ActivePanel.Current.SortBy(c);
    }

    partial void OnShowHiddenChanged(bool value)
    {
        Left.SetShowHidden(value);
        Right.SetShowHidden(value);
    }

    [RelayCommand] private void ToggleHidden() => ShowHidden = !ShowHidden;

    // ---- Thème -------------------------------------------------------------------------

    [RelayCommand]
    private void CycleTheme()
    {
        var next = _settings.Theme switch { "System" => "Light", "Light" => "Dark", _ => "System" };
        ApplyTheme(next);
    }

    private void ApplyTheme(string theme)
    {
        _settings.Theme = theme;
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        ThemeLabel = theme switch { "Light" => L.T("Clair"), "Dark" => L.T("Sombre"), _ => L.T("Système") };
    }

    // ---- Favoris (Ctrl+D) --------------------------------------------------------------------

    [RelayCommand]
    private async Task AddFavorite()
    {
        var path = ActivePanel.Current.CurrentPath;
        if (_settings.Favorites.Any(f => string.Equals(f.Path, path, PathText.Comparison)))
        {
            StatusMessage = L.T("Ce dossier est déjà dans les favoris.");
            return;
        }
        var name = await _dialogs.PromptAsync(L.T("Ajouter aux favoris"), L.T("Nom du favori :"), ActivePanel.Current.Title);
        if (string.IsNullOrWhiteSpace(name)) return;
        _settings.Favorites.Add(new FavoriteFolder { Name = name.Trim(), Path = path });
        OnPropertyChanged(nameof(Favorites));
        StatusMessage = L.F("« {0} » ajouté aux favoris.", name.Trim());
    }

    [RelayCommand]
    private void RemoveFavorite(FavoriteFolder? fav)
    {
        if (fav is null) return;
        _settings.Favorites.Remove(fav);
        OnPropertyChanged(nameof(Favorites));
    }

    [RelayCommand]
    private Task OpenFavorite(FavoriteFolder? fav) =>
        fav is null ? Task.CompletedTask : ActivePanel.Current.NavigateAsync(fav.Path);

    // ---- Ligne de commande ------------------------------------------------------------------

    /// <summary>Sous Linux, la commande s'ouvre dans un terminal qui reste ouvert : pas de Maj+Entrée.</summary>
    public string CommandLineHint => OperatingSystem.IsWindows()
        ? L.T("Commande (Entrée : exécuter, Maj+Entrée : garder la console ouverte)")
        : L.T("Commande (Entrée : exécuter)");

    /// <summary>
    /// "cd chemin" change de dossier dans le panneau actif ; toute autre commande est passée à cmd.exe
    /// dans le dossier actif. keepOpen (Maj+Entrée) garde la fenêtre de console ouverte.
    /// </summary>
    public async Task RunCommandLineAsync(bool keepOpen)
    {
        var cmd = CommandLine.Trim();
        if (cmd.Length == 0) return;
        var cwd = ActivePanel.Current.CurrentPath;
        if (ActivePanel.Current.IsRemote)
        {
            StatusMessage = L.T("La ligne de commande travaille sur ton PC : place d'abord le panneau actif sur un dossier local.");
            return;
        }

        if (cmd.Equals("cd", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            var target = cmd.Length > 2 ? cmd[2..].Trim().Trim('"') : string.Empty;
            if (target.Length == 0) { StatusMessage = cwd; CommandLine = string.Empty; return; }
            if (OperatingSystem.IsWindows() && target.StartsWith("/d ", StringComparison.OrdinalIgnoreCase)) target = target[3..].Trim().Trim('"');
            target = Environment.ExpandEnvironmentVariables(target);
            if (!OperatingSystem.IsWindows() && (target == "~" || target.StartsWith("~/", StringComparison.Ordinal)))
                target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), target[1..].TrimStart('/'));
            if (!Path.IsPathRooted(target)) target = Path.Combine(cwd, target);
            if (await ActivePanel.Current.NavigateAsync(target)) CommandLine = string.Empty;
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // La commande est tapée par l'utilisateur lui-même : c'est volontairement un vrai shell.
                var psi = new ProcessStartInfo("cmd.exe") { WorkingDirectory = cwd, UseShellExecute = true };
                // /S + guillemets autour de toute la commande : cmd.exe enlève seulement ces deux guillemets-là,
                // ceux de la commande restent intacts (sinon « start "titre" … » était abîmé et ne lançait rien).
                psi.Arguments = (keepOpen ? "/S /K \"" : "/S /C \"") + cmd + "\"";
                Process.Start(psi)?.Dispose();
            }
            else if (IsTerminalEnabled)
            {
                // Linux : un onglet du terminal intégré, pour voir le résultat et répondre si la commande pose une question.
                // La commande est tapée dans le shell comme un collage : les caractères de contrôle sont retirés.
                if (await OpenLocalTerminalAsync(_settings.Terminal.IntegratedShell, cwd) is not { Session: { } session }) return;
                session.Send(TwinPanel.Core.Terminal.TerminalCommands.Input(cmd, bracketedPaste: false));
                Terminal.RequestFocus();
            }
            else
            {
                // Terminal intégré désactivé : le terminal du bureau, qui reste ouvert après la commande.
                var (exe, args) = TerminalCommand.BuildRun(_settings.Terminal, cmd);
                var psi = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);
                Process.Start(psi)?.Dispose();
            }
            CommandLine = string.Empty;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync(L.T("Commande"), ex.Message);
        }
    }

    // ---- F3 / F4 / Maj+F4 ----------------------------------------------------------------

    /// <summary>F3 : visionneuse intégrée (ou visionneuse externe si elle est choisie dans les réglages).</summary>
    [RelayCommand]
    private async Task View()
    {
        if (ActivePanel.Current.CurrentItem is not { IsDirectory: false } f) return;
        if (await RemoteViewAsync(f.Entry)) return;
        if (ActivePanel.Current.IsInsideArchive)
        {
            // Dans une archive : on extrait le fichier dans un dossier temporaire pour le montrer.
            if (await MaterializeAsync(f.FullPath) is not { } temp) return;
            if (!await _tools.TryExternalViewAsync(temp)) _dialogs.ShowViewer([temp], 0);
            return;
        }
        if (await _tools.TryExternalViewAsync(f.FullPath)) return;

        // N / P dans la visionneuse : fichiers du dossier, dans l'ordre affiché.
        var files = ActivePanel.Current.Items.Where(i => !i.IsDirectory).Select(i => i.FullPath).ToList();
        _dialogs.ShowViewer(files, Math.Max(0, files.IndexOf(f.FullPath)));
    }

    /// <summary>Alt+F3 : ouvrir avec l'application associée par Windows.</summary>
    [RelayCommand]
    private async Task OpenWithDefaultApp()
    {
        if (ActivePanel.Current.CurrentItem is not { IsParentLink: false } f) return;
        if (ActivePanel.Current.IsRemote)
        {
            if (!f.IsDirectory) await OpenRemoteFileAsync(f.Entry);
            return;
        }
        await RunShellAsync(() => ShellService.Open(f.FullPath));
    }

    /// <summary>F4 : éditeur choisi (Bloc-notes par défaut, repli automatique s'il est introuvable).</summary>
    [RelayCommand]
    private async Task Edit()
    {
        if (ActivePanel.Current.CurrentItem is not { IsDirectory: false } f) return;
        if (await RemoteEditAsync(f.Entry, internalEditor: false)) return;
        var path = f.FullPath;
        if (ActivePanel.Current.IsInsideArchive)
        {
            if (!await _dialogs.ConfirmAsync("Archive", L.T("Ce fichier est dans une archive : il va être extrait dans un dossier temporaire.\n") +
                                                        L.T("Les modifications ne seront PAS remises dans l'archive. Continuer ?"))) return;
            if (await MaterializeAsync(path) is not { } temp) return;
            path = temp;
        }
        await _tools.EditAsync(path);
    }

    /// <summary>Menu Outils : ouvrir dans l'éditeur intégré, quel que soit l'éditeur choisi pour F4.</summary>
    [RelayCommand]
    private async Task EditInternal()
    {
        if (ActivePanel.Current.CurrentItem is not { IsDirectory: false } f) return;
        if (await RemoteEditAsync(f.Entry, internalEditor: true)) return;
        if (ActivePanel.Current.IsInsideArchive)
        {
            if (!await _dialogs.ConfirmAsync("Archive", L.T("Ce fichier est dans une archive : il va être extrait dans un dossier temporaire.\n") +
                                                        L.T("Les modifications ne seront PAS remises dans l'archive. Continuer ?"))) return;
            if (await MaterializeAsync(f.FullPath) is { } temp) await _dialogs.ShowEditorAsync(temp);
            return;
        }
        await _dialogs.ShowEditorAsync(f.FullPath);
    }

    /// <summary>Maj+F4 : crée un fichier texte et l'ouvre dans l'éditeur.</summary>
    [RelayCommand]
    private async Task NewFile()
    {
        if (await RefuseInArchiveAsync() || await RemoteNewFileAsync()) return;
        var name = await _dialogs.PromptAsync(L.T("Nouveau fichier"), L.T("Nom du fichier :"), L.T("nouveau.txt"), selectLength: L.T("nouveau.txt").Length - 4);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            var path = FileOperations.CreateFile(ActivePanel.Current.CurrentPath, name);
            await ActivePanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath, selectName: Path.GetFileName(path));
            await _tools.EditAsync(path);
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(L.T("Erreur"), ex.Message);
        }
    }

    // ---- F5 / F6 -------------------------------------------------------------------------

    [RelayCommand] private Task Copy() => TransferAsync(move: false);
    [RelayCommand] private Task Move() => TransferAsync(move: true);

    private Task TransferAsync(bool move) => TransferAsync(move, ActivePanel.Current.TargetEntries, null, fromPanel: true);

    /// <summary>
    /// Glisser-déposer : éléments lâchés sur un dossier (même panneau, autre panneau, onglet, lecteur).
    /// source null : fichiers venus d'un autre programme (Explorateur…). Même fenêtre de confirmation que F5 / F6.
    /// </summary>
    public Task DropAsync(FilePanelViewModel? source, IReadOnlyList<FileEntry> items, string targetDir, bool move)
    {
        source?.Activate();
        return TransferAsync(move, items, targetDir, fromPanel: source is not null);
    }

    /// <summary>Dossier où vont des fichiers lâchés sur un bouton de la barre des lecteurs (null : connexion fermée).</summary>
    public string? DriveDropTarget(DriveButtonViewModel button)
    {
        if (button.Kind == DriveButtonKind.Drive) return button.Key;
        if (button.OpenAlias is { } alias && _remote?.Find(alias) is { } session) return session.StartPath.ToString();
        StatusMessage = L.T("Ouvre d'abord la connexion (clic sur son bouton), puis lâche les fichiers dessus.");
        return null;
    }

    private async Task TransferAsync(bool move, IReadOnlyList<FileEntry> items, string? destination, bool fromPanel)
    {
        if (items.Count == 0) return;
        var (go, forced) = await CheckSameNamesAsync(items, move ? L.T("Déplacer") : L.T("Copier"));
        if (!go) return;
        var defaultDestination = destination ?? OtherPanel.Current.CurrentPath;
        bool intoArchive = ArchivePath.TrySplit(defaultDestination, out _, out _);
        if (await RemoteTransferAsync(move, items, destination, fromPanel, forced)) return;

        if (fromPanel && ArchivePath.TrySplit(ActivePanel.Current.CurrentPath, out var archive, out var innerBase))
        {
            if (intoArchive)
            {
                await _dialogs.ShowErrorAsync("Archive", L.T("D'une archive à l'autre : extrais d'abord les fichiers (F5 vers un dossier), puis ajoute-les."));
                return;
            }
            await ExtractFromArchiveAsync(archive, innerBase, items, defaultDestination, move);
            return;
        }

        var verb = move ? L.T("Déplacer") : L.T("Copier");
        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        var request = await _dialogs.AskTransferAsync(verb, what, defaultDestination);
        if (request is null || request.Destination.Length == 0) return;

        var dest = request.Destination;
        if (!Path.IsPathRooted(dest)) dest = Path.Combine(ActivePanel.Current.CurrentPath, dest);
        if (ArchivePath.TrySplit(dest, out var targetArchive, out var targetInner))
        {
            await AddToArchiveAsync(targetArchive, targetInner, items, move, forced ?? request.Policy);
            return;
        }
        if (!Directory.Exists(dest))
        {
            if (!await _dialogs.ConfirmAsync(L.T("Dossier inexistant"), L.F("Le dossier{0}{1}{2}n'existe pas. Le créer ?", Environment.NewLine, dest, Environment.NewLine))) return;
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                await _dialogs.ShowErrorAsync(L.T("Erreur"), ex.Message);
                return;
            }
        }

        var sources = items.Select(i => i.FullPath).ToList();
        var policy = forced ?? request.Policy ?? ConflictPolicy.Overwrite;
        if (forced is null && request.Policy is null)
        {
            var conflicts = FileOperations.FindConflicts(sources, dest);
            if (conflicts.Count > 0)
            {
                if (await _dialogs.AskConflictAsync(conflicts) is not { } chosen) return;
                policy = chosen;
            }
        }

        ActivePanel.Current.MarkAll(false);
        var title = L.F("{0} de {1} vers {2}", (move ? L.T("Déplacement") : L.T("Copie")), what, dest);
        Operations.Start(new OperationJobViewModel(title, transfer: true, (progress, ct, pause) => move
            ? FileOperations.MoveAsync(sources, dest, policy, progress, ct, pause)
            : FileOperations.CopyAsync(sources, dest, policy, progress, ct, pause)));
    }

    [RelayCommand]
    private void DismissJob(OperationJobViewModel? job)
    {
        if (job is not null) Operations.Dismiss(job);
    }

    [RelayCommand]
    private void ResumeJob(OperationJobViewModel? job)
    {
        if (job is not null) Operations.Resume(job);
    }

    // ---- Maj+F6 / Ctrl+M -----------------------------------------------------------------------

    /// <summary>Maj+F6 : renommer l'élément courant (le nom sans l'extension est présélectionné).</summary>
    [RelayCommand]
    private async Task Rename()
    {
        if (await ArchiveRenameAsync() || await RemoteRenameAsync()) return;
        if (ActivePanel.Current.CurrentItem is not { IsParentLink: false } item) return;
        int selectLength = item.IsDirectory ? item.Name.Length : Path.GetFileNameWithoutExtension(item.Name).Length;
        if (selectLength == 0) selectLength = item.Name.Length;

        var newName = await AskNewNameAsync(item.Name, selectLength);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;
        try
        {
            var path = FileOperations.Rename(item.FullPath, newName);
            await ActivePanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath, selectName: Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(L.T("Renommer"), ex.Message);
        }
    }

    /// <summary>Nom déjà tapé dans la liste (clic sur le nom) : il prend la place de la question.</summary>
    private string? _typedName;

    private Task<string?> AskNewNameAsync(string currentName, int selectLength)
    {
        if (_typedName is not { } typed) return _dialogs.PromptAsync(L.T("Renommer"), L.T("Nouveau nom :"), currentName, selectLength);
        _typedName = null;
        return Task.FromResult<string?>(typed);
    }

    /// <summary>Renomme avec les mêmes contrôles que Maj+F6, sur le PC, sur un serveur ou dans une archive.</summary>
    public async Task RenameInlineAsync(string newName)
    {
        _typedName = newName;
        try { await Rename(); }
        finally { _typedName = null; }
    }

    /// <summary>Ctrl+M : renommage multiple des éléments marqués (ou de tout le dossier si rien n'est marqué).</summary>
    [RelayCommand]
    private async Task MultiRenameFiles()
    {
        if (await RefuseInArchiveAsync()) return;
        var tab = ActivePanel.Current;
        Func<string, string, CancellationToken, Task>? move = null;
        if (tab.IsRemote)
        {
            if (SessionOf(tab) is not { } session) { await _dialogs.ShowErrorAsync(L.T("Renommage multiple"), L.T("La connexion de ce panneau n'est plus ouverte.")); return; }
            move = (from, to, ct) => session.UseAsync(fs => fs.RenameAsync(RemotePart(from), RemotePart(to), overwrite: false, ct), ct);
        }
        var entries = tab.MarkedItems.Select(i => i.Entry).ToList();
        if (entries.Count == 0) entries = tab.Items.Where(i => !i.IsParentLink).Select(i => i.Entry).ToList();
        if (entries.Count == 0) return;

        var log = await _dialogs.ShowMultiRenameAsync(entries, move);
        if (log is null) return;
        _lastRename = log;
        _lastRenameMove = move;
        tab.MarkAll(false);
        await RefreshAll();
        StatusMessage = L.F("{0} élément(s) renommé(s). Ctrl+Z pour annuler.", log.Items.Count);
    }

    /// <summary>Renommage multiple fait sur un serveur : comment l'annuler (null : disque du PC).</summary>
    private Func<string, string, CancellationToken, Task>? _lastRenameMove;

    /// <summary>Ctrl+Z : annule le dernier renommage multiple.</summary>
    [RelayCommand]
    private async Task UndoRename()
    {
        if (_lastRename is null) { StatusMessage = L.T("Rien à annuler."); return; }
        try
        {
            if (_lastRenameMove is { } move) await MultiRename.UndoAsync(_lastRename, move);
            else MultiRename.Undo(_lastRename);
            StatusMessage = L.F("Renommage annulé ({0} élément(s)).", _lastRename.Items.Count);
            _lastRename = null;
            await RefreshAll();
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(L.T("Annuler le renommage"), ex.Message);
        }
    }

    // ---- F7 / F8 -------------------------------------------------------------------------

    [RelayCommand]
    private async Task MakeDirectory()
    {
        if (await ArchiveMakeDirectoryAsync() || await RemoteMakeDirectoryAsync()) return;
        var name = await _dialogs.PromptAsync(L.T("Nouveau dossier"), L.T("Nom du dossier :"));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            FileOperations.CreateDirectory(ActivePanel.Current.CurrentPath, name);
            await ActivePanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath, selectName: name.Trim());
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(L.T("Erreur"), ex.Message);
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (await ArchiveDeleteAsync() || await RemoteDeleteAsync()) return;
        var items = ActivePanel.Current.TargetEntries;
        if (items.Count == 0) return;

        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        var recycle = (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && _settings.Behavior.UseRecycleBin;
        var msg = recycle ? L.F("Envoyer {0} à la corbeille ?", what) : L.F("Supprimer DÉFINITIVEMENT {0} ?", what);
        // Options → Comportement : la confirmation peut être retirée pour la corbeille, jamais pour une suppression définitive.
        if ((_settings.Behavior.ConfirmDelete || !recycle) && !await _dialogs.ConfirmAsync(L.T("Supprimer"), msg)) return;

        var paths = items.Select(i => i.FullPath).ToList();
        ActivePanel.Current.MarkAll(false);
        Operations.Start(new OperationJobViewModel(L.F("Suppression de {0}", what), transfer: false,
            (progress, ct, _) => FileOperations.DeleteAsync(paths, recycle, progress, ct)));
    }

    // ---- Marquage (commandes au clavier) -------------------------------------------------------

    /// <summary>Num+ / Num- : marquer ou démarquer selon un masque.</summary>
    public async Task MarkByMaskAsync(bool marked)
    {
        var mask = await _dialogs.PromptAsync(marked ? L.T("Marquer") : L.T("Démarquer"),
            L.T("Masque (ex. *.jpg;*.png  ou  *.* | *.tmp pour exclure) :"), "*.*");
        if (string.IsNullOrWhiteSpace(mask)) return;
        int n = ActivePanel.Current.MarkByMask(mask, marked, includeFolders: false);
        StatusMessage = L.F("{0} fichier(s) {1}.", n, (marked ? L.T("marqué(s)") : L.T("démarqué(s)")));
    }

    // ---- Lot 3 : recherche, comparaison, synchronisation, sommes de contrôle -------------------------

    /// <summary>Alt+F7 : recherche ; « Aller au fichier » ouvre son dossier dans le panneau actif.</summary>
    [RelayCommand]
    private async Task Search()
    {
        if (ActivePanel.Current.IsInsideArchive)
        {
            await _dialogs.ShowErrorAsync(L.T("Recherche"), L.T("La recherche ne se fait pas à l'intérieur d'une archive : extrais-la d'abord."));
            return;
        }
        var path = await _dialogs.ShowSearchAsync(ActivePanel.Current.CurrentPath, _remote);
        if (path is null) return;
        // « Aller au fichier » : son dossier dans le panneau actif (sur le disque ou sur le serveur).
        if (TwinPanel.Core.Remote.PanelRemotePath.TryParse(path, out var remote))
        {
            var parent = remote.WithPath(TwinPanel.Core.Remote.RemotePath.Parent(remote.Path) ?? "/").ToString();
            await ActivePanel.Current.NavigateAsync(parent, selectName: TwinPanel.Core.Remote.RemotePath.FileName(remote.Path));
            return;
        }
        var folder = Path.GetDirectoryName(path) ?? path;
        await ActivePanel.Current.NavigateAsync(folder, selectName: Path.GetFileName(path));
    }

    /// <summary>
    /// Maj+F2 : compare les deux dossiers affichés et marque, de chaque côté,
    /// les fichiers absents en face ou plus récents qu'en face.
    /// </summary>
    [RelayCommand]
    private void CompareFolders()
    {
        var left = Left.Current;
        var right = Right.Current;
        int n = MarkDifferences(left, right) + MarkDifferences(right, left);
        StatusMessage = n == 0 ? L.T("Les deux dossiers sont identiques (noms, tailles et dates).")
            : L.F("{0} différence(s) marquée(s) en rouge.", n);
    }

    private static int MarkDifferences(FolderTabViewModel tab, FolderTabViewModel other)
    {
        var otherByName = other.Items.Where(i => !i.IsParentLink)
            .GroupBy(i => i.Name, PathText.Comparer)
            .ToDictionary(g => g.Key, g => g.First(), PathText.Comparer);
        int n = 0;
        foreach (var item in tab.Items)
        {
            if (item.IsParentLink) continue;
            bool mark;
            if (!otherByName.TryGetValue(item.Name, out var twin)) mark = true;
            else if (item.IsDirectory || twin.IsDirectory) mark = item.IsDirectory != twin.IsDirectory;
            else
            {
                var diff = item.Entry.LastWriteTime - twin.Entry.LastWriteTime;
                mark = diff > TimeSpan.FromSeconds(2)
                       || (diff.Duration() <= TimeSpan.FromSeconds(2) && item.Entry.Size != twin.Entry.Size);
            }
            item.IsMarked = mark;
            if (mark) n++;
        }
        tab.UpdateSelectionInfo();
        return n;
    }

    /// <summary>Ctrl+Maj+S : synchroniser les dossiers des deux panneaux.</summary>
    [RelayCommand]
    private async Task SyncFolders()
    {
        if (Left.Current.IsInsideArchive || Right.Current.IsInsideArchive)
        {
            await _dialogs.ShowErrorAsync(L.T("Synchroniser"), L.T("La synchronisation ne fonctionne pas à l'intérieur d'une archive."));
            return;
        }
        var access = new SyncProfileAccess(_syncProfiles, ToStoredFolder, FromStoredFolder);
        var request = await _dialogs.ShowSyncAsync(Left.Current.CurrentPath, Right.Current.CurrentPath, _remote, access);
        // Profil sur un serveur fermé : on se connecte fenêtre fermée, pour que les questions de connexion restent visibles.
        while (request?.OpenProfile is { } wanted)
            request = await _dialogs.ShowSyncAsync(request.Left, request.Right, _remote, access, await OpenServersOfAsync(wanted) ? wanted : null);
        if (request is null || request.Plan.Count == 0) return;
        var remote = _remote;
        string Name(string p) => PanelRemotePath.TryParse(p, out var rp) ? $"{rp.Session}:{rp.Path}" : Path.GetFileName(p.TrimEnd('\\', '/'));
        var title = L.F("Synchronisation {0} ↔ {1}", Name(request.Left), Name(request.Right));
        Operations.Start(new OperationJobViewModel(title, transfer: false, (progress, ct, pause) => Task.Run(async () =>
        {
            await using var ls = SyncViewModel.CreateSide(request.Left, remote);
            await using var rs = SyncViewModel.CreateSide(request.Right, remote);
            await FolderSync.ExecuteAsync(ls, rs, request.Plan, progress, ct, pause);
        }, ct)));
    }

    /// <summary>
    /// Ctrl+Maj+F2 : compare le contenu de deux fichiers. Deux fichiers marqués dans le panneau actif,
    /// sinon le fichier courant de chaque panneau, sinon le fichier du même nom en face.
    /// </summary>
    [RelayCommand]
    private async Task CompareFiles()
    {
        var marked = ActivePanel.Current.MarkedItems.Where(i => !i.IsDirectory).ToList();
        FileEntry? ea = null, eb = null;
        if (marked.Count == 2) (ea, eb) = (marked[0].Entry, marked[1].Entry);
        else if (ActivePanel.Current.CurrentItem is { IsDirectory: false } cur)
        {
            ea = cur.Entry;
            var twin = OtherPanel.Current.Items.FirstOrDefault(i => !i.IsDirectory && string.Equals(i.Name, cur.Name, PathText.Comparison));
            eb = twin?.Entry ?? (OtherPanel.Current.CurrentItem is { IsDirectory: false } o ? o.Entry : null);
        }
        // Fichiers d'un serveur : comparés depuis une copie temporaire sur le PC.
        string? a = ea is null ? null : await LocalCopyAsync(ea);
        string? b = eb is null ? null : await LocalCopyAsync(eb);
        if ((ea is not null && a is null) || (eb is not null && b is null)) return; // téléchargement raté (déjà signalé)
        if (a is null || b is null)
        {
            await _dialogs.ShowErrorAsync(L.T("Comparer le contenu"),
                L.T("Marque deux fichiers dans un panneau, ou place le curseur sur un fichier de chaque côté."));
            return;
        }
        if (ActivePanel == Right) (a, b) = (b, a); // le fichier de gauche reste à gauche
        await _dialogs.ShowCompareFilesAsync(a, b);
    }

    /// <summary>Chemin local d'un fichier : lui-même, ou une copie temporaire s'il est sur un serveur.</summary>
    private async Task<string?> LocalCopyAsync(FileEntry entry) =>
        PanelRemotePath.TryParse(entry.FullPath, out _) ? await DownloadTempAsync(entry) : entry.FullPath;

    /// <summary>Crée un fichier de sommes de contrôle pour les fichiers marqués (ou le fichier courant).</summary>
    [RelayCommand]
    private async Task CreateChecksums()
    {
        var entries = ActivePanel.Current.TargetEntries.Where(e => !e.IsDirectory).ToList();
        var files = entries.Select(e => e.FullPath).ToList();
        if (files.Count == 0)
        {
            await _dialogs.ShowErrorAsync(L.T("Sommes de contrôle"), L.T("Marque un ou plusieurs fichiers (les dossiers ne sont pas pris en compte)."));
            return;
        }
        var kind = await _dialogs.ChooseAsync<HashKind>(L.T("Sommes de contrôle"), L.F("Type d'empreinte pour {0} fichier(s) :", files.Count),
            ("SHA-256", HashKind.Sha256), ("SHA-1", HashKind.Sha1), ("MD5", HashKind.Md5), (L.T("Annuler"), null));
        if (kind is not { } k) return;

        var dir = ActivePanel.Current.CurrentPath;
        var baseName = files.Count == 1 ? entries[0].Name : ActivePanel.Current.Title;
        var name = await _dialogs.PromptAsync(L.T("Sommes de contrôle"), L.T("Nom du fichier à créer :"), baseName + Checksums.Extension(k));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (SessionOf(ActivePanel.Current) is { } session)
        {
            // Serveur : empreintes calculées pendant le téléchargement, fichier de sommes écrit sur le serveur.
            var remoteOut = TwinPanel.Core.Remote.RemotePath.Combine(RemotePart(dir), name.Trim());
            var remoteFiles = entries.Select(ToRemoteItem).ToList();
            if (await session.UseAsync(fs => fs.StatAsync(remoteOut, CancellationToken.None), CancellationToken.None) is not null
                && !await _dialogs.ConfirmAsync(L.T("Sommes de contrôle"), L.F("« {0} » existe déjà. Le remplacer ?", name.Trim()))) return;
            Operations.Start(new OperationJobViewModel(L.F("Sommes {0} de {1} fichier(s) sur {2}", k, files.Count, session.Profile.Name), transfer: false,
                (progress, ct, _) => Task.Run(() => RemoteChecksums.CreateAsync(session, remoteFiles, k, remoteOut, progress, ct), ct)));
            return;
        }
        var output = Path.Combine(dir, name.Trim());
        if (File.Exists(output) && !await _dialogs.ConfirmAsync(L.T("Sommes de contrôle"), L.F("« {0} » existe déjà. Le remplacer ?", name.Trim()))) return;

        Operations.Start(new OperationJobViewModel(L.F("Sommes {0} de {1} fichier(s)", k, files.Count), transfer: false,
            (progress, ct, _) => Checksums.CreateAsync(files, k, output, progress, ct)));
    }

    /// <summary>Vérifie le fichier de sommes (.md5, .sha1, .sha256) sous le curseur.</summary>
    [RelayCommand]
    private async Task VerifyChecksums()
    {
        if (ActivePanel.Current.CurrentItem is not { IsDirectory: false } item
            || Path.GetExtension(item.Name).ToLowerInvariant() is not (".md5" or ".sha1" or ".sha256"))
        {
            await _dialogs.ShowErrorAsync(L.T("Vérifier les sommes"), L.T("Place le curseur sur un fichier .md5, .sha1 ou .sha256."));
            return;
        }
        StatusMessage = L.F("Vérification de {0}…", item.Name);
        try
        {
            var results = SessionOf(ActivePanel.Current) is { } session
                ? await Task.Run(() => RemoteChecksums.VerifyAsync(session, RemotePart(item.FullPath), CancellationToken.None))
                : await Checksums.VerifyAsync(item.FullPath);
            int ok = results.Count(r => r.Status == VerifyStatus.Ok);
            var problems = results.Where(r => r.Status != VerifyStatus.Ok)
                .Select(r => $"{(r.Status == VerifyStatus.Missing ? "MANQUANT " : "DIFFÉRENT")}  {r.FileName}").ToList();
            StatusMessage = L.F("{0} : {1}/{2} fichier(s) corrects.", item.Name, ok, results.Count);
            await _dialogs.ShowListAsync(L.T("Vérifier les sommes"),
                problems.Count == 0 ? L.F("Les {0} fichier(s) sont intacts.", ok) : L.F("{0} correct(s), {1} problème(s) :", ok, problems.Count),
                problems);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(L.T("Vérifier les sommes"), ex.Message);
        }
    }

    // ---- Lot 4 : vue rapide, arborescence, réglages des outils -------------------------------------

    /// <summary>Ctrl+Q : l'autre panneau affiche l'aperçu de l'élément sous le curseur.</summary>
    [RelayCommand]
    private void ToggleQuickView()
    {
        if (OtherPanel.IsQuickView || ActivePanel.IsQuickView) { StopQuickView(); return; }
        OtherPanel.IsQuickView = true;
        _quickViewPath = null;
        _quickViewTimer.Start();
    }

    private void StopQuickView()
    {
        _quickViewTimer.Stop();
        _quickViewPath = null;
        Left.IsQuickView = false;
        Right.IsQuickView = false;
    }

    private async Task UpdateQuickViewAsync()
    {
        var target = OtherPanel;
        if (!target.IsQuickView) return;
        var path = ActivePanel.Current.CurrentItem is { IsParentLink: false } item ? item.FullPath : null;
        if (path == _quickViewPath) return;
        _quickViewPath = path;

        if (path is null) { target.SetQuickView(null); return; }
        if (ActivePanel.Current.IsRemote)
        {
            // Aperçu d'un fichier distant : seulement s'il est petit (il faut le télécharger).
            var cur = ActivePanel.Current.CurrentItem;
            if (cur is null || cur.IsDirectory || cur.Entry.Size > 2 * 1024 * 1024) { target.SetQuickView(null); return; }
            if (await DownloadTempAsync(cur.Entry, quiet: true) is not { } local || path != _quickViewPath) return;
            path = local;
        }
        else if (ArchivePath.TrySplit(path, out _, out var innerKey) && innerKey.Length > 0)
        {
            // Aperçu d'un fichier d'archive : seulement s'il est petit (il faut l'extraire).
            var cur = ActivePanel.Current.CurrentItem;
            if (cur is null || cur.IsDirectory || cur.Entry.Size > 8 * 1024 * 1024) { target.SetQuickView(null); return; }
            if (await MaterializeAsync(path, quiet: true) is not { } temp || path != _quickViewPath) return;
            path = temp;
        }
        var viewer = new ViewerViewModel([path], 0, compact: true);
        target.SetQuickView(viewer);
        await viewer.LoadAsync();
    }

    /// <summary>Ctrl+F8 : affiche ou masque l'arborescence dans le panneau actif.</summary>
    [RelayCommand]
    private void ToggleTree() => ActivePanel.ShowTree = !ActivePanel.ShowTree;

    /// <summary>Menu Outils → Éditeur et visionneuse.</summary>
    [RelayCommand]
    private async Task ToolSettings()
    {
        if (await _dialogs.ShowToolSettingsAsync(_settings))
            StatusMessage = L.F("Éditeur (F4) : {0} — visionneuse (F3) : ", _tools.CurrentEditor.Name) +
                            (string.IsNullOrWhiteSpace(_settings.ViewerPath) ? L.T("intégrée") : Path.GetFileName(_settings.ViewerPath));
    }

    // ---- Lot 5 : archives ----------------------------------------------------------------------------

    /// <summary>Message et refus si le panneau actif montre l'intérieur d'une archive (lecture seule).</summary>
    private async Task<bool> RefuseInArchiveAsync()
    {
        if (!ActivePanel.Current.IsInsideArchive) return false;
        await _dialogs.ShowErrorAsync("Archive", L.T("Cette opération n'est pas possible à l'intérieur d'une archive.\n") +
                                                 L.T("Tu peux y ajouter (F5), renommer (Maj+F6), créer un dossier (F7) et supprimer (F8)."));
        return true;
    }

    /// <summary>Copie locale d'un fichier d'archive (dossier temporaire) ; le chemin lui-même s'il n'est pas dans une archive.</summary>
    private async Task<string?> MaterializeAsync(string path, bool quiet = false)
    {
        if (!ArchivePath.TrySplit(path, out var archive, out var inner) || inner.Length == 0) return path;
        try
        {
            if (!quiet) StatusMessage = L.F("Extraction de {0}…", Path.GetFileName(path));
            var temp = await ArchiveService.ExtractToTempAsync(archive, inner);
            if (!quiet) StatusMessage = L.T("Prêt");
            return temp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileOperationException)
        {
            if (!quiet) await _dialogs.ShowErrorAsync("Archive", ex.Message);
            return null;
        }
    }

    /// <summary>F5 depuis une archive : extraction des éléments marqués vers l'autre panneau. F6 : ils sont ensuite retirés de l'archive.</summary>
    private async Task ExtractFromArchiveAsync(string archive, string innerBase, IReadOnlyList<FileEntry> items, string? destination = null, bool move = false)
    {
        if (move && await EditableArchiveAsync() is null) return;
        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        var request = await _dialogs.AskTransferAsync(move ? L.T("Déplacer hors de l'archive") : L.T("Extraire"), what, destination ?? OtherPanel.Current.CurrentPath);
        if (request is null || request.Destination.Length == 0) return;

        var keys = items.Select(i => ArchivePath.TrySplit(i.FullPath, out _, out var k) ? k : string.Empty)
                        .Where(k => k.Length > 0).ToList();
        if (keys.Count == 0) return;
        var policy = request.Policy ?? ConflictPolicy.Overwrite;
        if (request.Policy is null)
        {
            var conflicts = items.Select(i => Path.Combine(request.Destination, i.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (conflicts.Count > 0)
            {
                if (await _dialogs.AskConflictAsync(conflicts) is not { } chosen) return;
                policy = chosen;
            }
        }
        ActivePanel.Current.MarkAll(false);
        var dest = request.Destination;
        Operations.Start(new OperationJobViewModel(L.F("{0} de {1} depuis {2}", (move ? L.T("Déplacement") : L.T("Extraction")), what, Path.GetFileName(archive)), transfer: true,
            (progress, ct, pause) => move
                ? ArchiveEditor.MoveOutAsync(archive, keys, innerBase, dest, policy, progress, ct, pause)
                : ArchiveService.ExtractAsync(archive, keys, innerBase, dest, policy, progress, ct, pause)) { ExclusiveKey = move ? archive : null });
    }

    /// <summary>Alt+F9 : extraire toute l'archive sous le curseur, dans un dossier à son nom à côté d'elle (modifiable). Archive et destination : PC ou serveur.</summary>
    [RelayCommand]
    private async Task UnpackArchive()
    {
        if (ActivePanel.Current.CurrentItem is not { IsDirectory: false } item || !ArchivePath.IsArchiveFile(item.Name) || ActivePanel.Current.IsInsideArchive)
        {
            await _dialogs.ShowErrorAsync(L.T("Extraire l'archive"), L.T("Place le curseur sur une archive (.zip, .7z, .rar, .tar, .gz…)."));
            return;
        }
        var source = SessionOf(ActivePanel.Current);
        if (ActivePanel.Current.IsRemote && source is null) { await _dialogs.ShowErrorAsync(L.T("Extraire l'archive"), L.T("La connexion de ce panneau n'est plus ouverte.")); return; }
        var baseName = item.Name;
        foreach (var ext in new[] { ".tar.gz", ".tar.bz2", ".tar.xz" })
            if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { baseName = baseName[..^ext.Length]; break; }
        if (baseName == item.Name) baseName = Path.GetFileNameWithoutExtension(item.Name);

        var request = await _dialogs.AskTransferAsync(L.T("Extraire"), L.F("le contenu de « {0} »", item.Name), CombinePanelPath(ActivePanel.Current.CurrentPath, baseName));
        if (request is null || request.Destination.Length == 0) return;
        if (await ResolveTargetAsync(request.Destination, L.T("Extraire")) is not { } target) return;
        var policy = request.Policy ?? ConflictPolicy.Overwrite;
        var archive = item.Entry;
        if (source is null && !await UnlockArchiveAsync(archive.FullPath)) return;
        Operations.Start(new OperationJobViewModel(L.F("Extraction de {0}", item.Name), transfer: true, (progress, ct, pause) =>
            source is null && target.Session is null
                ? ArchiveService.ExtractAsync(archive.FullPath, [""], "", target.Path, policy, progress, ct, pause)
                : Task.Run(() => RemoteArchives.ExtractAsync(source, source is null ? null : ToRemoteItem(archive), archive.FullPath,
                    target.Session, target.Path, policy, progress, ct, pause), ct)));
    }

    /// <summary>Ajoute un nom à un chemin de panneau (PC ou « sftp://… »).</summary>
    private static string CombinePanelPath(string panelPath, string name) =>
        PanelRemotePath.TryParse(panelPath, out var p) ? p.WithPath(TwinPanel.Core.Remote.RemotePath.Combine(p.Path, name)).ToString() : Path.Combine(panelPath, name);

    /// <summary>Destination tapée : serveur ouvert (« sftp://alias/chemin ») ou chemin du PC (relatif = dossier du panneau actif).</summary>
    private async Task<(TwinPanel.Core.Remote.RemoteSession? Session, string Path)?> ResolveTargetAsync(string text, string title)
    {
        text = text.Trim().Trim('"');
        if (PanelRemotePath.TryParse(text, out var rp))
        {
            if (_remote?.Find(rp.Session) is not { } session)
            {
                await _dialogs.ShowErrorAsync(title, L.F("La connexion « {0} » n'est pas ouverte.", rp.Session));
                return null;
            }
            return (session, rp.Path);
        }
        if (!Path.IsPathRooted(text))
        {
            if (PanelRemotePath.TryParse(ActivePanel.Current.CurrentPath, out var here) && _remote?.Find(here.Session) is { } s)
                return (s, TwinPanel.Core.Remote.RemotePath.Combine(here.Path, text.Replace('\\', '/')));
            text = Path.Combine(ActivePanel.Current.CurrentPath, text);
        }
        return (null, text);
    }

    /// <summary>Alt+F5 : créer une archive (.zip, .tar ou .tar.gz selon le nom) avec les éléments marqués ; sources et archive sur le PC ou un serveur.</summary>
    [RelayCommand]
    private async Task PackArchive()
    {
        if (await RefuseInArchiveAsync()) return;
        var items = ActivePanel.Current.TargetEntries;
        if (items.Count == 0) return;
        // Vue à plat : tous les fichiers seraient rangés à la racine de l'archive, sous le même nom.
        if (TwinPanel.Core.Search.FlatListing.DuplicateNames(items.Select(i => i.Name)) is { Count: > 0 } sameNames)
        {
            await _dialogs.ShowErrorAsync(L.T("Créer une archive"), L.F("{0} nom(s) reviennent plusieurs fois, par exemple « {1} » : ", sameNames.Count, sameNames[0]) +
                L.T("ils s'écraseraient dans l'archive.\nDémarque les doublons, ou crée l'archive depuis la liste normale (Ctrl+B)."));
            return;
        }
        var source = SessionOf(ActivePanel.Current);
        if (ActivePanel.Current.IsRemote && source is null) { await _dialogs.ShowErrorAsync(L.T("Créer une archive"), L.T("La connexion de ce panneau n'est plus ouverte.")); return; }
        var baseName = items.Count == 1 ? Path.GetFileNameWithoutExtension(items[0].Name) : ActivePanel.Current.Title;
        var targetDir = OtherPanel.Current.IsInsideArchive ? ActivePanel.Current.CurrentPath : OtherPanel.Current.CurrentPath;
        var defaultPath = CombinePanelPath(targetDir, baseName + ".zip");

        if (await _dialogs.AskPackAsync(defaultPath, defaultPath.Length - 4) is not { } pack) return;
        var password = pack.Password;
        if (await ResolveTargetAsync(pack.Path, L.T("Créer une archive")) is not { } target) return;
        if (ArchiveService.KindFromName(target.Path) is not { } kind)
        {
            await _dialogs.ShowErrorAsync(L.T("Créer une archive"), L.T("Formats possibles : .zip, .tar, .tar.gz (ou .tgz).\n") +
                                                               L.T("Le 7z et le rar peuvent être lus et extraits, mais pas créés."));
            return;
        }
        bool exists;
        try
        {
            exists = target.Session is { } ds
                ? await ds.UseAsync(fs => fs.StatAsync(target.Path, CancellationToken.None), CancellationToken.None) is not null
                : File.Exists(target.Path);
        }
        catch (Exception ex) when (ex is TwinPanel.Core.Remote.RemoteException or IOException)
        {
            await _dialogs.ShowErrorAsync(L.T("Créer une archive"), ex.Message);
            return;
        }
        var fileName = target.Session is null ? Path.GetFileName(target.Path) : TwinPanel.Core.Remote.RemotePath.FileName(target.Path);
        if (exists && !await _dialogs.ConfirmAsync(L.T("Créer une archive"), L.F("« {0} » existe déjà. La remplacer ?", fileName))) return;

        var localSources = items.Select(i => i.FullPath).ToList();
        var remoteItems = source is null ? new List<RemoteItem>() : items.Select(ToRemoteItem).ToList();
        ActivePanel.Current.MarkAll(false);
        var dest = target;
        Operations.Start(new OperationJobViewModel(L.F("Création de {0}", fileName), transfer: false, (progress, ct, pause) =>
            source is null && dest.Session is null
                ? ArchiveService.CreateAsync(localSources, dest.Path, kind, progress, ct, pause, password)
                : Task.Run(() => RemoteArchives.CreateAsync(source, remoteItems, localSources, dest.Session, dest.Path, kind, progress, ct, pause, password), ct)));
    }

    /// <summary>Tester l'archive sous le curseur (lecture complète, contrôle des sommes CRC).</summary>
    [RelayCommand]
    private async Task TestArchive()
    {
        if (ActivePanel.Current.CurrentItem is not { IsDirectory: false } item || !ArchivePath.IsArchiveFile(item.Name) || ActivePanel.Current.IsInsideArchive)
        {
            await _dialogs.ShowErrorAsync(L.T("Tester l'archive"), L.T("Place le curseur sur une archive (.zip, .7z, .rar, .tar, .gz…)."));
            return;
        }
        var entry = item.Entry;
        if (SessionOf(ActivePanel.Current) is { } session)
        {
            Operations.Start(new OperationJobViewModel(L.F("Test de {0}", item.Name), transfer: false,
                (progress, ct, _) => Task.Run(() => RemoteArchives.TestAsync(session, ToRemoteItem(entry), progress, ct), ct)));
            return;
        }
        var archive = item.FullPath;
        if (!await UnlockArchiveAsync(archive)) return;
        Operations.Start(new OperationJobViewModel(L.F("Test de {0}", item.Name), transfer: false,
            (progress, ct, _) => ArchiveService.TestAsync(archive, progress, ct)));
    }

    // ---- Presse-papiers / ligne de commande ------------------------------------------------------

    /// <summary>Ctrl+Entrée : nom de l'élément courant dans la ligne de commande ; Ctrl+Maj+Entrée : chemin complet.</summary>
    public void AppendToCommandLine(bool fullPath)
    {
        if (ActivePanel.Current.CurrentItem is not { IsParentLink: false } item) return;
        var text = fullPath ? item.FullPath : item.Name;
        if (text.Contains(' ')) text = $"\"{text}\"";
        CommandLine = (CommandLine.Length == 0 || CommandLine.EndsWith(' ') ? CommandLine : CommandLine + " ") + text + " ";
    }

    /// <summary>Texte à copier : chemins complets des éléments marqués (ou de l'élément courant), un par ligne.</summary>
    public string GetPathsForClipboard(bool namesOnly) =>
        string.Join(Environment.NewLine, ActivePanel.Current.TargetEntries.Select(e => namesOnly ? e.Name : e.FullPath));

    // ---- Aide ---------------------------------------------------------------------------------

    /// <summary>Guide d'utilisation (F1).</summary>
    [RelayCommand]
    private void ShowHelp() => _dialogs.ShowHelp(CurrentShortcuts(), atShortcuts: false);

    /// <summary>Le guide, à la page des raccourcis.</summary>
    [RelayCommand]
    private void ShowShortcutsHelp() => _dialogs.ShowHelp(CurrentShortcuts(), atShortcuts: true);

    /// <summary>Tous les raccourcis en vigueur (modifiables dans Options → Raccourcis).</summary>
    private List<TwinPanel.App.Views.HelpShortcut> CurrentShortcuts()
    {
        var lines = new List<TwinPanel.App.Views.HelpShortcut>();
        foreach (var group in CommandCatalog.All.GroupBy(c => c.Category))
        {
            lines.Add(new TwinPanel.App.Views.HelpShortcut(string.Empty, group.Key));
            foreach (var c in group)
                if (CommandCatalog.GesturesFor(c, _settings.Shortcuts) is { Length: > 0 } g)
                    lines.Add(new TwinPanel.App.Views.HelpShortcut(g.Replace(";", L.T(" ou "), StringComparison.Ordinal).Replace("OemPipe", "\\", StringComparison.Ordinal), c.Label));
        }
        lines.Add(new TwinPanel.App.Views.HelpShortcut(string.Empty, L.T("Touches fixes")));
        lines.AddRange(CommandCatalog.FixedKeys.Select(k => new TwinPanel.App.Views.HelpShortcut(k.Keys, k.Action)));
        return lines;
    }

    // ---- Astuces -------------------------------------------------------------------------------

    [ObservableProperty] private string _tipText = string.Empty;
    [ObservableProperty] private bool _isTipVisible;

    /// <summary>Une astuce par lancement ; la suivante la fois d'après.</summary>
    private void ShowStartupTip()
    {
        if (!_settings.Behavior.ShowTips) return;
        NextTip();
    }

    [RelayCommand]
    private void NextTip()
    {
        TipText = TwinPanel.Core.Help.Tips.At(_settings.Behavior.NextTip);
        _settings.Behavior.NextTip = (_settings.Behavior.NextTip + 1) % TwinPanel.Core.Help.Tips.All.Count;
        IsTipVisible = true;
    }

    [RelayCommand]
    private void CloseTip() => IsTipVisible = false;

    [RelayCommand]
    private void HideTips()
    {
        _settings.Behavior.ShowTips = false;
        IsTipVisible = false;
        StatusMessage = L.T("Astuces désactivées : Options → Comportement pour les remettre.");
    }

    [RelayCommand]
    private Task ShowAbout()
    {
        // Le numéro complet, pour que « beta » reste visible ; ce qui suit « + » est la référence du commit.
        var version = (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindowViewModel).Assembly)?.InformationalVersion ?? "?").Split('+')[0];
        return _dialogs.ShowAboutAsync($"TwinPanel {version}",
            L.T("Gestionnaire de fichiers à deux panneaux, gratuit.\nCréé par chrypox, avec l'aide de l'IA Claude Code."),
            [$".NET {Environment.Version} — {Environment.OSVersion.VersionString}", L.F("Réglages : {0}", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TwinPanel"))]);
    }

    // ---- Menu des onglets (clic droit sur un onglet) ------------------------------------------

    public async Task RenameTabAsync(FolderTabViewModel tab)
    {
        var name = await _dialogs.PromptAsync(L.T("Renommer l'onglet"), L.T("Nom de l'onglet (vide : nom du dossier) :"), tab.CustomName.Length > 0 ? tab.CustomName : tab.Title);
        if (name is null) return;
        name = name.Trim();
        tab.CustomName = name == tab.Title ? string.Empty : name;
    }

    public async Task CloseTabAsync(FilePanelViewModel panel, FolderTabViewModel tab)
    {
        if (panel.Tabs.Count <= 1) { StatusMessage = L.T("Le dernier onglet ne peut pas être fermé."); return; }
        if (tab.IsLocked && !await _dialogs.ConfirmAsync(L.T("Fermer l'onglet"), L.F("L'onglet « {0} » est verrouillé. Le fermer quand même ?", tab.DisplayTitle))) return;
        panel.CloseTab(tab);
    }

    public async Task CloseAllTabsAsync(FilePanelViewModel panel)
    {
        bool includeLocked = false;
        int locked = panel.Tabs.Count(t => t.IsLocked && !ReferenceEquals(t, panel.SelectedTab));
        if (locked > 0)
        {
            var choice = await _dialogs.ChooseAsync<bool>(L.T("Fermer tous les onglets"),
                L.F("{0} onglet(s) verrouillé(s). Les fermer aussi ?", locked), (L.T("Garder les verrouillés"), false), (L.T("Tout fermer"), true), (L.T("Annuler"), null));
            if (choice is null) return;
            includeLocked = choice.Value;
        }
        int closed = panel.CloseAllTabs(includeLocked);
        StatusMessage = L.F("{0} onglet(s) fermé(s) ; l'onglet affiché est gardé.", closed);
    }

    public void RemoveDuplicateTabs(FilePanelViewModel panel)
    {
        int closed = panel.RemoveDuplicateTabs();
        StatusMessage = closed == 0 ? L.T("Aucun onglet en double.") : L.F("{0} onglet(s) en double fermé(s).", closed);
    }

    // ---- Outils -----------------------------------------------------------------------------

    /// <summary>Imprimer les fichiers marqués (ou celui sous le curseur) avec l'application associée de Windows.</summary>
    [RelayCommand]
    private async Task Print()
    {
        var files = ActivePanel.Current.TargetEntries.Where(e => !e.IsDirectory).ToList();
        if (files.Count == 0) { await _dialogs.ShowErrorAsync(L.T("Imprimer"), L.T("Choisis un ou plusieurs fichiers (les dossiers ne s'impriment pas).")); return; }
        var (printers, defaultPrinter) = OperatingSystem.IsWindows() ? PrintService.Printers() : await Task.Run(TwinPanel.Core.Tools.LinuxPrinters.List);
        if (printers.Count == 0)
        {
            await _dialogs.ShowErrorAsync(L.T("Imprimer"), OperatingSystem.IsWindows() ? L.T("Aucune imprimante n'est installée dans Windows.") : L.T("Aucune imprimante n'est installée."));
            return;
        }
        if (await _dialogs.ChoosePrinterAsync(printers, defaultPrinter, files.Count) is not { } printer) return;

        int printed = 0;
        var opened = new List<string>();
        var failed = new List<string>();
        foreach (var f in files)
        {
            string? path = f.FullPath;
            if (ActivePanel.Current.IsRemote) path = await DownloadTempAsync(f, quiet: true);       // serveur : copie temporaire
            else if (ActivePanel.Current.IsInsideArchive) path = await MaterializeAsync(f.FullPath, quiet: true);
            if (path is null) { failed.Add(f.Name); continue; }
            var outcome = OperatingSystem.IsWindows() ? PrintService.Print(path, printer)
                : await Task.Run(() => TwinPanel.Core.Tools.LinuxPrinters.Send(path, printer)) ? PrintOutcome.Printed : PrintOutcome.Failed;
            switch (outcome)
            {
                case PrintOutcome.Printed: printed++; break;
                case PrintOutcome.OpenedForManualPrint: opened.Add(f.Name); break;
                default: failed.Add(f.Name); break;
            }
        }
        StatusMessage = L.F("Envoyé à « {0} » : {1} fichier(s).", printer, printed);
        if (opened.Count > 0)
            await _dialogs.ShowListAsync(L.T("Imprimer"), L.T("Ces fichiers n'ont pas d'impression directe : ils sont ouverts dans leur application, imprime-les avec Ctrl+P :"), opened);
        if (failed.Count > 0)
            await _dialogs.ShowListAsync(L.T("Imprimer"), L.T("Impossible d'imprimer ou d'ouvrir ces fichiers :"), failed);
    }

    private async Task RunShellAsync(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync(L.T("Erreur"), ex.Message);
        }
    }
}
