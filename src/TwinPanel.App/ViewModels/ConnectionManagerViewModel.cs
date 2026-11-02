// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.Services;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.ViewModels;

/// <summary>Couleur d'étiquette : valeur enregistrée (#RRGGBB ou vide) et pinceau pour l'affichage.</summary>
public sealed record ColorChoice(string Value, string Label)
{
    public IBrush Brush => Value.Length > 0 && Avalonia.Media.Color.TryParse(Value, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;
    public override string ToString() => Label;
}

/// <summary>Élément d'une liste déroulante : valeur + texte affiché.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Ce que la fenêtre fournit au gestionnaire (fichiers, presse-papiers, aperçu d'import).</summary>
public interface IConnectionManagerHost
{
    Task<string?> PickFileAsync(string title, string? startFile = null);
    Task<string?> SaveFileAsync(string title, string suggestedName);
    Task SetClipboardAsync(string text);
    /// <summary>Liste à cocher des connexions trouvées ; null si annulé.</summary>
    Task<(IReadOnlyList<ImportedConnection> Selected, bool WithPasswords)?> ChooseImportsAsync(IReadOnlyList<ImportedConnection> found);
    /// <summary>Choix d'une connexion SSH où installer une clé publique ; null si annulé.</summary>
    Task<(ConnectionProfile Profile, bool UseKey)?> ChooseServerForKeyAsync(IReadOnlyList<ConnectionProfile> profiles, string keyName);
}

public abstract class ConnectionRow;

public sealed class GroupRow(string name, int count) : ConnectionRow
{
    public string Name { get; } = name.Length == 0 ? L.T("Sans groupe") : name;
    public string Count { get; } = count.ToString();
}

public sealed class ProfileRow(ConnectionProfile profile, bool connected) : ConnectionRow
{
    public ConnectionProfile Profile { get; } = profile;
    public string Name => Profile.Name;
    public string Summary => Profile.Summary;
    public string Protocol => RemoteProtocols.Scheme(Profile.Protocol).ToUpperInvariant();
    public bool IsConnected { get; } = connected;
    public bool IsUnencrypted => RemoteProtocols.IsUnencrypted(Profile.Protocol);
    public IBrush ColorBrush => Profile.Color.Length > 0 && Color.TryParse(Profile.Color, out var c)
        ? new SolidColorBrush(c) : Brushes.Transparent;
}

public sealed class KeyRow(KeychainKey key, int usedBy)
{
    public KeychainKey Key { get; } = key;
    public string Name => Key.Name;
    public string Details => $"{Key.KeyType} — {Key.Fingerprint}";
    public string Usage => usedBy == 0 ? L.T("inutilisée") : L.F("utilisée par {0} connexion(s)", usedBy);
    public string Protection => Key.HasPassphrase ? (Key.SavePassphrase ? L.T("phrase de passe enregistrée") : L.T("phrase de passe demandée")) : L.T("sans phrase de passe");
}

/// <summary>Gestionnaire de connexions (Ctrl+F) : liste, fiche en onglets, trousseau, serveurs connus, coffre.</summary>
public partial class ConnectionManagerViewModel : ObservableObject
{
    private readonly ConnectionStore _store;
    private readonly RemoteSessionManager _sessions;
    private readonly IConnectionPrompts _prompts;
    private readonly IDialogService _dialogs;
    private IConnectionManagerHost? _host;
    private bool _loading;

    public ConnectionManagerViewModel(ConnectionStore store, RemoteSessionManager sessions, IConnectionPrompts prompts, IDialogService dialogs)
    {
        _store = store;
        _sessions = sessions;
        _prompts = prompts;
        _dialogs = dialogs;
        Editor.PropertyChanged += OnEditorChanged;
        RefreshAll();
        if (Rows.OfType<ProfileRow>().FirstOrDefault() is { } first) SelectedRow = first;
    }

    public void AttachHost(IConnectionManagerHost host) => _host = host;

    /// <summary>Connexion à ouvrir quand la fenêtre se ferme (« Connecter »).</summary>
    public ConnectionProfile? ConnectRequest { get; private set; }
    public event EventHandler? CloseRequested;

    public ProfileEditor Editor { get; } = new();

    public ObservableCollection<ConnectionRow> Rows { get; } = [];
    public ObservableCollection<KeyRow> Keys { get; } = [];
    public ObservableCollection<KeychainIdentity> Identities { get; } = [];
    public ObservableCollection<KnownSshHost> KnownSsh { get; } = [];
    public ObservableCollection<TrustedCertificate> TrustedCertificates { get; } = [];

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private ConnectionRow? _selectedRow;
    [ObservableProperty] private bool _hasProfile;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private KeyRow? _selectedKey;
    [ObservableProperty] private KnownSshHost? _selectedKnownSsh;
    [ObservableProperty] private TrustedCertificate? _selectedCertificate;
    [ObservableProperty] private string _vaultStatus = string.Empty;
    [ObservableProperty] private bool _hasMaster;
    [ObservableProperty] private decimal? _autoLockMinutes;

    // Identité en cours de modification (onglet Trousseau)
    [ObservableProperty] private KeychainIdentity? _selectedIdentity;
    [ObservableProperty] private string _identityName = string.Empty;
    [ObservableProperty] private string _identityUser = string.Empty;
    [ObservableProperty] private string _identityPassword = string.Empty;
    [ObservableProperty] private bool _identitySavePassword;
    [ObservableProperty] private Choice<Guid?>? _identityKey;
    [ObservableProperty] private string _identityPasswordHint = string.Empty;

    public ObservableCollection<Choice<Guid?>> KeyChoices { get; } = [];

    public static IReadOnlyList<Choice<ImportSource>> ImportSources { get; } =
        Enum.GetValues<ImportSource>().Select(s => new Choice<ImportSource>(s, ConnectionImport.Label(s))).ToList();

    private ConnectionProfile? _current;   // fiche affichée (null : aucune)
    private bool _currentIsNew;

    // ---- Liste ----------------------------------------------------------------------------------------

    partial void OnSearchChanged(string value) => RefreshRows();

    private void RefreshAll()
    {
        RefreshRows();
        RefreshKeys();
        RefreshKnown();
        RefreshVault();
        Editor.SetLists(_store, _current?.Id);
    }

