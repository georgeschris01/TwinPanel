// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Search;

namespace TwinPanel.App.ViewModels;

/// <summary>Une ligne de résultat (avec le groupe pour les doublons), avec les icônes et couleurs du panneau.</summary>
public sealed partial class SearchResult(FileEntry entry, string group) : ObservableObject
{
    public FileEntry Entry { get; } = entry;
    public string Group { get; } = group;
    public string Name => Entry.Name;
    /// <summary>Dossier qui contient le résultat (sur un serveur : sftp://…/dossier).</summary>
    public string Folder => TwinPanel.Core.Remote.PanelRemotePath.TryParse(Entry.FullPath, out var p)
        ? p.WithPath(TwinPanel.Core.Remote.RemotePath.Parent(p.Path) ?? "/").ToString()
        : Path.GetDirectoryName(Entry.FullPath) ?? string.Empty;
    public string SizeText => Entry.DisplaySize;
    public string DateText => Entry.DisplayDate;
    public FileCategory Category => Entry.Category;

    /// <summary>Couleur de l'icône (règles des Options → Couleurs comprises), comme dans les panneaux.</summary>
    public Avalonia.Media.IBrush IconBrush => Converters.FileIcons.BrushFor(Entry);
    /// <summary>Le nom a une couleur à lui (règle par masque, ou « colorer les noms ») ; sinon celle du thème.</summary>
    public bool HasNameColor => Converters.FileIcons.ColorNames || Converters.FileIcons.Rules.RuleColor(Entry) is not null;
    public Avalonia.Media.IBrush NameColor => Converters.FileIcons.BrushFor(Entry);

    /// <summary>Vraie icône du système (Options → Affichage) ; null : icône dessinée de TwinPanel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShellIcon))]
    private Avalonia.Media.Imaging.Bitmap? _shellIcon;
    public bool HasShellIcon => ShellIcon is not null;

    /// <summary>Demande la vraie icône si l'option est choisie (connue : tout de suite ; sinon lue sur un fil à part).</summary>
    public void RequestShellIcon()
    {
        if (!FolderTabViewModel.UseShellIcons || !Services.ShellIcons.IsSupported) return;
        bool local = !TwinPanel.Core.Remote.PanelRemotePath.TryParse(Entry.FullPath, out _);
        var (key, path, byType) = Services.ShellIcons.Describe(Entry.FullPath, Entry.Name, Entry.IsDirectory, local);
        if (Services.ShellIcons.TryGetCached(key, large: true, out var known)) { ShellIcon = known; return; }
        Services.ShellIcons.Request(key, path, Entry.IsDirectory, byType, large: true, icon => ShellIcon = icon);
    }
}

/// <summary>Fenêtre de recherche (Alt+F7) : recherche classique ou recherche de doublons.</summary>
public partial class SearchViewModel(string startPath, TwinPanel.Core.Remote.RemoteSessionManager? sessions = null) : ObservableObject
{
    private CancellationTokenSource? _cts;
    private string _note = string.Empty;

    /// <summary>Pose une question depuis la fenêtre de recherche (titre, message).</summary>
    public Func<string, string, Task<bool>>? Confirm { get; init; }

    public ObservableCollection<SearchResult> Results { get; } = [];

    [ObservableProperty] private string _root = startPath;
    [ObservableProperty] private string _nameMask = "*";
    [ObservableProperty] private bool _recursive = true;
    [ObservableProperty] private string _containsText = string.Empty;
    [ObservableProperty] private bool _matchCase;
    [ObservableProperty] private bool _textIsRegex;
    [ObservableProperty] private bool _searchDocuments = true;
    [ObservableProperty] private decimal? _minSizeKb;
    [ObservableProperty] private decimal? _maxSizeKb;
    [ObservableProperty] private DateTime? _modifiedAfter;
    [ObservableProperty] private DateTime? _modifiedBefore;
    [ObservableProperty] private bool _findDuplicates;
    [ObservableProperty] private SearchResult? _selectedResult;
    [ObservableProperty] private string _status = L.T("Indique les critères puis lance la recherche.");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    private bool _isRunning;

