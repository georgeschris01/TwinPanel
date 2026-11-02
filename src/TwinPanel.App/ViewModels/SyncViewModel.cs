// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Compare;

namespace TwinPanel.App.ViewModels;

/// <summary>Une ligne de la synchronisation, avec l'action choisie (modifiable par clic).</summary>
public partial class SyncRowViewModel : ObservableObject
{
    public SyncRowViewModel(CompareItem item, SyncAction action)
    {
        Item = item;
        _action = action;
    }

    public CompareItem Item { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionSymbol))]
    private SyncAction _action;

    public string RelativePath => Item.IsDirectory ? $"[{Item.RelativePath}]" : Item.RelativePath;
    public string LeftSize => Item.Left is null ? string.Empty : Item.Left.DisplaySize;
    public string LeftDate => Item.Left?.DisplayDate ?? string.Empty;
    public string RightSize => Item.Right is null ? string.Empty : Item.Right.DisplaySize;
    public string RightDate => Item.Right?.DisplayDate ?? string.Empty;

    public string StateText => Item.State switch
    {
        CompareState.Equal => L.T("identique"),
        CompareState.LeftOnly => L.T("seulement à gauche"),
        CompareState.RightOnly => L.T("seulement à droite"),
        CompareState.LeftNewer => L.T("plus récent à gauche"),
        CompareState.RightNewer => L.T("plus récent à droite"),
        _ => L.T("différent"),
    };

    public string ActionSymbol => Action switch
    {
        SyncAction.CopyToRight => "→",
        SyncAction.CopyToLeft => "←",
        _ => "·",
    };

    /// <summary>Clic sur la flèche : aucune action → copier à droite → copier à gauche → …</summary>
    [RelayCommand]
    private void CycleAction()
    {
        var next = Action switch
        {
            SyncAction.None => SyncAction.CopyToRight,
            SyncAction.CopyToRight => SyncAction.CopyToLeft,
            _ => SyncAction.None,
        };
        // On ne copie pas depuis un côté vide.
        if (next == SyncAction.CopyToRight && Item.Left is null) next = SyncAction.CopyToLeft;
        if (next == SyncAction.CopyToLeft && Item.Right is null) next = SyncAction.None;
        Action = next;
    }
}

/// <summary>Demande de synchronisation renvoyée à la fenêtre principale.</summary>
/// <param name="OpenProfile">Pas de copie : ce profil vise un serveur fermé, la fenêtre se rouvrira une fois connecté.</param>
public sealed record SyncRequest(string Left, string Right, IReadOnlyList<(CompareItem Item, SyncAction Action)> Plan, SyncProfile? OpenProfile = null);

/// <summary>
/// Accès aux profils enregistrés. Les dossiers y sont gardés sous une forme stable : ToStored rend null pour une connexion
/// rapide (rien pour la retrouver), FromStored rend null si le serveur n'est pas ouvert.
/// </summary>
public sealed record SyncProfileAccess(SyncProfileStore Store, Func<string, string?> ToStored, Func<string, string?> FromStored);