    private void RefreshRows()
    {
        var keep = (SelectedRow as ProfileRow)?.Profile.Id ?? _current?.Id;
        var words = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _store.Profiles.Where(p => words.All(w =>
                p.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase) || p.Host.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                || p.Group.Contains(w, StringComparison.CurrentCultureIgnoreCase) || p.UserName.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                || p.Tags.Any(t => t.Contains(w, StringComparison.CurrentCultureIgnoreCase))))
            .OrderBy(p => p.Group.Length == 0 ? 1 : 0).ThenBy(p => p.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        _loading = true;
        Rows.Clear();
        foreach (var g in matches.GroupBy(p => p.Group))
        {
            Rows.Add(new GroupRow(g.Key, g.Count()));
            foreach (var p in g) Rows.Add(new ProfileRow(p, _sessions.FindByProfile(p.Id) is not null));
        }
        _loading = false;
        SelectedRow = Rows.OfType<ProfileRow>().FirstOrDefault(r => r.Profile.Id == keep) ?? SelectedRow;
    }

    partial void OnSelectedRowChanged(ConnectionRow? oldValue, ConnectionRow? newValue)
    {
        if (_loading) return;
        if (newValue is not ProfileRow row) return;
        if (_current is not null && row.Profile.Id == _current.Id) return;
        _ = SwitchToAsync(row.Profile, oldValue);
    }

    private async Task SwitchToAsync(ConnectionProfile target, ConnectionRow? previous)
    {
        if (!await ConfirmLeaveAsync())
        {
            _loading = true;
            SelectedRow = previous;
            _loading = false;
            return;
        }
        Show(target.Clone(), isNew: false);
    }

    /// <summary>Modifications non enregistrées : on propose de les enregistrer. False : rester sur la fiche.</summary>
    private async Task<bool> ConfirmLeaveAsync()
    {
        if (_current is null || !Editor.IsDirty) return true;
        var choice = await _dialogs.ChooseAsync<int>(L.T("Modifications"), L.F("Enregistrer les modifications de « {0} » ?", Editor.Name),
            (L.T("Enregistrer"), 1), (L.T("Ne pas enregistrer"), 2), (L.T("Annuler"), null));
        if (choice is null) return false;
        if (choice == 1) return await SaveCoreAsync();
        return true;
    }

    private void Show(ConnectionProfile profile, bool isNew)
    {
        _current = profile;
        _currentIsNew = isNew;
        Editor.SetLists(_store, profile.Id);
        Editor.Load(profile, _store);
        HasProfile = true;
        Message = isNew ? L.T("Nouvelle connexion : remplis au moins l'hôte et l'identifiant, puis Enregistrer.") : string.Empty;
    }

    // ---- Fiches -------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task NewProfile()
    {
        if (!await ConfirmLeaveAsync()) return;
        var p = new ConnectionProfile
        {
            Name = _store.UniqueName(L.T("Nouvelle connexion")),
            Group = (SelectedRow as ProfileRow)?.Profile.Group ?? string.Empty,
        };
        _loading = true;
        SelectedRow = null;
        _loading = false;
        Show(p, isNew: true);
    }

    [RelayCommand]
    private async Task DuplicateProfile()
    {
        if (_current is null || !await ConfirmLeaveAsync()) return;
        var copy = _store.FindProfile(_current.Id)?.Clone(newId: true) ?? _current.Clone(newId: true);
        copy.Name = _store.UniqueName(copy.Name + L.T(" (copie)"));
        copy.SavePassword = copy.SaveProxyPassword = copy.SaveClientCertificatePassword = false;
        _loading = true;
        SelectedRow = null;
        _loading = false;
        Show(copy, isNew: true);
        Message = L.T("Copie créée (sans les mots de passe). Enregistre-la pour la garder.");
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (_current is null) return;
        if (_currentIsNew) { _current = null; HasProfile = false; return; }
        var users = _store.Profiles.Where(p => p.JumpHostId == _current.Id).Select(p => p.Name).ToList();
        var extra = users.Count > 0 ? L.F("\n\nElle sert de rebond à : {0} (ce rebond sera retiré).", string.Join(", ", users)) : string.Empty;
        if (!await _dialogs.ConfirmAsync(L.T("Supprimer"), L.F("Supprimer la connexion « {0} » et ses mots de passe enregistrés ?{1}", _current.Name, extra))) return;
        _store.DeleteProfile(_current.Id);
        _current = null;
        HasProfile = false;
        Editor.IsDirty = false;
        RefreshRows();
        if (Rows.OfType<ProfileRow>().FirstOrDefault() is { } first) SelectedRow = first;
    }

    [RelayCommand]
    private async Task Save() => await SaveCoreAsync();

    private async Task<bool> SaveCoreAsync()
    {
        if (_current is null) return false;
        var candidate = _current.Clone();
        Editor.Apply(candidate);
        var errors = candidate.Validate();
        if (_store.Profiles.Any(p => p.Id != candidate.Id && string.Equals(p.Name, candidate.Name, StringComparison.CurrentCultureIgnoreCase)))
            errors = [.. errors, L.T("Une autre connexion porte déjà ce nom.")];
        if (errors.Count > 0) { Message = string.Join(Environment.NewLine, errors); return false; }

        // Les secrets tapés sont rangés dans le coffre (il faut parfois le déverrouiller).
        if (!await EnsureVaultForWriteAsync(Editor.HasNewSecrets)) return false;
        try
        {
            _store.SaveProfile(candidate);
            if (candidate.SavePassword && Editor.Password.Length > 0) _store.Vault.Set(SecretIds.Password(candidate.Id), Editor.Password);
            if (candidate.SaveProxyPassword && Editor.ProxyPassword.Length > 0) _store.Vault.Set(SecretIds.ProxyPassword(candidate.Id), Editor.ProxyPassword);
            if (candidate.SaveClientCertificatePassword && Editor.CertificatePassword.Length > 0)
                _store.Vault.Set(SecretIds.CertificatePassword(candidate.Id), Editor.CertificatePassword);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or VaultLockedException or InvalidOperationException
                                       or System.Security.Cryptography.CryptographicException)
        {
            Message = L.T("Enregistrement impossible : ") + ex.Message;
            return false;
        }

        _current = candidate;
        _currentIsNew = false;
        Editor.Load(candidate, _store);
        RefreshRows();
        SelectedRow = Rows.OfType<ProfileRow>().FirstOrDefault(r => r.Profile.Id == candidate.Id);
        Message = L.F("« {0} » enregistrée.", candidate.Name);
        return true;
    }

