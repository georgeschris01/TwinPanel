// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.Services;
using TwinPanel.Core;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Rights;

namespace TwinPanel.App.ViewModels;

/// <summary>Une case à trois états (droit Unix ou attribut Windows) : null = « mélangé » quand plusieurs éléments diffèrent.</summary>
public partial class TriStateFlag : ObservableObject
{
    private readonly Action _changed;

    public TriStateFlag(int bit, string label, bool? initial, Action changed)
    {
        Bit = bit;
        Label = label;
        Initial = initial;
        _value = initial;
        _changed = changed;
    }

    public int Bit { get; }
    public string Label { get; }
    public bool? Initial { get; private set; }
    /// <summary>La case peut revenir à « mélangé » seulement si elle l'était au départ.</summary>
    public bool IsMixed => Initial is null;
    public bool IsChanged => Value != Initial;

    [ObservableProperty]
    private bool? _value;

    partial void OnValueChanged(bool? value) => _changed();

    /// <summary>Après « Appliquer » : la nouvelle valeur devient la valeur de départ.</summary>
    public void Commit()
    {
        Initial = Value;
        OnPropertyChanged(nameof(IsMixed));
    }
}

/// <summary>Que faire du contenu des dossiers (option « récursif »).</summary>
public sealed record ContentRule(int Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Lecture « fil de navigation » d'un serveur : passe par la connexion du panneau (reconnexion automatique).</summary>
internal sealed class SessionRightsTarget(RemoteSession session) : IRightsTarget
{
    public bool SupportsPermissions => session.SupportsPermissions;
    public bool SupportsOwnership => session.SupportsOwnership;
    public Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct) => session.UseAsync(fs => fs.ListAsync(path, ct), ct);
    public Task<RightsDetails?> GetDetailsAsync(string path, CancellationToken ct) => session.UseAsync(fs => fs.GetDetailsAsync(path, ct), ct);
    public Task SetModeAsync(string path, int mode, CancellationToken ct) => session.UseAsync(fs => fs.SetModeAsync(path, mode, ct), ct);
    public Task SetOwnerAsync(string path, UnixAccount? user, UnixAccount? group, CancellationToken ct) => session.UseAsync(fs => fs.SetOwnerAsync(path, user, group, ct), ct);
    public Task<UnixAccounts?> GetAccountsAsync(CancellationToken ct) => session.UseAsync(fs => fs.GetAccountsAsync(ct), ct);
}