public sealed record DirectionChoice(SyncDirection Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Fenêtre « Synchroniser les dossiers » (dossiers du PC ou de serveurs ouverts : « sftp://… »).</summary>
public partial class SyncViewModel(string left, string right, TwinPanel.Core.Remote.RemoteSessionManager? sessions = null, SyncProfileAccess? profiles = null) : ObservableObject
{
    private bool _keepResults;

    /// <summary>Questions posées depuis cette fenêtre : une fenêtre modale cacherait celles de la fenêtre principale.</summary>
    public Func<string, string, string, Task<string?>>? Prompt { get; init; }
    public Func<string, string, Task<bool>>? Confirm { get; init; }
    /// <summary>Le profil choisi vise un serveur fermé.</summary>
    public Action<SyncProfile>? NeedsConnection { get; init; }

    public ObservableCollection<SyncProfile> Profiles { get; } = new(profiles?.Store.Items ?? []);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    private SyncProfile? _selectedProfile;

    private bool HasSelectedProfile() => SelectedProfile is not null;

    /// <summary>Choisir un profil remplit la fenêtre, sans rien comparer ni copier.</summary>
    partial void OnSelectedProfileChanged(SyncProfile? value)
    {
        if (value is null || _keepResults) return;
        if (profiles?.FromStored(value.Left) is not { } leftPath || profiles.FromStored(value.Right) is not { } rightPath)
        {
            NeedsConnection?.Invoke(value);
            return;
        }
        LeftPath = leftPath;
        RightPath = rightPath;
        NameMask = value.NameMask;
        Recursive = value.Recursive;
        CompareContent = value.CompareContent;
        IgnoreDate = value.IgnoreDate;
        Direction = Directions.First(d => d.Value == value.Direction);
        ClearResults();
    }

    [RelayCommand]
    private async Task SaveProfile()
    {
        if (profiles is null || Prompt is null || Confirm is null) return;
        if (profiles.ToStored(LeftPath.Trim().Trim('"')) is not { } storedLeft || profiles.ToStored(RightPath.Trim().Trim('"')) is not { } storedRight)
        {
            Status = L.T("Une connexion rapide ne peut pas être gardée dans un profil : enregistre d'abord sa fiche (Ctrl+F).");
            return;
        }
        var name = (await Prompt(L.T("Enregistrer le profil"), L.T("Nom du profil :"), SelectedProfile?.Name ?? string.Empty))?.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (profiles.Store.Find(name) is { } existing && existing != SelectedProfile
            && !await Confirm(L.T("Enregistrer le profil"), L.F("Le profil « {0} » existe déjà. Le remplacer ?", existing.Name))) return;

        var profile = new SyncProfile
        {
            Name = name, Left = storedLeft, Right = storedRight, NameMask = NameMask, Recursive = Recursive,
            CompareContent = CompareContent, IgnoreDate = IgnoreDate, Direction = Direction.Value,
        };
        if (TryChangeProfiles(() => profiles.Store.Put(profile))) ShowProfiles(profile);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task DeleteProfile()
    {
        if (profiles is null || Confirm is null || SelectedProfile is not { } profile) return;
        if (!await Confirm(L.T("Supprimer le profil"), L.F("Supprimer le profil « {0} » ? Les dossiers ne sont pas touchés.", profile.Name))) return;
        if (TryChangeProfiles(() => profiles.Store.Remove(profile.Name))) ShowProfiles(null);
    }

    private bool TryChangeProfiles(Action change)
    {
        try { change(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = L.T("Profil non enregistré : ") + ex.Message;
            return false;
        }
    }

    /// <summary>Rafraîchit la liste sans recharger le profil : la comparaison affichée reste valable.</summary>
    private void ShowProfiles(SyncProfile? selected)
    {
        _keepResults = true;
        Profiles.Clear();
        foreach (var p in profiles?.Store.Items ?? []) Profiles.Add(p);
        SelectedProfile = selected;
        _keepResults = false;
    }

    public static IReadOnlyList<DirectionChoice> Directions { get; } =
    [
        new(SyncDirection.Both, L.T("Dans les deux sens ↔")),
        new(SyncDirection.ToRight, L.T("De gauche à droite →")),
        new(SyncDirection.ToLeft, L.T("De droite à gauche ←")),
    ];

    [ObservableProperty] private DirectionChoice _direction = Directions[0];

    /// <summary>Le sens change : les actions proposées sont recalculées (les choix faits ligne par ligne sont remplacés).</summary>
    partial void OnDirectionChanged(DirectionChoice value)
    {
        foreach (var r in _all) r.Action = r.Item.DefaultAction(value.Value);
        UpdateStatus();
    }

    /// <summary>Côté de synchronisation pour un chemin de panneau (serveur ouvert ou dossier du PC).</summary>
    public static SyncSide CreateSide(string path, TwinPanel.Core.Remote.RemoteSessionManager? sessions)
    {
        if (TwinPanel.Core.Remote.PanelRemotePath.TryParse(path, out var p))
        {
            var session = sessions?.Find(p.Session) ?? throw new IOException(L.F("La connexion « {0} » n'est pas ouverte.", p.Session));
            return new TwinPanel.Core.Remote.RemoteSyncSide(session, p.Path);
        }
        return new LocalSyncSide(path);
    }

    private readonly List<SyncRowViewModel> _all = [];
    private CancellationTokenSource? _cts;

    public ObservableCollection<SyncRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _leftPath = left;
    [ObservableProperty] private string _rightPath = right;
    [ObservableProperty] private string _nameMask = "*";
    [ObservableProperty] private bool _recursive = true;
    [ObservableProperty] private bool _compareContent;
    [ObservableProperty] private bool _ignoreDate;
    [ObservableProperty] private bool _showEqual;
    [ObservableProperty] private bool _showDifferent = true;
    [ObservableProperty] private bool _showLeftOnly = true;
    [ObservableProperty] private bool _showRightOnly = true;
    [ObservableProperty] private string _status = L.T("Clique sur « Comparer ».");
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasActions;

    // Une comparaison ne vaut que pour ses dossiers : sinon « Synchroniser » copierait vers ceux qu'on vient de taper.
    partial void OnLeftPathChanged(string value) => ClearResults();
    partial void OnRightPathChanged(string value) => ClearResults();

    private void ClearResults()
    {
        foreach (var r in _all) r.PropertyChanged -= OnRowChanged;
        _all.Clear();
        Rows.Clear();
        HasActions = false;
        Status = L.T("Clique sur « Comparer ».");
    }

    partial void OnShowEqualChanged(bool value) => ApplyFilter();
    partial void OnShowDifferentChanged(bool value) => ApplyFilter();
    partial void OnShowLeftOnlyChanged(bool value) => ApplyFilter();
    partial void OnShowRightOnlyChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private async Task Compare()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsBusy = true;
        var options = new CompareOptions
        {
            Recursive = Recursive,
            CompareContent = CompareContent,
            IgnoreDate = IgnoreDate,
            Bidirectional = Direction.Value == SyncDirection.Both,
            NameMask = string.IsNullOrWhiteSpace(NameMask) ? "*" : NameMask,
        };
        try
        {
            var progress = new Progress<string>(p => Status = L.T("Comparaison : ") + p);
            IReadOnlyList<CompareItem> items;
            await using (var ls = CreateSide(LeftPath.Trim().Trim('"'), sessions))
            await using (var rs = CreateSide(RightPath.Trim().Trim('"'), sessions))
                items = await Task.Run(() => FolderComparer.CompareAsync(ls, rs, options, progress, cts.Token), cts.Token);
            foreach (var r in _all) r.PropertyChanged -= OnRowChanged;
            _all.Clear();
            foreach (var i in items)
            {
                var row = new SyncRowViewModel(i, i.DefaultAction(Direction.Value));
                row.PropertyChanged += OnRowChanged;
                _all.Add(row);
            }
            ApplyFilter();
            UpdateStatus();
        }
        catch (OperationCanceledException) { Status = L.T("Comparaison arrêtée."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status = L.T("Erreur : ") + ex.Message; }
        finally { IsBusy = false; }
    }

    public void Cancel() => _cts?.Cancel();

    public SyncRequest BuildRequest() =>
        new(LeftPath.Trim().Trim('"'), RightPath.Trim().Trim('"'),
            _all.Where(r => r.Action != SyncAction.None).Select(r => (r.Item, r.Action)).ToList());

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncRowViewModel.Action)) UpdateStatus();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var r in _all.Where(Visible)) Rows.Add(r);
    }

    private bool Visible(SyncRowViewModel r) => r.Item.State switch
    {
        CompareState.Equal => ShowEqual,
        CompareState.LeftOnly => ShowLeftOnly,
        CompareState.RightOnly => ShowRightOnly,
        _ => ShowDifferent,
    };

    private void UpdateStatus()
    {
        var files = _all.Where(r => !r.Item.IsDirectory && r.Action != SyncAction.None).ToList();
        int toRight = files.Count(r => r.Action == SyncAction.CopyToRight);
        int toLeft = files.Count - toRight;
        long bytes = files.Sum(r => (r.Action == SyncAction.CopyToRight ? r.Item.Left : r.Item.Right)?.Size ?? 0);
        int equal = _all.Count(r => r.Item.State == CompareState.Equal);
        HasActions = _all.Any(r => r.Action != SyncAction.None);
        Status = L.F("{0} élément(s), {1} identique(s) — à copier : {2} → droite, {3} ← gauche ({4})", _all.Count, equal, toRight, toLeft, FileEntry.FormatSize(bytes));
    }
}