    private async Task<bool> EnsureVaultForWriteAsync(bool needed)
    {
        if (!needed || _store.Vault.IsUnlocked) return true;
        if (await _prompts.UnlockVaultAsync(_store.Vault)) { RefreshVault(); return true; }
        Message = L.T("Coffre verrouillé : les mots de passe n'ont pas été enregistrés.");
        return false;
    }

    [RelayCommand]
    private async Task Test()
    {
        if (_current is null || IsBusy) return;
        var candidate = _current.Clone();
        Editor.Apply(candidate);
        if (candidate.Name.Length == 0) candidate.Name = candidate.Host;
        var errors = candidate.Validate();
        if (errors.Count > 0) { Message = string.Join(Environment.NewLine, errors); return; }
        IsBusy = true;
        Message = L.F("Connexion à {0}…", candidate.Host);
        try
        {
            var prompts = new PrefilledPrompts(_prompts, Editor.Password, Editor.ProxyPassword, Editor.CertificatePassword);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(candidate.TimeoutSeconds * 3));
            // Hors du fil de l'interface : les questions (clé du serveur…) y sont ramenées toutes seules.
            Message = await Task.Run(() => RemoteConnector.TestAsync(candidate, _store, prompts, cts.Token));
        }
        catch (OperationCanceledException) { Message = L.T("Test annulé."); }
        catch (Exception ex) when (ex is RemoteException or IOException or InvalidOperationException)
        {
            Message = L.T("Échec : ") + ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshKnown();
        }
    }