/// <summary>
/// Fenêtre Propriétés (Alt+Entrée) : informations, droits Unix (serveur SFTP / SCP / FTP, disque sous Linux),
/// attributs et sécurité NTFS (Windows). Plusieurs éléments peuvent être modifiés ensemble.
/// </summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    public const int MaxDetails = 500;

    private readonly IReadOnlyList<FileEntry> _entries;
    private readonly RemoteSession? _session;
    private readonly IDialogService _dialogs;
    private readonly IRightsTarget? _rights;
    private readonly List<RightsDetails> _details = [];
    private UnixAccounts? _accounts;
    private CancellationTokenSource? _cts;
    private bool _syncing;

    /// <param name="entries">Éléments choisis (chemins de panneau : « C:\… », « /home/… » ou « sftp://alias/… »).</param>
    public PropertiesViewModel(IReadOnlyList<FileEntry> entries, RemoteSession? session, IDialogService dialogs)
    {
        _entries = entries;
        _session = session;
        _dialogs = dialogs;
        IsRemote = session is not null;
        if (session is not null) _rights = new SessionRightsTarget(session);
        else if (!OperatingSystem.IsWindows()) _rights = new LocalUnixRights();
        IsWindowsLocal = !IsRemote && OperatingSystem.IsWindows();
        HasUnixRights = _rights?.SupportsPermissions == true || _rights?.SupportsOwnership == true;
        CanChmod = _rights?.SupportsPermissions == true;
        CanChown = _rights?.SupportsOwnership == true;
        HasFolders = entries.Any(e => e.IsDirectory);

        // Droits : propriétaire / groupe / autres × lire / écrire / exécuter, puis bits spéciaux.
        string[] who = [L.T("Propriétaire"), L.T("Groupe"), L.T("Autres")];
        string[] what = [L.T("Lecture"), L.T("Écriture"), L.T("Exécution")];
        for (int w = 0; w < 3; w++)
            for (int a = 0; a < 3; a++)
                ModeBits.Add(new TriStateFlag(UnixMode.Bit(w, a), $"{who[w]} : {what[a]}", false, OnBitsChanged));
        SpecialBits.Add(new TriStateFlag(UnixMode.SetUid, L.T("Set-UID (s'exécute avec les droits du propriétaire)"), false, OnBitsChanged));
        SpecialBits.Add(new TriStateFlag(UnixMode.SetGid, L.T("Set-GID (groupe hérité par les nouveaux fichiers d'un dossier)"), false, OnBitsChanged));
        SpecialBits.Add(new TriStateFlag(UnixMode.Sticky, L.T("Sticky (dans un dossier : seul le propriétaire supprime ses fichiers)"), false, OnBitsChanged));

        ContentRules =
        [
            new(0, L.T("Appliquer seulement les cases changées")),
            new(1, L.T("Mettre partout les droits ci-dessus")),
            new(2, L.T("Droits séparés pour les fichiers et les dossiers")),
            new(3, L.T("Ne pas changer les droits du contenu (propriétaire seulement)")),
        ];
        _contentRule = ContentRules[0];

        Title = entries.Count == 1 ? L.F("Propriétés de {0}", entries[0].Name) : L.F("Propriétés de {0} éléments", entries.Count);
        Header = entries.Count == 1 ? entries[0].Name : L.F("{0} éléments", entries.Count);
        Location = entries.Count == 0 ? string.Empty : ParentOf(entries[0].FullPath);
        if (IsWindowsLocal) LoadWindowsAttributes();
    }

    public string Title { get; }
    public string Header { get; }
    public string Location { get; }
    public bool IsRemote { get; }
    public bool IsWindowsLocal { get; }
    public bool HasUnixRights { get; }
    public bool CanChmod { get; }
    public bool CanChown { get; }
    public bool HasFolders { get; }
    public bool IsSingle => _entries.Count == 1;
    public string ServerName => _session?.Profile.Name ?? string.Empty;

    // ---- Général ------------------------------------------------------------------------------------------

    [ObservableProperty] private string _typeText = string.Empty;
    [ObservableProperty] private string _sizeText = string.Empty;
    [ObservableProperty] private string _contentText = string.Empty;
    [ObservableProperty] private string _createdText = string.Empty;
    [ObservableProperty] private string _modifiedText = string.Empty;
    [ObservableProperty] private string _accessedText = string.Empty;
    [ObservableProperty] private string _linkText = string.Empty;
    [ObservableProperty] private string _ownerText = string.Empty;
    [ObservableProperty] private string _groupText = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isMeasuring;
    [ObservableProperty] private bool _isLoaded;

    public bool HasLink => LinkText.Length > 0;
    partial void OnLinkTextChanged(string value) => OnPropertyChanged(nameof(HasLink));

    private static string ParentOf(string path)
    {
        if (PanelRemotePath.TryParse(path, out var p)) return p.WithPath(RemotePath.Parent(p.Path) ?? "/").ToString();
        return Path.GetDirectoryName(path) ?? path;
    }

    private static string RemotePart(string panelPath) => PanelRemotePath.TryParse(panelPath, out var p) ? p.Path : panelPath;

    /// <summary>Chemin compris par la cible des droits (chemin distant, ou chemin local).</summary>
    private string TargetPath(FileEntry e) => IsRemote ? RemotePart(e.FullPath) : e.FullPath;

    public static string SizeWithBytes(long bytes) =>
        bytes < 1024 ? L.F("{0} octets", bytes.ToString("N0", CultureInfo.CurrentCulture))
            : L.F("{0} ({1} octets)", FileEntry.FormatSize(bytes), bytes.ToString("N0", CultureInfo.CurrentCulture));

    private static string Date(DateTime? d) => d is { } v && v > DateTime.MinValue.AddDays(1) ? v.ToString("dddd d MMMM yyyy, HH:mm:ss", CultureInfo.CurrentCulture) : string.Empty;

    /// <summary>Lecture des informations (appelée à l'ouverture de la fenêtre).</summary>
    public async Task LoadAsync()
    {
        IsBusy = true;
        Status = L.T("Lecture des informations…");
        try
        {
            if (_rights is not null)
            {
                foreach (var e in _entries.Take(MaxDetails))
                {
                    try
                    {
                        if (await _rights.GetDetailsAsync(TargetPath(e), CancellationToken.None) is { } d) _details.Add(d);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Status = ex.Message;
                    }
                }
                if (CanChown)
                {
                    try { _accounts = await _rights.GetAccountsAsync(CancellationToken.None); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _accounts = null; }
                    if (_accounts is null)
                        AccountsHint = L.T("Liste des comptes indisponible sur ce serveur : tape un nom ou un numéro (uid / gid) dans « Autre ».");
                }
                InitUnixRights();
            }
            FillGeneral();
            if (IsWindowsLocal && IsSingle) await Task.Run(LoadSecuritySafe);
            await LoadMediaAsync();
            if (Status == L.T("Lecture des informations…")) Status = string.Empty;
        }
        finally
        {
            IsBusy = false;
            IsLoaded = true;
        }
    }

    private void FillGeneral()
    {
        int files = _entries.Count(e => !e.IsDirectory), folders = _entries.Count - files;
        if (IsSingle)
        {
            var e = _entries[0];
            var d = _details.FirstOrDefault();
            bool isLink = d?.IsLink ?? (e.Attributes & FileAttributes.ReparsePoint) != 0;
            TypeText = (e.IsDirectory ? L.T("Dossier") : e.Extension.Length > 0 ? L.F("Fichier {0}", e.Extension.ToUpperInvariant()) : L.T("Fichier"))
                       + (isLink ? (IsRemote || !OperatingSystem.IsWindows() ? L.T(" (lien symbolique)") : L.T(" (lien / jonction)")) : string.Empty);
            SizeText = e.IsDirectory ? L.T("Dossier : clique sur « Calculer »") : SizeWithBytes(d?.Size ?? e.Size);
            ModifiedText = Date(d?.Modified ?? e.LastWriteTime);
            AccessedText = Date(d?.Accessed);
            CreatedText = Date(d?.Created);
            if (!IsRemote)
            {
                try
                {
                    FileSystemInfo info = e.IsDirectory ? new DirectoryInfo(e.FullPath) : new FileInfo(e.FullPath);
                    CreatedText = Date(info.CreationTime);
                    AccessedText = Date(info.LastAccessTime);
                    ModifiedText = Date(info.LastWriteTime);
                    if (info.LinkTarget is { } target) LinkText = target;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (d?.LinkTarget is { Length: > 0 } t) LinkText = t;
        }
        else
        {
            TypeText = L.F("{0} fichier(s), {1} dossier(s)", files, folders);
            long bytes = _entries.Where(x => !x.IsDirectory).Sum(x => x.Size);
            SizeText = folders > 0 ? L.F("{0} sans le contenu des dossiers", SizeWithBytes(bytes)) : SizeWithBytes(bytes);
        }
        if (_details.Count > 0)
        {
            OwnerText = Same(_details.Select(x => x.OwnerDisplay)) ?? L.T("(plusieurs)");
            GroupText = Same(_details.Select(x => x.GroupDisplay)) ?? L.T("(plusieurs)");
        }
    }

    private static int? CommonId(IEnumerable<int?> ids)
    {
        var distinct = ids.Distinct().Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static List<UnixAccount> WithCurrent(IReadOnlyList<UnixAccount>? list, int? currentId, string? currentName)
    {
        var result = list?.ToList() ?? [];
        if (currentId is { } id && result.All(a => a.Id != id))
            result.Insert(0, new UnixAccount(id, currentName is { Length: > 0 } n ? n : id.ToString(CultureInfo.InvariantCulture)));
        return result;
    }

    private static string? Same(IEnumerable<string> values)
    {
        var distinct = values.Distinct(StringComparer.Ordinal).Take(2).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    /// <summary>Taille réelle des dossiers (parcours complet ; un second clic l'arrête).</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Measure()
    {
        if (IsMeasuring) { _cts?.Cancel(); return; }
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsMeasuring = true;
        SizeText = L.T("Calcul en cours…");
        try
        {
            long bytes; int files, folders;
            if (IsRemote && _session is not null)
            {
                var items = _entries.Select(e => _details.FirstOrDefault(d => d.Path == RemotePart(e.FullPath))?.ToItem()
                                                 ?? new RemoteItem(e.Name, RemotePart(e.FullPath), e.IsDirectory, e.Size, e.LastWriteTime)).ToList();
                // Connexion à part : la navigation reste libre pendant un long calcul.
                await using var fs = await _session.OpenWorkerAsync(ct);
                var progress = new Progress<string>(p => Status = p);
                (bytes, files, folders) = await Task.Run(() => RightsApplier.MeasureAsync(fs, items, progress, ct), ct);
            }
            else
            {
                (bytes, files, folders) = await Task.Run(() =>
                {
                    long b = 0; int f = 0, d = 0;
                    foreach (var e in _entries)
                    {
                        var m = LocalTree.Measure(e.FullPath, ct);
                        b += m.Bytes; f += m.Files; d += m.Folders + (e.IsDirectory ? 1 : 0);
                    }
                    return (b, f, d);
                }, ct);
            }
            SizeText = SizeWithBytes(bytes);
            ContentText = L.F("{0} fichier(s), {1} dossier(s)", files.ToString("N0", CultureInfo.CurrentCulture), folders.ToString("N0", CultureInfo.CurrentCulture));
            Status = string.Empty;
        }
        catch (OperationCanceledException) { SizeText = L.T("Calcul annulé."); Status = string.Empty; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SizeText = string.Empty; Status = ex.Message; }
        finally { IsMeasuring = false; }
    }

    public string MeasureLabel => IsMeasuring ? L.T("Arrêter") : L.T("Calculer");
    partial void OnIsMeasuringChanged(bool value) => OnPropertyChanged(nameof(MeasureLabel));

    // ---- Droits Unix --------------------------------------------------------------------------------------

    public ObservableCollection<TriStateFlag> ModeBits { get; } = [];
    public ObservableCollection<TriStateFlag> SpecialBits { get; } = [];
    public TriStateFlag Bit(int who, int what) => ModeBits[who * 3 + what];

    // Noms pour la grille de la fenêtre (liaisons simples).
    public TriStateFlag UR => ModeBits[0];
    public TriStateFlag UW => ModeBits[1];
    public TriStateFlag UX => ModeBits[2];
    public TriStateFlag GR => ModeBits[3];
    public TriStateFlag GW => ModeBits[4];
    public TriStateFlag GX => ModeBits[5];
    public TriStateFlag OR => ModeBits[6];
    public TriStateFlag OW => ModeBits[7];
    public TriStateFlag OX => ModeBits[8];
    public TriStateFlag SUid => SpecialBits[0];
    public TriStateFlag SGid => SpecialBits[1];
    public TriStateFlag SSticky => SpecialBits[2];

    private IEnumerable<TriStateFlag> AllBits => ModeBits.Concat(SpecialBits);

    [ObservableProperty] private string _octal = string.Empty;
    [ObservableProperty] private string _symbolic = string.Empty;
    [ObservableProperty] private bool _modesKnown;
    [ObservableProperty] private string _ownerInput = string.Empty;
    [ObservableProperty] private string _groupInput = string.Empty;
    /// <summary>Tous les utilisateurs / groupes du serveur, pour les listes déroulantes.</summary>
    [ObservableProperty] private IReadOnlyList<UnixAccount> _userList = [];
    [ObservableProperty] private IReadOnlyList<UnixAccount> _groupList = [];
    [ObservableProperty] private UnixAccount? _selectedUser;
    [ObservableProperty] private UnixAccount? _selectedGroup;
    public bool HasAccountList => UserList.Count > 0 || GroupList.Count > 0;
    partial void OnUserListChanged(IReadOnlyList<UnixAccount> value) => OnPropertyChanged(nameof(HasAccountList));
    partial void OnGroupListChanged(IReadOnlyList<UnixAccount> value) => OnPropertyChanged(nameof(HasAccountList));
    [ObservableProperty] private string _accountsHint = string.Empty;
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private string _fileOctal = "644";
    [ObservableProperty] private string _dirOctal = "755";

    private int? _initialUserId, _initialGroupId;
    private bool _modeUnknown;

    public IReadOnlyList<ContentRule> ContentRules { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeparateModes), nameof(GridEnabled))]
    private ContentRule _contentRule;

    public bool SeparateModes => Recursive && ContentRule.Id == 2;
    public bool GridEnabled => CanChmod && ModesKnown && !SeparateModes;
    partial void OnRecursiveChanged(bool value) { OnPropertyChanged(nameof(SeparateModes)); OnPropertyChanged(nameof(GridEnabled)); }
    partial void OnModesKnownChanged(bool value) => OnPropertyChanged(nameof(GridEnabled));

    private void InitUnixRights()
    {
        var modes = _details.Where(d => d.Mode >= 0).Select(d => d.Mode).ToList();
        ModesKnown = modes.Count > 0;
        _syncing = true;
        foreach (var b in AllBits)
        {
            var common = RightsApplier.Common(modes, b.Bit);
            b.Value = common;
            b.Commit();
        }
        _syncing = false;
        UpdateOctalFromBits();
        if (!ModesKnown && CanChmod) Status = L.T("Le serveur ne donne pas les droits actuels : choisis-les puis « Appliquer ».");

        // Listes complètes du serveur ; le propriétaire / groupe actuel y est choisi (ajouté s'il n'a pas de nom connu).
        _initialUserId = CommonId(_details.Select(d => d.UserId));
        _initialGroupId = CommonId(_details.Select(d => d.GroupId));
        UserList = WithCurrent(_accounts?.Users, _initialUserId, _details.FirstOrDefault()?.Owner);
        GroupList = WithCurrent(_accounts?.Groups, _initialGroupId, _details.FirstOrDefault()?.Group);
        SelectedUser = _initialUserId is { } u ? UserList.FirstOrDefault(a => a.Id == u) : null;
        SelectedGroup = _initialGroupId is { } g ? GroupList.FirstOrDefault(a => a.Id == g) : null;
        OwnerInput = string.Empty;
        GroupInput = string.Empty;
        // Un serveur sans droits connus (FTP sous Windows…) : on laisse quand même essayer, avec des droits complets.
        _modeUnknown = !ModesKnown;
        if (!ModesKnown && CanChmod) ModesKnown = true;
    }

    private void OnBitsChanged()
    {
        if (_syncing) return;
        UpdateOctalFromBits();
    }

    private void UpdateOctalFromBits()
    {
        _syncing = true;
        if (AllBits.Any(b => b.Value is null))
        {
            Octal = string.Empty;
            Symbolic = L.T("(droits différents selon les éléments)");
        }
        else
        {
            int mode = AllBits.Where(b => b.Value == true).Sum(b => b.Bit);
            Octal = UnixMode.ToOctal(mode);
            Symbolic = UnixMode.ToSymbolic(mode);
        }
        _syncing = false;
    }

    partial void OnOctalChanged(string value)
    {
        if (_syncing || !UnixMode.TryParseOctal(value, out var mode)) return;
        _syncing = true;
        foreach (var b in AllBits) b.Value = (mode & b.Bit) != 0;
        Symbolic = UnixMode.ToSymbolic(mode);
        _syncing = false;
    }

    /// <summary>Raccourcis courants.</summary>
    [RelayCommand]
    private void SetPreset(string octal) => Octal = octal;

    private ModeChange ChangedBits()
    {
        int set = 0, clear = 0;
        foreach (var b in AllBits.Where(b => b.IsChanged && b.Value is not null))
            if (b.Value == true) set |= b.Bit; else clear |= b.Bit;
        return new ModeChange(set, clear);
    }

    private bool TryBuildRequest(out RightsRequest request, out string error)
    {
        request = new RightsRequest(ModeChange.None, ModeChange.None, null, null, false);
        error = string.Empty;
        ModeChange file = ModeChange.None, dir = ModeChange.None;
        bool modeForContent = true;
        if (CanChmod)
        {
            var changed = _modeUnknown && AllBits.Any(b => b.IsChanged)
                ? ModeChange.Absolute(AllBits.Where(b => b.Value == true).Sum(b => b.Bit))
                : ChangedBits();
            file = dir = changed;
            if (Recursive && HasFolders)
            {
                switch (ContentRule.Id)
                {
                    case 1:
                        if (AllBits.Any(b => b.Value is null)) { error = L.T("Certaines cases sont « mélangées » : coche-les ou décoche-les toutes pour mettre partout les mêmes droits."); return false; }
                        file = dir = ModeChange.Absolute(AllBits.Where(b => b.Value == true).Sum(b => b.Bit));
                        break;
                    case 2:
                        if (!UnixMode.TryParseOctal(FileOctal, out var f) || !UnixMode.TryParseOctal(DirOctal, out var d))
                        { error = L.T("Droits des fichiers / dossiers : 3 ou 4 chiffres de 0 à 7 (ex. 644 et 755)."); return false; }
                        file = ModeChange.Absolute(f);
                        dir = ModeChange.Absolute(d);
                        break;
                    case 3:
                        modeForContent = false;
                        break;
                }
            }
        }

        UnixAccount? owner = null, group = null;
        if (CanChown)
        {
            // « Autre » (nom ou numéro tapé) passe avant la liste ; sinon la liste, si le choix a changé.
            if (OwnerInput.Trim().Length > 0)
            {
                owner = (_accounts?.FindUser(OwnerInput)) ?? UnixAccounts.Find(null, OwnerInput);
                if (owner is null) { error = L.F("Utilisateur inconnu : « {0} » (choisis-le dans la liste ou tape son numéro).", OwnerInput.Trim()); return false; }
            }
            else if (SelectedUser is { } su && su.Id != _initialUserId) owner = su;
            if (GroupInput.Trim().Length > 0)
            {
                group = (_accounts?.FindGroup(GroupInput)) ?? UnixAccounts.Find(null, GroupInput);
                if (group is null) { error = L.F("Groupe inconnu : « {0} » (choisis-le dans la liste ou tape son numéro).", GroupInput.Trim()); return false; }
            }
            else if (SelectedGroup is { } sg && sg.Id != _initialGroupId) group = sg;
        }
        request = new RightsRequest(file, dir, owner, group, Recursive && HasFolders, modeForContent);
        return true;
    }

    private async Task<bool> ApplyUnixAsync()
    {
        if (_rights is null || !HasUnixRights) return true;
        if (!TryBuildRequest(out var request, out var error)) { await _dialogs.ShowErrorAsync(L.T("Droits"), error); return false; }
        if (request.IsEmpty) return true;

        var items = _entries.Select(e => _details.FirstOrDefault(d => d.Path == TargetPath(e) || d.Path == RemotePath.Normalize(TargetPath(e)))?.ToItem()
                                         ?? new RemoteItem(e.Name, TargetPath(e), e.IsDirectory, e.Size, e.LastWriteTime)).ToList();
        if (request.Recursive)
        {
            var what = request.ChangesOwner && (request.FileMode.IsEmpty && request.DirMode.IsEmpty || !request.ModeForContent)
                ? L.T("le propriétaire / groupe") : L.T("les droits");
            if (!await _dialogs.ConfirmAsync(L.T("Appliquer au contenu"), L.F("Changer {0} de tout le contenu des dossiers choisis (sous-dossiers compris) ?", what)))
                return false;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsBusy = true;
        try
        {
            var progress = new Progress<string>(p => Status = p);
            RightsResult result;
            if (_session is not null && request.Recursive)
            {
                await using var fs = await _session.OpenWorkerAsync(ct);
                result = await Task.Run(() => RightsApplier.ApplyAsync(fs, items, request, progress, ct), ct);
            }
            else
            {
                IRightsTarget target = _rights;
                result = await Task.Run(() => RightsApplier.ApplyAsync(target, items, request, progress, ct), ct);
            }
            Changed = true;
            Status = L.F("{0} élément(s) modifié(s) sur {1}.", result.Changed, result.Visited);
            if (result.Errors.Count > 0)
            {
                await _dialogs.ShowListAsync(L.T("Droits"), L.F("{0} élément(s) n'ont pas pu être modifiés :", result.Errors.Count), result.Errors);
                return false;
            }
            // Relecture : la fenêtre montre l'état réel du serveur.
            _details.Clear();
            foreach (var e in _entries.Take(MaxDetails))
                if (await _rights.GetDetailsAsync(TargetPath(e), CancellationToken.None) is { } d) _details.Add(d);
            InitUnixRights();
            FillGeneral();
            return true;
        }
        catch (OperationCanceledException) { Status = L.T("Annulé (une partie des éléments a peut-être déjà été modifiée)."); return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(L.T("Droits"), ex.Message);
            return false;
        }
        finally { IsBusy = false; }
    }

    /// <summary>Vrai si quelque chose a été modifié (le panneau sera rafraîchi).</summary>
    public bool Changed { get; private set; }

    [RelayCommand]
    private void CancelWork() => _cts?.Cancel();

    /// <summary>« Appliquer » : droits Unix et attributs Windows (la sécurité NTFS s'applique bouton par bouton).</summary>
    [RelayCommand]
    private async Task Apply()
    {
        if (IsBusy) return;
        bool ok = await ApplyUnixAsync();
        if (ok && IsWindowsLocal) ok = await ApplyAttributesAsync();
        if (ok) ok = await ApplyMediaAsync();
        if (ok && Status.Length == 0) Status = L.T("Modifications appliquées.");
    }

    /// <summary>« OK » : appliquer puis fermer (retourne faux s'il y a eu un problème : la fenêtre reste ouverte).</summary>
    public async Task<bool> ApplyAndCloseAsync()
    {
        if (IsBusy) return false;
        bool ok = await ApplyUnixAsync();
        if (ok && IsWindowsLocal) ok = await ApplyAttributesAsync();
        if (ok) ok = await ApplyMediaAsync();
        return ok;
    }

    public void Cancel() => _cts?.Cancel();
}
