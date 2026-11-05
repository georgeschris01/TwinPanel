// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core.Rights;

// Toutes les entrées de cette partie vérifient WindowsLocal (Windows seulement) avant d'appeler la sécurité NTFS ;
// l'analyseur ne suit pas cette vérification jusque dans les lambdas, d'où la suppression de l'avertissement.
#pragma warning disable CA1416

namespace TwinPanel.App.ViewModels;

/// <summary>Partie Windows de la fenêtre Propriétés : attributs et sécurité NTFS (propriétaire, héritage, autorisations).</summary>
public sealed partial class PropertiesViewModel
{
    // ---- Attributs ----------------------------------------------------------------------------------------

    public ObservableCollection<TriStateFlag> AttributeFlags { get; } = [];
    [ObservableProperty] private string _attributesInfo = string.Empty;
    [ObservableProperty] private bool _attributesRecursive;

    /// <summary>Vrai seulement sous Windows (sert aussi de garde pour l'analyseur de compatibilité).</summary>
    [SupportedOSPlatformGuard("windows")]
    private bool WindowsLocal => IsWindowsLocal && OperatingSystem.IsWindows();

    private void LoadWindowsAttributes()
    {
        var attrs = new List<int>();
        foreach (var e in _entries.Take(MaxDetails))
        {
            try { attrs.Add((int)File.GetAttributes(e.FullPath)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        (FileAttributes Flag, string Label)[] flags =
        [
            (FileAttributes.ReadOnly, L.T("Lecture seule")),
            (FileAttributes.Hidden, L.T("Caché")),
            (FileAttributes.System, L.T("Système")),
            (FileAttributes.Archive, L.T("Archive (à sauvegarder)")),
        ];
        foreach (var (flag, label) in flags)
            AttributeFlags.Add(new TriStateFlag((int)flag, label, RightsApplier.Common(attrs, (int)flag), () => { }));
        foreach (var f in AttributeFlags) f.Commit();

        var info = new List<string>();
        if (RightsApplier.Common(attrs, (int)FileAttributes.Compressed) != false) info.Add(L.T("compressé"));
        if (RightsApplier.Common(attrs, (int)FileAttributes.Encrypted) != false) info.Add(L.T("chiffré (EFS)"));
        if (RightsApplier.Common(attrs, (int)FileAttributes.ReparsePoint) != false) info.Add(L.T("lien / jonction"));
        if (RightsApplier.Common(attrs, (int)FileAttributes.Offline) != false || RightsApplier.Common(attrs, 0x400000) != false) info.Add(L.T("dans le nuage (non téléchargé)"));
        AttributesInfo = info.Count > 0 ? L.T("Aussi : ") + string.Join(", ", info) + "." : string.Empty;
    }

    private async Task<bool> ApplyAttributesAsync()
    {
        FileAttributes set = 0, clear = 0;
        foreach (var f in AttributeFlags.Where(f => f.IsChanged && f.Value is not null))
            if (f.Value == true) set |= (FileAttributes)f.Bit; else clear |= (FileAttributes)f.Bit;
        if (set == 0 && clear == 0) return true;
        bool recursive = AttributesRecursive && HasFolders;
        if (recursive && !await _dialogs.ConfirmAsync(L.T("Attributs"), L.T("Changer aussi les attributs de tout le contenu des dossiers choisis ?"))) return false;

        IsBusy = true;
        var errors = new List<string>();
        try
        {
            var paths = _entries.Select(e => e.FullPath).ToList();
            await Task.Run(() =>
            {
                foreach (var root in paths)
                    foreach (var p in LocalTree.Enumerate(root, recursive))
                    {
                        try { WindowsAttributes.Change(p, set, clear); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            if (errors.Count < RightsApplier.MaxErrors) errors.Add($"{p} : {ex.Message}");
                        }
                    }
            });
            Changed = true;
            foreach (var f in AttributeFlags) f.Commit();
            if (errors.Count > 0)
            {
                await _dialogs.ShowListAsync(L.T("Attributs"), L.F("{0} élément(s) n'ont pas pu être modifiés :", errors.Count), errors);
                return false;
            }
            return true;
        }
        finally { IsBusy = false; }
    }

    // ---- Sécurité NTFS ------------------------------------------------------------------------------------

    public ObservableCollection<AclEntry> AclEntries { get; } = [];
    [ObservableProperty] private bool _securityAvailable;
    [ObservableProperty] private string _securityMessage = L.T("Lecture…");
    [ObservableProperty] private string _ntfsOwner = string.Empty;
    [ObservableProperty] private bool _inheritanceEnabled;
    [ObservableProperty] private string _newOwnerInput = string.Empty;
    [ObservableProperty] private bool _ownerRecursive;
    [ObservableProperty] private IReadOnlyList<string> _accountChoices = [];
    [ObservableProperty] private string _accountInput = string.Empty;
    [ObservableProperty] private string _aclType = L.T("Autoriser");
    [ObservableProperty] private NtfsRights.Preset? _aclPreset;
    [ObservableProperty] private NtfsRights.Scope? _aclScope;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSelected))]
    private AclEntry? _selectedAcl;