    /// <summary>Enregistre si besoin, puis ferme la fenêtre en demandant l'ouverture de la connexion.</summary>
    [RelayCommand]
    private async Task Connect()
    {
        if (_current is null) return;
        if (_currentIsNew || Editor.IsDirty)
        {
            if (!await SaveCoreAsync()) return;
        }
        ConnectRequest = _store.FindProfile(_current.Id);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Double-clic sur une connexion de la liste.</summary>
    public Task ConnectRowAsync(ProfileRow row) => _current?.Id == row.Profile.Id ? Connect() : Task.CompletedTask;

    // ---- Import / export ----------------------------------------------------------------------------------

    [RelayCommand]
    private async Task Import(Choice<ImportSource>? source)
    {
        if (_host is null) return;
        IReadOnlyList<ImportedConnection> found;
        try
        {
            if (source is null)
            {
                var file = await _host.PickFileAsync(L.T("Fichier exporté par TwinPanel (.json)"));
                if (file is null) return;
                var doc = ConnectionStore.ReadExport(file);
                found = doc.Profiles.Select(p => new ImportedConnection { Profile = p, Source = "TwinPanel" }).ToList();
            }
            else
            {
                found = ConnectionImport.Read(source.Value);
                if (found.Count == 0 && source.Value is not ImportSource.Putty)
                {
                    var file = await _host.PickFileAsync(L.F("{0} : aucun fichier trouvé à l'endroit habituel. Choisis-le :", source.Label),
                        ConnectionImport.DefaultFiles(source.Value).FirstOrDefault());
                    if (file is null) return;
                    found = ConnectionImport.Read(source.Value, file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or System.Text.Json.JsonException or InvalidDataException)
        {
            await _dialogs.ShowErrorAsync(L.T("Importer"), L.T("Lecture impossible : ") + ex.Message);
            return;
        }
        if (found.Count == 0)
        {
            await _dialogs.ShowErrorAsync(L.T("Importer"), L.T("Aucune connexion trouvée."));
            return;
        }
        if (await _host.ChooseImportsAsync(found) is not { } choice || choice.Selected.Count == 0) return;
        if (!await EnsureVaultForWriteAsync(true)) return;

        // Clés privées : lues et rangées dans le trousseau (phrase de passe demandée si besoin).
        var keys = new Dictionary<string, KeychainKey?>(StringComparer.OrdinalIgnoreCase);
        var keyProblems = new List<string>();
        foreach (var file in choice.Selected.Select(c => c.KeyFile).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
            keys[file] = await ImportKeyFileAsync(file, keyProblems);

        int n = _store.ImportCandidates(choice.Selected, choice.WithPasswords, f => keys.GetValueOrDefault(f));
        RefreshAll();
        var msg = L.F("{0} connexion(s) importée(s).", n);
        if (keyProblems.Count > 0) msg += L.F(" Clés non reprises : {0} (ces connexions passent au mot de passe).", string.Join(" ; ", keyProblems));
        Message = msg;
    }

    [RelayCommand]
    private async Task Export()
    {
        if (_host is null || _store.Profiles.Count == 0) return;
        var file = await _host.SaveFileAsync(L.T("Exporter les connexions (sans mots de passe)"), "connexions-twinpanel.json");
        if (file is null) return;
        try
        {
            _store.Export(_store.Profiles, file);
            Message = L.F("{0} connexion(s) exportée(s) dans {1} — sans aucun mot de passe ni clé privée.", _store.Profiles.Count, Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await _dialogs.ShowErrorAsync(L.T("Exporter"), ex.Message); }
    }

    // ---- Trousseau : clés ------------------------------------------------------------------------------------

    private void RefreshKeys()
    {
        var keep = SelectedKey?.Key.Id;
        Keys.Clear();
        foreach (var k in _store.Keys.OrderBy(k => k.Name, StringComparer.CurrentCultureIgnoreCase))
            Keys.Add(new KeyRow(k, _store.ProfilesUsingKey(k.Id).Count()));
        SelectedKey = Keys.FirstOrDefault(k => k.Key.Id == keep) ?? Keys.FirstOrDefault();

        KeyChoices.Clear();
        KeyChoices.Add(new Choice<Guid?>(null, L.T("(aucune)")));
        foreach (var k in _store.Keys) KeyChoices.Add(new Choice<Guid?>(k.Id, $"{k.Name} ({k.KeyType})"));

        var keepIdentity = SelectedIdentity?.Id;
        Identities.Clear();
        foreach (var i in _store.Identities.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)) Identities.Add(i);
        SelectedIdentity = Identities.FirstOrDefault(i => i.Id == keepIdentity) ?? Identities.FirstOrDefault();
        Editor.SetLists(_store, _current?.Id);
    }

    private async Task<KeychainKey?> ImportKeyFileAsync(string file, List<string>? problems = null)
    {
        string text;
        try { text = await File.ReadAllTextAsync(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems?.Add($"{Path.GetFileName(file)} ({ex.Message})");
            return null;
        }
        string? passphrase = null;
        bool save = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return _store.AddKey(Path.GetFileNameWithoutExtension(file), text, passphrase, save, file);
            }
            catch (KeyPassphraseException ex)
            {
                var a = await _prompts.AskSecretAsync(new SecretRequest(L.T("Phrase de passe"),
                    L.F("{0}Phrase de passe de la clé {1} :", (attempt > 0 ? ex.Message + "\n" : string.Empty), Path.GetFileName(file)), CanRemember: true));
                if (a is null) { problems?.Add(L.F("{0} (phrase de passe non donnée)", Path.GetFileName(file))); return null; }
                passphrase = a.Value;
                save = a.Remember;
            }
            catch (InvalidDataException ex)
            {
                problems?.Add($"{Path.GetFileName(file)} ({ex.Message})");
                return null;
            }
        }
        problems?.Add(L.F("{0} (phrase de passe incorrecte)", Path.GetFileName(file)));
        return null;
    }

    /// <summary>Depuis la fiche (onglet Authentification) : importer une clé et la choisir aussitôt pour cette connexion.</summary>
    [RelayCommand]
    private async Task ImportKeyForProfile()
    {
        if (await ImportKeyCoreAsync() is { } key) SelectKeyInEditor(key);
    }

    /// <summary>Depuis la fiche : créer une clé Ed25519 et la choisir aussitôt pour cette connexion.</summary>
    [RelayCommand]
    private async Task GenerateKeyForProfile()
    {
        if (await GenerateKeyCoreAsync() is { } key) SelectKeyInEditor(key);
    }

    private void SelectKeyInEditor(KeychainKey key)
    {
        Editor.SetLists(_store, _current?.Id);
        Editor.Key = Editor.KeyChoices.FirstOrDefault(c => c.Value == key.Id) ?? Editor.Key;
        if (Editor.Auth.Value is not (AuthMode.Key or AuthMode.KeyAndPassword))
            Editor.Auth = ProfileEditor.AuthModes.First(a => a.Value == AuthMode.Key);
    }

    [RelayCommand]
    private Task ImportKey() => ImportKeyCoreAsync();

    private async Task<KeychainKey?> ImportKeyCoreAsync()
    {
        if (_host is null) return null;
        var file = await _host.PickFileAsync(L.T("Clé privée SSH (OpenSSH, PEM ou PuTTY .ppk)"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519"));
        if (file is null || !await EnsureVaultForWriteAsync(true)) return null;
        var problems = new List<string>();
        var key = await ImportKeyFileAsync(file, problems);
        RefreshKeys();
        if (key is not null)
        {
            SelectedKey = Keys.FirstOrDefault(k => k.Key.Id == key.Id);
            Message = L.F("Clé « {0} » ajoutée au trousseau (copiée dans le coffre chiffré ; le fichier d'origine n'est plus nécessaire).", key.Name);
        }
        else if (problems.Count > 0) await _dialogs.ShowErrorAsync(L.T("Importer une clé"), string.Join(Environment.NewLine, problems));
        return key;
    }

    [RelayCommand]
    private Task GenerateKey() => GenerateKeyCoreAsync();

    private async Task<KeychainKey?> GenerateKeyCoreAsync()
    {
        var name = await _dialogs.PromptAsync(L.T("Nouvelle clé"), L.T("Nom de la clé (il sert aussi de commentaire dans la clé publique) :"),
            $"{Environment.UserName}@twinpanel");
        if (string.IsNullOrWhiteSpace(name) || !await EnsureVaultForWriteAsync(true)) return null;
        var text = await Task.Run(() => SshKeys.GenerateEd25519(name.Trim()));
        var key = _store.AddKey(name.Trim(), text, null, false, L.T("créée par TwinPanel"));
        RefreshKeys();
        SelectedKey = Keys.FirstOrDefault(k => k.Key.Id == key.Id);
        if (_host is not null) await _host.SetClipboardAsync(key.PublicKey);
        Message = L.T("Clé Ed25519 créée. Sa partie publique est copiée : colle-la dans ~/.ssh/authorized_keys sur le serveur.");
        return key;
    }

    [RelayCommand]
    private async Task CopyPublicKey()
    {
        if (SelectedKey is not { } k || _host is null) return;
        await _host.SetClipboardAsync(k.Key.PublicKey);
        Message = L.T("Clé publique copiée (à coller dans ~/.ssh/authorized_keys sur le serveur).");
    }

    /// <summary>
    /// Installe la clé publique choisie sur un serveur (dans ~/.ssh/authorized_keys), par une connexion qui marche déjà
    /// (en général par mot de passe) ; propose ensuite d'utiliser la clé pour cette connexion.
    /// </summary>
    [RelayCommand]
    private async Task InstallPublicKey()
    {
        if (SelectedKey is not { } row || _host is null || IsBusy) return;
        var ssh = _store.Profiles.Where(p => RemoteProtocols.IsSsh(p.Protocol)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (ssh.Count == 0)
        {
            await _dialogs.ShowErrorAsync(L.T("Installer la clé"), L.T("Crée d'abord une connexion SFTP ou SCP vers le serveur (par mot de passe), puis reviens ici."));
            return;
        }
        if (await _host.ChooseServerForKeyAsync(ssh, row.Key.Name) is not { } choice) return;
        var profile = choice.Profile;
        IsBusy = true;
        Message = L.F("Installation de la clé sur {0}…", profile.Name);
        try
        {
            var added = await Task.Run(async () =>
            {
                var ctx = await RemoteConnector.PrepareAsync(profile, _store, _prompts) ?? throw new OperationCanceledException();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(profile.TimeoutSeconds * 3));
                await using var fs = await RemoteConnector.OpenAsync(ctx, cts.Token);
                var result = await AuthorizedKeys.InstallAsync(fs, row.Key.PublicKey, cts.Token);
                ctx.Credentials.CommitRemembered();
                return result;
            });
            Message = added
                ? L.F("Clé « {0} » installée sur {1} (~/.ssh/authorized_keys).", row.Key.Name, profile.Name)
                : L.F("La clé « {0} » était déjà installée sur {1}.", row.Key.Name, profile.Name);
            if (choice.UseKey)
            {
                profile.KeyId = row.Key.Id;
                profile.IdentityId = null;
                if (profile.Auth is not (AuthMode.Key or AuthMode.KeyAndPassword)) profile.Auth = AuthMode.Key;
                _store.SaveProfile(profile);
                if (_current?.Id == profile.Id) Show(profile.Clone(), isNew: false);
                RefreshAll();
                Message += L.T(" La connexion utilise maintenant cette clé : fais « Tester » pour vérifier.");
            }
        }
        catch (OperationCanceledException) { Message = L.T("Installation annulée."); }
        catch (Exception ex) when (ex is RemoteException or IOException or InvalidOperationException or ArgumentException)
        {
            Message = L.T("Échec : ") + ex.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshKnown();
        }
    }

    [RelayCommand]
    private async Task ExportPublicKey()
    {
        if (SelectedKey is not { } k || _host is null) return;
        var file = await _host.SaveFileAsync(L.T("Enregistrer la clé publique"), SafeFileName(k.Name) + ".pub");
        if (file is null) return;
        await File.WriteAllTextAsync(file, k.Key.PublicKey + "\n");
        Message = L.F("Clé publique enregistrée : {0}", file);
    }

    [RelayCommand]
    private async Task ExportPrivateKey()
    {
        if (SelectedKey is not { } k || _host is null) return;
        if (!await _dialogs.ConfirmAsync(L.T("Exporter la clé privée"),
                L.T("La clé privée va être écrite dans un fichier, telle qu'elle est dans le coffre.\n") +
                L.T("Toute personne qui obtient ce fichier peut se connecter à tes serveurs.\n") +
                L.T("Range-le en lieu sûr et supprime-le quand il ne sert plus. Continuer ?"))) return;
        if (!await EnsureVaultForWriteAsync(true)) return;
        var file = await _host.SaveFileAsync(L.T("Enregistrer la clé privée"), SafeFileName(k.Name));
        if (file is null) return;
        var text = _store.Vault.Get(SecretIds.KeyMaterial(k.Key.Id));
        if (text is null) { Message = L.T("Clé introuvable dans le coffre."); return; }
        await File.WriteAllTextAsync(file, text);
        Message = L.F("Clé privée exportée : {0}", file);
    }

    [RelayCommand]
    private async Task RenameKey()
    {
        if (SelectedKey is not { } k) return;
        var name = await _dialogs.PromptAsync(L.T("Renommer la clé"), L.T("Nouveau nom :"), k.Key.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        k.Key.Name = name.Trim();
        _store.SaveKey(k.Key);
        RefreshKeys();
    }

    [RelayCommand]
    private async Task ToggleKeyPassphrase()
    {
        if (SelectedKey is not { } row || !row.Key.HasPassphrase) return;
        var key = row.Key;
        if (key.SavePassphrase)
        {
            key.SavePassphrase = false;
            _store.SaveKey(key);
            Message = L.T("La phrase de passe sera demandée à chaque connexion.");
        }
        else
        {
            if (!await EnsureVaultForWriteAsync(true)) return;
            var a = await _prompts.AskSecretAsync(new SecretRequest(L.T("Phrase de passe"), L.F("Phrase de passe de « {0} » à enregistrer :", key.Name), CanRemember: false));
            if (a is null) return;
            try { SshKeys.Inspect(_store.Vault.Get(SecretIds.KeyMaterial(key.Id)) ?? string.Empty, a.Value); }
            catch (Exception ex) when (ex is KeyPassphraseException or InvalidDataException) { Message = ex.Message; return; }
            _store.Vault.Set(SecretIds.KeyPassphrase(key.Id), a.Value);
            key.SavePassphrase = true;
            _store.SaveKey(key);
            Message = L.T("Phrase de passe vérifiée et enregistrée dans le coffre.");
        }
        RefreshKeys();
    }

    [RelayCommand]
    private async Task DeleteKey()
    {
        if (SelectedKey is not { } k) return;
        var users = _store.ProfilesUsingKey(k.Key.Id).Select(p => p.Name).ToList();
        var warn = users.Count > 0 ? L.F("\n\nUtilisée par : {0}. Ces connexions n'auront plus de clé.", string.Join(", ", users)) : string.Empty;
        if (!await _dialogs.ConfirmAsync(L.T("Supprimer la clé"), L.F("Supprimer définitivement la clé « {0} » du trousseau ?{1}", k.Key.Name, warn))) return;
        _store.DeleteKey(k.Key.Id);
        RefreshKeys();
    }

    private static string SafeFileName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => bad.Contains(c) || c == '@' ? '_' : c).ToArray());
        return s.Length == 0 ? "cle" : s;
    }

    // ---- Trousseau : identités ---------------------------------------------------------------------------------

    partial void OnSelectedIdentityChanged(KeychainIdentity? value)
    {
        IdentityName = value?.Name ?? string.Empty;
        IdentityUser = value?.UserName ?? string.Empty;
        IdentityPassword = string.Empty;
        IdentitySavePassword = value?.SavePassword ?? false;
        IdentityKey = KeyChoices.FirstOrDefault(c => c.Value == value?.KeyId) ?? KeyChoices.FirstOrDefault();
        IdentityPasswordHint = value?.SavePassword == true ? L.T("(enregistré — laisser vide pour le garder)") : string.Empty;
    }

    [RelayCommand]
    private void NewIdentity()
    {
        var i = new KeychainIdentity { Name = L.T("Nouvelle identité") };
        _store.SaveIdentity(i);
        RefreshKeys();
        SelectedIdentity = Identities.FirstOrDefault(x => x.Id == i.Id);
    }

    [RelayCommand]
    private async Task SaveIdentity()
    {
        if (SelectedIdentity is not { } i) return;
        if (string.IsNullOrWhiteSpace(IdentityName) || string.IsNullOrWhiteSpace(IdentityUser))
        {
            Message = L.T("Une identité a besoin d'un nom et d'un identifiant.");
            return;
        }
        if (!await EnsureVaultForWriteAsync(IdentitySavePassword && IdentityPassword.Length > 0)) return;
        i.Name = IdentityName.Trim();
        i.UserName = IdentityUser.Trim();
        i.KeyId = IdentityKey?.Value;
        i.SavePassword = IdentitySavePassword;
        _store.SaveIdentity(i);
        if (i.SavePassword && IdentityPassword.Length > 0) _store.Vault.Set(SecretIds.IdentityPassword(i.Id), IdentityPassword);
        RefreshKeys();
        Message = L.F("Identité « {0} » enregistrée.", i.Name);
    }

    [RelayCommand]
    private async Task DeleteIdentity()
    {
        if (SelectedIdentity is not { } i) return;
        var users = _store.Profiles.Where(p => p.IdentityId == i.Id).Select(p => p.Name).ToList();
        var warn = users.Count > 0 ? L.F("\n\nUtilisée par : {0}.", string.Join(", ", users)) : string.Empty;
        if (!await _dialogs.ConfirmAsync(L.T("Supprimer l'identité"), L.F("Supprimer l'identité « {0} » ?{1}", i.Name, warn))) return;
        _store.DeleteIdentity(i.Id);
        RefreshKeys();
    }

    // ---- Serveurs connus -------------------------------------------------------------------------------------

    private void RefreshKnown()
    {
        KnownSsh.Clear();
        foreach (var h in _store.KnownHosts.SshHosts.OrderBy(h => h.Host)) KnownSsh.Add(h);
        TrustedCertificates.Clear();
        foreach (var c in _store.KnownHosts.Certificates.OrderBy(c => c.Host)) TrustedCertificates.Add(c);
    }

    [RelayCommand]
    private async Task ForgetKnownSsh()
    {
        if (SelectedKnownSsh is not { } h) return;
        if (!await _dialogs.ConfirmAsync(L.T("Oublier"), L.F("Oublier la clé de {0}:{1} ? Elle sera redemandée à la prochaine connexion.", h.Host, h.Port))) return;
        _store.KnownHosts.Forget(h);
        RefreshKnown();
    }

    [RelayCommand]
    private async Task ForgetCertificate()
    {
        if (SelectedCertificate is not { } c) return;
        if (!await _dialogs.ConfirmAsync(L.T("Oublier"), L.F("Ne plus faire confiance au certificat de {0}:{1} ?", c.Host, c.Port))) return;
        _store.KnownHosts.Forget(c);
        RefreshKnown();
    }

    // ---- Coffre ---------------------------------------------------------------------------------------------

    private void RefreshVault()
    {
        var v = _store.Vault;
        HasMaster = v.HasMasterPassword;
        AutoLockMinutes = v.AutoLockMinutes;
        VaultStatus = !v.HasMasterPassword
            ? L.T("Les secrets sont chiffrés par Windows (DPAPI) : seul ton compte Windows, sur ce PC, peut les lire. Aucun mot de passe maître.")
            : v.IsUnlocked
                ? L.T("Double protection : Windows + mot de passe maître (AES-256-GCM, PBKDF2 600 000 tours). Le coffre est ouvert.")
                : L.T("Double protection : Windows + mot de passe maître. Le coffre est verrouillé.");
    }

    partial void OnAutoLockMinutesChanged(decimal? value)
    {
        if (value is { } v && (int)v != _store.Vault.AutoLockMinutes && v >= 0) _store.Vault.AutoLockMinutes = (int)v;
    }

    [RelayCommand]
    private async Task SetMasterPassword()
    {
        if (_store.Vault.HasMasterPassword && !_store.Vault.IsUnlocked && !await _prompts.UnlockVaultAsync(_store.Vault)) return;
        var first = await _prompts.AskSecretAsync(new SecretRequest(L.T("Mot de passe maître"),
            L.T("Nouveau mot de passe maître (au moins 8 caractères). ATTENTION : s'il est oublié, les mots de passe enregistrés seront perdus."), false));
        if (first is null) return;
        if (first.Value.Length < 8) { Message = L.T("Le mot de passe maître doit avoir au moins 8 caractères."); return; }
        var second = await _prompts.AskSecretAsync(new SecretRequest(L.T("Mot de passe maître"), L.T("Retape-le pour confirmer :"), false));
        if (second is null) return;
        if (second.Value != first.Value) { Message = L.T("Les deux mots de passe ne sont pas identiques."); return; }
        IsBusy = true;
        try { await Task.Run(() => _store.Vault.SetMasterPassword(first.Value)); Message = L.T("Mot de passe maître enregistré."); }
        finally { IsBusy = false; RefreshVault(); }
    }

    [RelayCommand]
    private async Task RemoveMasterPassword()
    {
        if (!_store.Vault.HasMasterPassword) return;
        if (!await _dialogs.ConfirmAsync(L.T("Retirer le mot de passe maître"),
                L.T("Les secrets resteront chiffrés par Windows, mais sans mot de passe maître. Continuer ?"))) return;
        if (!_store.Vault.IsUnlocked && !await _prompts.UnlockVaultAsync(_store.Vault)) return;
        await Task.Run(() => _store.Vault.SetMasterPassword(null));
        RefreshVault();
        Message = L.T("Mot de passe maître retiré.");
    }

    [RelayCommand]
    private void LockVault()
    {
        _store.Vault.Lock();
        RefreshVault();
        Message = L.T("Coffre verrouillé.");
    }

    // ---- Divers ---------------------------------------------------------------------------------------------

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileEditor.IsDirty) && Editor.IsDirty && Message.EndsWith(L.T("enregistrée."), StringComparison.Ordinal)) Message = string.Empty;
    }

    /// <summary>Fermeture de la fenêtre : false pour l'annuler (modifications à garder).</summary>
    public Task<bool> CanCloseAsync() => ConfirmLeaveAsync();
}

/// <summary>Fiche d'une connexion en cours de modification (copie : rien n'est écrit avant « Enregistrer »).</summary>
public partial class ProfileEditor : ObservableObject
{
    private bool _loading;