    private bool CanStart() => !IsRunning;
    private bool CanStop() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Start()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Results.Clear();
        _note = string.Empty;
        IsRunning = true;
        var clock = Stopwatch.StartNew();
        var currentFolder = new Progress<string>(f => Status = L.T("Recherche dans ") + f);
        try
        {
            var criteria = BuildCriteria();
            if (FindDuplicates) await RunDuplicatesAsync(criteria, currentFolder, cts.Token);
            else await RunSearchAsync(criteria, currentFolder, cts.Token);
            Status = L.F("{0} résultat(s) en {1:0.0} s", Results.Count, clock.Elapsed.TotalSeconds)
                     + (FindDuplicates ? L.F(" — {0} groupe(s) de doublons{1}", Results.Select(r => r.Group).Distinct().Count(), _note) : string.Empty);
        }
        catch (OperationCanceledException) { Status = L.F("Arrêté — {0} résultat(s).", Results.Count); }
        catch (ArgumentException ex) { Status = L.T("Expression régulière invalide : ") + ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = L.T("Erreur : ") + ex.Message; }
        finally { IsRunning = false; }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _cts?.Cancel();

    /// <summary>Remplit « Rechercher dans » avec tous les disques du PC.</summary>
    [RelayCommand]
    private void AllDrives() => Root = SearchRoots.AllDrives();

    public void Cancel() => _cts?.Cancel();

    private SearchCriteria BuildCriteria() => new()
    {
        Root = Root.Trim(),
        NameMask = string.IsNullOrWhiteSpace(NameMask) ? "*" : NameMask,
        MaxDepth = Recursive ? null : 0,
        ContainsText = string.IsNullOrEmpty(ContainsText) ? null : ContainsText,
        MatchCase = MatchCase,
        TextIsRegex = TextIsRegex,
        SearchDocuments = SearchDocuments,
        MinSize = MinSizeKb is { } min ? (long)(min * 1024) : null,
        MaxSize = MaxSizeKb is { } max ? (long)(max * 1024) : null,
        ModifiedAfter = ModifiedAfter?.Date,
        ModifiedBefore = ModifiedBefore?.Date.AddDays(1).AddTicks(-1), // jusqu'à la fin de la journée
        IncludeFolders = !FindDuplicates && string.IsNullOrEmpty(ContainsText),
    };

    /// <summary>
    /// Chaque dossier de départ (séparés par « ; ») : sur le disque ou sur un serveur ouvert.
    /// Les résultats sont ajoutés par paquets pour ne pas ralentir l'affichage.
    /// </summary>
    private async Task RunSearchAsync(SearchCriteria criteria, IProgress<string> progress, CancellationToken ct)
    {
        var roots = SearchRoots.Split(criteria.Root);
        if (roots.Count == 0) throw new IOException(L.T("Indique au moins un dossier où chercher."));
        foreach (var root in roots)
        {
            var batch = new List<SearchResult>();
            var sinceFlush = Stopwatch.StartNew();
            await foreach (var e in SearchOneRootAsync(criteria with { Root = root }, progress, ct))
            {
                batch.Add(new SearchResult(e, string.Empty));
                if (sinceFlush.ElapsedMilliseconds > 150) { Flush(batch); sinceFlush.Restart(); }
            }
            Flush(batch);
        }
    }