    public static IReadOnlyList<string> AclTypes { get; } = [L.T("Autoriser"), L.T("Refuser")];
    public static IReadOnlyList<NtfsRights.Preset> AclPresets => NtfsRights.Presets;
    public static IReadOnlyList<NtfsRights.Scope> AclScopes => NtfsRights.FolderScopes;
    public bool IsFolder => IsSingle && _entries[0].IsDirectory;
    public bool CanEditSelected => SelectedAcl is { IsInherited: false };
    public string InheritanceLabel => InheritanceEnabled ? L.T("Couper l'héritage…") : L.T("Rétablir l'héritage du dossier parent");
    partial void OnInheritanceEnabledChanged(bool value) => OnPropertyChanged(nameof(InheritanceLabel));

    /// <summary>Lecture de la sécurité (hors fil de l'interface) ; les erreurs deviennent un message dans l'onglet.</summary>
    private void LoadSecuritySafe()
    {
        if (!WindowsLocal) return;
        var path = _entries[0].FullPath;
        string? message = null;
        NtfsSecurity? sec = null;
        IReadOnlyList<string> accounts = [];
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            var format = root is { Length: > 0 } ? new DriveInfo(root).DriveFormat : string.Empty;
            if (format.Length > 0 && !format.Equals("NTFS", StringComparison.OrdinalIgnoreCase) && !format.Equals("ReFS", StringComparison.OrdinalIgnoreCase))
                message = L.F("Ce disque est en {0} : ce format ne gère ni propriétaire ni autorisations (seulement les attributs).", format);
            else
            {
                sec = WindowsSecurity.Read(path);
                accounts = WindowsSecurity.CommonAccounts();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PlatformNotSupportedException or ArgumentException)
        {
            message = ex is UnauthorizedAccessException
                ? L.T("Windows refuse de montrer la sécurité de cet élément (lance TwinPanel en administrateur pour la voir).")
                : L.T("Sécurité illisible : ") + ex.Message;
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowSecurity(sec, accounts, message));
    }