    public static IReadOnlyList<Choice<RemoteProtocol>> Protocols { get; } =
        Enum.GetValues<RemoteProtocol>().Select(p => new Choice<RemoteProtocol>(p, RemoteProtocols.Label(p))).ToList();

    public static IReadOnlyList<Choice<AuthMode>> AuthModes { get; } =
    [
        new(AuthMode.Password, L.T("Mot de passe")),
        new(AuthMode.Key, L.T("Clé SSH")),
        new(AuthMode.KeyAndPassword, L.T("Clé SSH puis mot de passe")),
        new(AuthMode.Anonymous, L.T("Anonyme (FTP)")),
    ];

    public static IReadOnlyList<Choice<ProxyKind>> ProxyKinds { get; } =
        [new(ProxyKind.None, L.T("Aucun")), new(ProxyKind.Http, "HTTP (CONNECT)"), new(ProxyKind.Socks4, "SOCKS 4"), new(ProxyKind.Socks5, "SOCKS 5")];

    public static IReadOnlyList<Choice<FtpDataMode>> DataModes { get; } =
        [new(FtpDataMode.AutoPassive, L.T("Passif automatique (recommandé)")), new(FtpDataMode.Passive, L.T("Passif (PASV)")), new(FtpDataMode.Active, L.T("Actif"))];