    private async IAsyncEnumerable<FileEntry> SearchOneRootAsync(SearchCriteria criteria, IProgress<string> progress,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!TwinPanel.Core.Remote.PanelRemotePath.TryParse(criteria.Root, out var remote))
        {
            await foreach (var e in FileSearch.SearchAsync(criteria, progress, ct)) yield return e;
            yield break;
        }
        var session = sessions?.Find(remote.Session)
                      ?? throw new IOException(L.F("La connexion « {0} » n'est pas ouverte : ouvre-la d'abord (Ctrl+F).", remote.Session));
        if (!string.IsNullOrEmpty(criteria.ContainsText))
            Status = L.T("Recherche de texte sur un serveur : chaque fichier est téléchargé pour être lu (fichiers de plus de 16 Mo ignorés)…");
        // Connexion à part : la navigation dans les panneaux reste libre pendant la recherche.
        await using var fs = await session.OpenWorkerAsync(ct);
        await foreach (var e in RemoteSearch.SearchAsync(fs, remote, criteria, progress, ct)) yield return e;
    }

    private async Task RunDuplicatesAsync(SearchCriteria criteria, IProgress<string> progress, CancellationToken ct)
    {
        if (SearchRoots.Split(criteria.Root).Any(r => TwinPanel.Core.Remote.PanelRemotePath.TryParse(r, out _)))
        {
            await RunRemoteDuplicatesAsync(criteria, progress, ct);
            return;
        }
        if (SearchRoots.Split(criteria.Root).Count > 1)
        {
            // Plusieurs disques : on rassemble tout, puis on compare.
            var all = new List<FileEntry>();
            foreach (var r in SearchRoots.Split(criteria.Root))
                await foreach (var e in FileSearch.SearchAsync(criteria with { Root = r, IncludeFolders = false }, progress, ct)) all.Add(e);
            await GroupDuplicatesAsync(all, ct);
            return;
        }
        var files = new List<FileEntry>();
        await foreach (var e in FileSearch.SearchAsync(criteria with { IncludeFolders = false }, progress, ct))
            files.Add(e);
        await GroupDuplicatesAsync(files, ct);
    }

    /// <summary>Un seul serveur à la fois : comparer avec le PC obligerait à tout lire des deux côtés.</summary>
    private async Task RunRemoteDuplicatesAsync(SearchCriteria criteria, IProgress<string> progress, CancellationToken ct)
    {
        var roots = new List<TwinPanel.Core.Remote.PanelRemotePath>();
        foreach (var root in SearchRoots.Split(criteria.Root))
        {
            if (!TwinPanel.Core.Remote.PanelRemotePath.TryParse(root, out var remote) || (roots.Count > 0 && remote.Session != roots[0].Session))
                throw new IOException(L.T("La recherche de doublons se fait sur un seul serveur à la fois, ou sur les disques du PC, sans les mélanger."));
            roots.Add(remote);
        }
        var session = sessions?.Find(roots[0].Session)
                      ?? throw new IOException(L.F("La connexion « {0} » n'est pas ouverte : ouvre-la d'abord (Ctrl+F).", roots[0].Session));

        // Connexion à part : la navigation dans les panneaux reste libre pendant la recherche.
        await using var fs = await session.OpenWorkerAsync(ct);
        var files = new List<FileEntry>();
        foreach (var root in roots)
            await foreach (var e in RemoteSearch.SearchAsync(fs, root, criteria with { IncludeFolders = false }, progress, ct)) files.Add(e);

        Status = L.F("Comparaison de {0} fichier(s) : tailles, puis contenu…", files.Count);
        var hashing = new Progress<string>(name => Status = L.T("Empreinte de ") + name);
        var found = await TwinPanel.Core.Remote.RemoteDuplicates.FindAsync(fs, files, AskNetworkReadAsync, hashing, ct);
        if (found.Unreadable > 0) _note = L.F(" — {0} fichier(s) illisible(s) non comparé(s)", found.Unreadable);
        ShowDuplicates(found.Groups);
    }

    /// <summary>Le serveur ne calcule pas les empreintes : lire ses fichiers par le réseau peut être long, l'utilisateur décide.</summary>
    private Task<bool> AskNetworkReadAsync(int files, long bytes) => Dispatcher.UIThread.InvokeAsync(() => Confirm?.Invoke(L.T("Doublons sur le serveur"),
        L.F("Ce serveur ne calcule pas les empreintes lui-même. Pour comparer {0} fichier(s) de même taille, il faut lire {1} par le réseau.\n", files, FileEntry.FormatSize(bytes)) +
        L.T("Rien n'est modifié sur le serveur ni enregistré sur le PC.\nContinuer ?")) ?? Task.FromResult(false));

    private async Task GroupDuplicatesAsync(List<FileEntry> files, CancellationToken ct)
    {
        Status = L.F("Comparaison du contenu de {0} fichier(s)…", files.Count);
        var hashing = new Progress<string>(f => Status = L.T("Empreinte de ") + Path.GetFileName(f));
        ShowDuplicates(await DuplicateFinder.FindAsync(files, hashing, ct));
    }

    private void ShowDuplicates(IReadOnlyList<DuplicateGroup> groups)
    {
        int n = 1;
        foreach (var g in groups)
        {
            var label = L.F("Groupe {0} — {1} × {2}", n++, g.Files.Count, FileEntry.FormatSize(g.Size));
            foreach (var f in g.Files) Add(new SearchResult(f, label));
        }
    }

    private void Add(SearchResult r)
    {
        r.RequestShellIcon();
        Results.Add(r);
    }

    private void Flush(List<SearchResult> batch)
    {
        foreach (var r in batch) Add(r);
        batch.Clear();
    }
}