    private void ShowSecurity(NtfsSecurity? sec, IReadOnlyList<string> accounts, string? message)
    {
        var previous = SelectedAcl;
        AclEntries.Clear();
        if (sec is null)
        {
            SecurityAvailable = false;
            SecurityMessage = message ?? string.Empty;
            return;
        }
        SecurityAvailable = true;
        SecurityMessage = string.Empty;
        NtfsOwner = sec.Owner;
        InheritanceEnabled = sec.InheritanceEnabled;
        foreach (var e in sec.Entries) AclEntries.Add(e);
        if (accounts.Count > 0 || AccountChoices.Count == 0)
            AccountChoices = accounts.Concat(sec.Entries.Select(e => e.Account)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AclPreset ??= NtfsRights.Presets[2];
        AclScope ??= NtfsRights.FolderScopes[0];
        SelectedAcl = previous is null ? null : AclEntries.FirstOrDefault(e => e.Sid == previous.Sid && e.Allow == previous.Allow);
    }

    private async Task ReloadSecurityAsync() => await Task.Run(LoadSecuritySafe);

    partial void OnSelectedAclChanged(AclEntry? value)
    {
        if (value is null) return;
        AccountInput = value.Account;
        AclType = value.Allow ? L.T("Autoriser") : L.T("Refuser");
        AclPreset = NtfsRights.PresetFor(value.Rights) ?? AclPreset;
        AclScope = NtfsRights.FolderScopes.FirstOrDefault(s => s.Inheritance == value.Inheritance && s.Propagation == (value.Propagation & System.Security.AccessControl.PropagationFlags.InheritOnly)) ?? AclScope;
    }

    /// <summary>Exécute une modification de sécurité, puis relit l'état réel.</summary>
    private async Task SecurityActionAsync(string title, Action<string> action)
    {
        if (!WindowsLocal || !IsSingle) return;
        var path = _entries[0].FullPath;
        IsBusy = true;
        try
        {
            await Task.Run(() => action(path));
            Changed = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.Principal.IdentityNotMappedException)
        {
            await _dialogs.ShowErrorAsync(title, ex.Message);
        }
        finally { IsBusy = false; }
        await ReloadSecurityAsync();
    }

    private bool TryReadEditor(out string account, out System.Security.AccessControl.FileSystemRights rights, out bool allow,
        out System.Security.AccessControl.InheritanceFlags inheritance, out System.Security.AccessControl.PropagationFlags propagation)
    {
        account = AccountInput.Trim();
        rights = AclPreset?.Rights ?? System.Security.AccessControl.FileSystemRights.ReadAndExecute;
        allow = AclType != L.T("Refuser");
        var scope = IsFolder ? AclScope ?? NtfsRights.FolderScopes[0] : NtfsRights.FolderScopes[1];
        inheritance = scope.Inheritance;
        propagation = scope.Propagation;
        return account.Length > 0;
    }

    [RelayCommand]
    private async Task AddAcl()
    {
        if (!WindowsLocal) return;
        if (!TryReadEditor(out var account, out var rights, out var allow, out var inh, out var prop))
        {
            await _dialogs.ShowErrorAsync(L.T("Sécurité"), L.T("Choisis ou tape un compte (ex. Utilisateurs, ou PC\\Utilisateur)."));
            return;
        }
        if (!allow && !await _dialogs.ConfirmAsync(L.T("Sécurité"), L.F("Un « Refuser » l'emporte sur les « Autoriser ». Refuser « {0} » à {1} ?", AclPreset?.Label, account))) return;
        await SecurityActionAsync(L.T("Ajouter une autorisation"), path => WindowsSecurity.AddRule(path, account, rights, allow, inh, prop));
    }

    [RelayCommand]
    private async Task ReplaceAcl()
    {
        if (!WindowsLocal || SelectedAcl is not { IsInherited: false } old) return;
        if (!TryReadEditor(out var account, out var rights, out var allow, out var inh, out var prop)) return;
        await SecurityActionAsync(L.T("Modifier l'autorisation"), path => WindowsSecurity.ReplaceRule(path, old, account, rights, allow, inh, prop));
    }

    [RelayCommand]
    private async Task RemoveAcl()
    {
        if (!WindowsLocal || SelectedAcl is not { IsInherited: false } entry) return;
        if (!await _dialogs.ConfirmAsync(L.T("Sécurité"), L.F("Retirer l'autorisation « {0} : {1} » de {2} ?", entry.TypeLabel, entry.RightsLabel, entry.Account))) return;
        await SecurityActionAsync(L.T("Retirer l'autorisation"), path => WindowsSecurity.RemoveRule(path, entry));
    }

    [RelayCommand]
    private async Task ToggleInheritance()
    {
        if (!WindowsLocal) return;
        if (InheritanceEnabled)
        {
            var keep = await _dialogs.ChooseAsync<bool>(L.T("Couper l'héritage"),
                L.T("Les autorisations du dossier parent ne s'appliqueront plus ici. Que faire de celles déjà héritées ?"),
                (L.T("Les garder (copiées ici)"), (bool?)true), (L.T("Les retirer"), (bool?)false), (L.T("Annuler"), (bool?)null));
            if (keep is null) return;
            if (keep == false && !await _dialogs.ConfirmAsync(L.T("Couper l'héritage"),
                    L.T("Sans autorisations héritées, l'élément peut devenir inaccessible s'il ne reste aucune autorisation propre. Continuer ?"))) return;
            await SecurityActionAsync(L.T("Héritage"), path => WindowsSecurity.SetInheritance(path, enabled: false, keepCopies: keep.Value));
        }
        else
        {
            await SecurityActionAsync(L.T("Héritage"), path => WindowsSecurity.SetInheritance(path, enabled: true, keepCopies: false));
        }
    }

    [RelayCommand]
    private async Task ChangeNtfsOwner()
    {
        if (!WindowsLocal) return;
        var account = NewOwnerInput.Trim();
        if (account.Length == 0) { await _dialogs.ShowErrorAsync(L.T("Propriétaire"), L.T("Choisis ou tape le nouveau propriétaire.")); return; }
        bool recursive = OwnerRecursive && IsFolder;
        if (!await _dialogs.ConfirmAsync(L.T("Propriétaire"), L.F("Donner {0} à « {1} » ?", (recursive ? L.T("ce dossier et tout son contenu") : L.T("cet élément")), account))) return;
        var errors = new List<string>();
        await SecurityActionAsync(L.T("Propriétaire"), path =>
        {
            var sid = WindowsSecurity.ResolveAccount(account);
            foreach (var p in LocalTree.Enumerate(path, recursive))
            {
                try { WindowsSecurity.SetOwner(p, sid); }
                catch (Exception ex) when (p != path && ex is IOException or UnauthorizedAccessException)
                {
                    if (errors.Count < RightsApplier.MaxErrors) errors.Add($"{p} : {ex.Message}");
                }
            }
        });
        if (errors.Count > 0) await _dialogs.ShowListAsync(L.T("Propriétaire"), L.F("{0} élément(s) n'ont pas pu changer de propriétaire :", errors.Count), errors);
    }
}

/// <summary>Attributs Windows (sans dépendre de la sécurité NTFS).</summary>
internal static class WindowsAttributes
{
    public static void Change(string path, FileAttributes set, FileAttributes clear)
    {
        var current = File.GetAttributes(path);
        var updated = (current & ~clear) | set;
        if (updated != current) File.SetAttributes(path, updated);
    }
}