    // Nom complet : la propriété TlsMinimum de cette classe cache le type du même nom.
    public static IReadOnlyList<Choice<TwinPanel.Core.Remote.TlsMinimum>> TlsLevels { get; } =
        [new(TwinPanel.Core.Remote.TlsMinimum.Tls12, L.T("TLS 1.2 ou plus récent")), new(TwinPanel.Core.Remote.TlsMinimum.Tls13, L.T("TLS 1.3 uniquement"))];

    public static IReadOnlyList<string> Encodings { get; } = ["auto", "utf-8", "windows-1252", "iso-8859-1", "iso-8859-15"];

    /// <summary>Couleurs d'étiquette proposées (la première : aucune).</summary>
    public static IReadOnlyList<ColorChoice> Colors { get; } =
    [
        new(string.Empty, L.T("Aucune")), new("#E5534B", L.T("Rouge")), new("#F2B84B", "Orange"), new("#57AB5A", L.T("Vert")),
        new("#4A9EE8", L.T("Bleu")), new("#986EE2", "Violet"), new("#8B949E", L.T("Gris")),
    ];

    public ObservableCollection<Choice<Guid?>> IdentityChoices { get; } = [];
    public ObservableCollection<Choice<Guid?>> KeyChoices { get; } = [];
    public ObservableCollection<Choice<Guid?>> JumpChoices { get; } = [];
    public ObservableCollection<string> GroupChoices { get; } = [];

    [ObservableProperty] private bool _isDirty;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _buttonLabel = string.Empty;
    [ObservableProperty] private string _group = string.Empty;
    [ObservableProperty] private string _tags = string.Empty;
    [ObservableProperty] private ColorChoice _color = Colors[0];
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSsh), nameof(IsFtp), nameof(IsWebDav), nameof(IsTls), nameof(IsUnencrypted), nameof(ShowKey), nameof(ShowPassword), nameof(ShowUser))]
    private Choice<RemoteProtocol> _protocol = Protocols[0];
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private decimal? _port = 22;
    [ObservableProperty] private string _remoteDirectory = string.Empty;
    [ObservableProperty] private string _localDirectory = string.Empty;
    [ObservableProperty] private string _webDavRoot = "/";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKey), nameof(ShowPassword), nameof(ShowUser), nameof(UsesIdentity))]
    private Choice<Guid?>? _identity;
    [ObservableProperty] private string _userName = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKey), nameof(ShowPassword), nameof(ShowUser))]
    private Choice<AuthMode> _auth = AuthModes[0];
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _savePassword;
    [ObservableProperty] private string _passwordHint = string.Empty;
    [ObservableProperty] private Choice<Guid?>? _key;

    [ObservableProperty] private bool _acceptUnencrypted;
    [ObservableProperty] private Choice<TwinPanel.Core.Remote.TlsMinimum> _tlsMinimum = TlsLevels[0];
    [ObservableProperty] private string _clientCertificatePath = string.Empty;
    [ObservableProperty] private string _certificatePassword = string.Empty;
    [ObservableProperty] private bool _saveCertificatePassword;

    [ObservableProperty] private Choice<Guid?>? _jumpHost;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProxy))]
    private Choice<ProxyKind> _proxy = ProxyKinds[0];
    [ObservableProperty] private string _proxyHost = string.Empty;
    [ObservableProperty] private decimal? _proxyPort = 1080;
    [ObservableProperty] private string _proxyUser = string.Empty;
    [ObservableProperty] private string _proxyPassword = string.Empty;
    [ObservableProperty] private bool _saveProxyPassword;
    [ObservableProperty] private Choice<FtpDataMode> _dataMode = DataModes[0];
    [ObservableProperty] private string _ftpEncoding = "auto";
    [ObservableProperty] private decimal? _timeoutSeconds = 30;
    [ObservableProperty] private decimal? _keepAliveSeconds = 30;

    public bool IsSsh => RemoteProtocols.IsSsh(Protocol.Value);
    public bool IsFtp => RemoteProtocols.IsFtp(Protocol.Value);
    public bool IsWebDav => RemoteProtocols.IsWebDav(Protocol.Value);
    public bool IsTls => RemoteProtocols.IsTls(Protocol.Value);
    public bool IsUnencrypted => RemoteProtocols.IsUnencrypted(Protocol.Value);
    public bool UsesIdentity => Identity?.Value is not null;
    public bool ShowUser => !UsesIdentity && Auth.Value != AuthMode.Anonymous;
    public bool ShowPassword => !UsesIdentity && Auth.Value is AuthMode.Password or AuthMode.KeyAndPassword;
    public bool ShowKey => !UsesIdentity && IsSsh && Auth.Value is AuthMode.Key or AuthMode.KeyAndPassword;
    public bool ShowProxy => Proxy.Value != ProxyKind.None;
    /// <summary>Le trousseau ne contient encore aucune clé.</summary>
    public bool NoKeys => KeyChoices.Count <= 1;

    /// <summary>Des secrets ont été tapés et doivent aller au coffre.</summary>
    public bool HasNewSecrets => (SavePassword && Password.Length > 0) || (SaveProxyPassword && ProxyPassword.Length > 0)
                                 || (SaveCertificatePassword && CertificatePassword.Length > 0);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(PasswordHint))) IsDirty = true;
    }

    partial void OnProtocolChanged(Choice<RemoteProtocol>? oldValue, Choice<RemoteProtocol> newValue)
    {
        if (_loading || oldValue is null) return;
        // Le port suit le protocole tant qu'il n'a pas été changé à la main.
        if (Port is null || (int)Port.Value == RemoteProtocols.DefaultPort(oldValue.Value)) Port = RemoteProtocols.DefaultPort(newValue.Value);
        if (!RemoteProtocols.IsSsh(newValue.Value) && Auth.Value is AuthMode.Key or AuthMode.KeyAndPassword) Auth = AuthModes[0];
        if (RemoteProtocols.IsWebDav(newValue.Value) && (WebDavRoot.Length == 0 || WebDavRoot == "/")) WebDavRoot = "/";
    }

    /// <summary>Raccourci « Nextcloud » pour WebDAV.</summary>
    [RelayCommand]
    private void NextcloudPreset()
    {
        WebDavRoot = "/remote.php/dav/files/{user}";
        if (Protocol.Value is not (RemoteProtocol.WebDavHttps or RemoteProtocol.WebDavHttp)) Protocol = Protocols.First(p => p.Value == RemoteProtocol.WebDavHttps);
    }

    public void SetLists(ConnectionStore store, Guid? currentId)
    {
        var (identity, key, jump) = (Identity?.Value, Key?.Value, JumpHost?.Value);
        bool wasLoading = _loading;
        _loading = true;
        IdentityChoices.Clear();
        IdentityChoices.Add(new Choice<Guid?>(null, L.T("(aucune : identifiant de cette fiche)")));
        foreach (var i in store.Identities) IdentityChoices.Add(new Choice<Guid?>(i.Id, $"{i.Name} ({i.UserName})"));
        KeyChoices.Clear();
        KeyChoices.Add(new Choice<Guid?>(null, L.T("(aucune)")));
        foreach (var k in store.Keys) KeyChoices.Add(new Choice<Guid?>(k.Id, $"{k.Name} ({k.KeyType})"));
        JumpChoices.Clear();
        JumpChoices.Add(new Choice<Guid?>(null, L.T("(aucun : connexion directe)")));
        foreach (var p in store.Profiles.Where(p => RemoteProtocols.IsSsh(p.Protocol) && store.CanJumpThrough(p, currentId)))
            JumpChoices.Add(new Choice<Guid?>(p.Id, $"{p.Name} ({p.Summary})"));
        GroupChoices.Clear();
        foreach (var g in store.Groups) GroupChoices.Add(g);
        OnPropertyChanged(nameof(NoKeys));
        Identity = IdentityChoices.FirstOrDefault(c => c.Value == identity) ?? IdentityChoices[0];
        Key = KeyChoices.FirstOrDefault(c => c.Value == key) ?? KeyChoices[0];
        JumpHost = JumpChoices.FirstOrDefault(c => c.Value == jump) ?? JumpChoices[0];
        _loading = wasLoading;
    }

    public void Load(ConnectionProfile p, ConnectionStore store)
    {
        _loading = true;
        Name = p.Name;
        ButtonLabel = p.ButtonLabel;
        Group = p.Group;
        Tags = string.Join(", ", p.Tags);
        Color = Colors.FirstOrDefault(c => string.Equals(c.Value, p.Color, StringComparison.OrdinalIgnoreCase)) ?? Colors[0];
        Notes = p.Notes;
        Protocol = Protocols.First(c => c.Value == p.Protocol);
        Host = p.Host;
        Port = p.Port;
        RemoteDirectory = p.RemoteDirectory;
        LocalDirectory = p.LocalDirectory;
        WebDavRoot = p.WebDavRoot;
        Identity = IdentityChoices.FirstOrDefault(c => c.Value == p.IdentityId) ?? IdentityChoices.FirstOrDefault();
        UserName = p.UserName;
        Auth = AuthModes.First(c => c.Value == p.Auth);
        Password = string.Empty;
        SavePassword = p.SavePassword;
        PasswordHint = p.SavePassword && store.Vault.Contains(SecretIds.Password(p.Id)) ? L.T("Mot de passe enregistré (laisser vide pour le garder)") : string.Empty;
        Key = KeyChoices.FirstOrDefault(c => c.Value == p.KeyId) ?? KeyChoices.FirstOrDefault();
        AcceptUnencrypted = p.AcceptUnencrypted;
        TlsMinimum = TlsLevels.First(c => c.Value == p.TlsMinimum);
        ClientCertificatePath = p.ClientCertificatePath;
        CertificatePassword = string.Empty;
        SaveCertificatePassword = p.SaveClientCertificatePassword;
        JumpHost = JumpChoices.FirstOrDefault(c => c.Value == p.JumpHostId) ?? JumpChoices.FirstOrDefault();
        Proxy = ProxyKinds.First(c => c.Value == p.Proxy);
        ProxyHost = p.ProxyHost;
        ProxyPort = p.ProxyPort;
        ProxyUser = p.ProxyUser;
        ProxyPassword = string.Empty;
        SaveProxyPassword = p.SaveProxyPassword;
        DataMode = DataModes.First(c => c.Value == p.FtpDataMode);
        FtpEncoding = p.FtpEncoding;
        TimeoutSeconds = p.TimeoutSeconds;
        KeepAliveSeconds = p.KeepAliveSeconds;
        _loading = false;
        IsDirty = false;
    }

    public void Apply(ConnectionProfile p)
    {
        p.Name = Name.Trim();
        p.ButtonLabel = ButtonLabel.Trim();
        p.Group = Group.Trim().Trim('/');
        p.Tags = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        p.Color = Color.Value;
        p.Notes = Notes;
        p.Protocol = Protocol.Value;
        p.Host = Host.Trim();
        p.Port = (int)(Port ?? RemoteProtocols.DefaultPort(Protocol.Value));
        p.RemoteDirectory = RemoteDirectory.Trim();
        p.LocalDirectory = LocalDirectory.Trim().Trim('"');
        p.WebDavRoot = string.IsNullOrWhiteSpace(WebDavRoot) ? "/" : WebDavRoot.Trim();
        p.IdentityId = Identity?.Value;
        p.UserName = UserName.Trim();
        p.Auth = Auth.Value;
        p.SavePassword = SavePassword;
        p.KeyId = Key?.Value;
        p.AcceptUnencrypted = AcceptUnencrypted;
        p.TlsMinimum = TlsMinimum.Value;
        p.ClientCertificatePath = ClientCertificatePath.Trim().Trim('"');
        p.SaveClientCertificatePassword = SaveCertificatePassword;
        p.JumpHostId = JumpHost?.Value;
        p.Proxy = Proxy.Value;
        p.ProxyHost = ProxyHost.Trim();
        p.ProxyPort = (int)(ProxyPort ?? 1080);
        p.ProxyUser = ProxyUser.Trim();
        p.SaveProxyPassword = SaveProxyPassword;
        p.FtpDataMode = DataMode.Value;
        p.FtpEncoding = string.IsNullOrWhiteSpace(FtpEncoding) ? "auto" : FtpEncoding.Trim();
        p.TimeoutSeconds = (int)(TimeoutSeconds ?? 30);
        p.KeepAliveSeconds = (int)(KeepAliveSeconds ?? 30);
    }
}
