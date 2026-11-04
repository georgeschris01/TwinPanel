// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.Services;
using TwinPanel.Core;
using TwinPanel.Core.Archives;
using TwinPanel.Core.Operations;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;

namespace TwinPanel.App.ViewModels;

/// <summary>Lot 6 : connexions distantes (SFTP, SCP, FTP, FTPS, WebDAV) dans les panneaux.</summary>
public partial class MainWindowViewModel
{
    private ConnectionStore? _connections;
    private RemoteSessionManager? _remote;
    private IConnectionPrompts? _prompts;
    private RemoteEdits? _remoteEdits;

    [ObservableProperty] private bool _isConnecting;

    public ConnectionStore? Connections => _connections;
    public RemoteSessionManager? RemoteSessions => _remote;

    /// <summary>Branche les connexions distantes (appelé au démarrage par App).</summary>
    public void AttachRemote(ConnectionStore store, RemoteSessionManager sessions, IConnectionPrompts prompts)
    {
        _connections = store;
        _remote = sessions;
        _prompts = prompts;
        _remoteEdits = new RemoteEdits(_dialogs, s => StatusMessage = s, RefreshAll) { AutoUpload = _settings.Behavior.AutoReUpload };
        _remoteEdits.AutoUploadChosen += (_, _) =>
        {
            // « Toujours renvoyer sans demander » : gardé dans les réglages (modifiable dans Options → Comportement).
            _settings.Behavior.AutoReUpload = true;
            try { _store.Save(_settings); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
        FolderTabViewModel.RemoteFileOpener = OpenRemoteFileAsync;
        FolderTabViewModel.RemoteArchiveOpener = OpenRemoteArchiveAsync;
        FolderTabViewModel.ArchiveCopyOpened = (local, archive, inner) => _remoteEdits?.WatchArchiveCopy(local, archive, inner);
        FolderTabViewModel.ArchivePasswordAsker = UnlockArchiveAsync;
        StartRemoteRefresh();
        FolderTabViewModel.RemoteFolderSizer = MeasureRemoteFoldersAsync;
        FolderTabViewModel.RemoteFlatLister = ListRemoteFlatAsync;
        ThumbnailLoader.RemoteReader = ReadRemoteThumbnailAsync;
        ThumbnailLoader.RemoteIdle = CloseThumbnailConnectionAsync;
        // Barre des lecteurs : toutes les connexions enregistrées (point vert pour celles qui sont ouvertes).
        store.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateConnectionButtons);
        sessions.SessionsChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateConnectionButtons);
        UpdateConnectionButtons();
    }

    /// <summary>
    /// Taille automatique des dossiers d'un serveur : une connexion à part (les panneaux restent utilisables),
    /// dossiers mesurés un par un, liens symboliques non suivis. Aucun identifiant n'est redemandé ni enregistré.
    /// </summary>
    private async Task MeasureRemoteFoldersAsync(IReadOnlyList<FileEntry> folders, Action<FileEntry, long> report, CancellationToken ct)
    {
        if (_remote is null || folders.Count == 0 || !PanelRemotePath.TryParse(folders[0].FullPath, out var first)) return;
        if (_remote.Find(first.Session) is not { } session) return;
        await Task.Run(async () =>
        {
            try
            {
                await using var fs = await session.OpenWorkerAsync(ct);
                foreach (var e in folders)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!PanelRemotePath.TryParse(e.FullPath, out var p)) continue;
                    var (bytes, _, _) = await TwinPanel.Core.Rights.RightsApplier.MeasureAsync(fs,
                        [new RemoteItem(e.Name, p.Path, true, 0, e.LastWriteTime)], null, ct);
                    report(e, bytes);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Connexion refusée ou coupée : on s'arrête sans message (les dossiers restent « <DIR> »).
            }
        }, ct);
    }

    private void UpdateConnectionButtons()
    {
        if (_connections is null || _remote is null) return;
        var list = _connections.Profiles
            .OrderBy(p => p.Group.Length == 0 ? 1 : 0).ThenBy(p => p.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => (p, _remote.FindByProfile(p.Id)?.Alias)).ToList();
        // Une connexion rapide non enregistrée mais ouverte apparaît aussi.
        foreach (var s in _remote.Sessions.Where(s => _connections.FindProfile(s.Profile.Id) is null))
            list.Add((s.Profile, s.Alias));
        Left.SetConnections(list);
        Right.SetConnections(list);
    }

    /// <summary>Fermeture de TwinPanel : toutes les connexions sont fermées proprement.</summary>
    public async Task ShutdownRemoteAsync()
    {
        if (_remote is not null) await _remote.CloseAllAsync().ConfigureAwait(false);
    }

    /// <summary>Arrête la surveillance des fichiers édités (fil de l'interface).</summary>
    public void StopRemoteEdits() => _remoteEdits?.Dispose();

    private RemoteSession? SessionOf(FolderTabViewModel tab) => _remote?.FindForPath(tab.CurrentPath);

    private static RemoteItem ToRemoteItem(FileEntry e) =>
        new(e.Name, PanelRemotePath.TryParse(e.FullPath, out var p) ? p.Path : e.FullPath, e.IsDirectory, e.Size, e.LastWriteTime,
            (e.Attributes & FileAttributes.ReparsePoint) != 0);

    private static string RemotePart(string panelPath) => PanelRemotePath.TryParse(panelPath, out var p) ? p.Path : "/";

    // ---- Onglets sur un serveur au démarrage --------------------------------------------------------------

    /// <summary>Remplace les onglets sur un serveur par « remote:{fiche}|{chemin} » (ceux d'une connexion rapide non enregistrée sont oubliés).</summary>
    private PanelState WithSavedRemoteTabs(PanelState state)
    {
        var tabs = new List<string>();
        int active = 0;
        for (int i = 0; i < state.Tabs.Count; i++)
        {
            var saved = SavedTab.Decode(state.Tabs[i]);
            var path = saved.Path;
            if (PanelRemotePath.TryParse(path, out var remote))
            {
                var session = _remote?.Find(remote.Session);
                if (session is null || _connections?.FindProfile(session.Profile.Id) is null) continue;
                path = SavedRemoteTab.Encode(session.Profile.Id, remote.Path);
            }
            if (i == state.ActiveTab) active = tabs.Count;
            tabs.Add((saved with { Path = path }).Encode());
        }
        return new PanelState { Tabs = tabs, ActiveTab = active, SortColumn = state.SortColumn, SortDescending = state.SortDescending };
    }

    /// <param name="Position">Son rang dans le panneau à la fermeture.</param>
    /// <param name="Saved">Sa forme enregistrée, pour le garder s'il n'a pas pu être rouvert.</param>
    private sealed record PendingRemoteTab(Guid ProfileId, string Path, bool WasActive, int Position, string Saved, TabLock Lock = TabLock.None, string Name = "");

    /// <summary>Onglets de serveur pas rouverts (« Pas cette fois », serveur injoignable) : ils ne doivent pas disparaître pour autant.</summary>
    private readonly List<(FilePanelViewModel Panel, PendingRemoteTab Tab)> _unopenedTabs = [];

    /// <summary>Remet à leur place les onglets qui n'ont pas pu (ou pas encore pu) être rouverts.</summary>
    private PanelState WithUnopenedTabs(FilePanelViewModel panel, PanelState state) =>
        TabPositions.WithPending(state, _unopenedTabs.Where(x => ReferenceEquals(x.Panel, panel)).Select(x => (x.Tab.Position, x.Tab.Saved)));

    /// <summary>Sépare les onglets locaux (ouverts tout de suite) de ceux sur un serveur (rouverts ensuite).</summary>
    private static (PanelState Local, List<PendingRemoteTab> Remote) SplitRemoteTabs(PanelState state)
    {
        var local = new List<string>();
        var remote = new List<PendingRemoteTab>();
        int active = 0;
        for (int i = 0; i < state.Tabs.Count; i++)
        {
            var saved = SavedTab.Decode(state.Tabs[i]);
            if (SavedRemoteTab.TryDecode(saved.Path, out var id, out var path))
                remote.Add(new PendingRemoteTab(id, path, i == state.ActiveTab, i, state.Tabs[i], saved.Lock, saved.Name));
            else
            {
                if (i == state.ActiveTab) active = local.Count;
                local.Add(state.Tabs[i]);
            }
        }
        return (new PanelState { Tabs = local, ActiveTab = active, SortColumn = state.SortColumn, SortDescending = state.SortDescending }, remote);
    }

    /// <summary>Options → Démarrage : rouvre (après accord, ou toujours) les onglets qui étaient sur un serveur.</summary>
    private async Task RestoreRemoteTabsAsync(List<PendingRemoteTab> left, List<PendingRemoteTab> right)
    {
        if (_connections is null || _remote is null || _prompts is null) return;
        var all = left.Select(t => (Panel: Left, Tab: t)).Concat(right.Select(t => (Panel: Right, Tab: t)))
            .Where(x => _connections.FindProfile(x.Tab.ProfileId) is not null).ToList();
        var mode = _settings.Startup.RemoteTabs;
        if (all.Count == 0 || mode == RemoteTabsRestore.Never) return;
        // Gardés de côté dès maintenant : si TwinPanel se ferme pendant la question ou la reconnexion
        // (par exemple juste après une installation), ils restent enregistrés et reviendront.
        _unopenedTabs.AddRange(all);

        var profiles = all.Select(x => x.Tab.ProfileId).Distinct().Select(id => _connections.FindProfile(id)!).ToList();
        if (mode == RemoteTabsRestore.Ask)
        {
            var names = string.Join(", ", profiles.Select(p => p.Name));
            var choice = await _dialogs.ChooseAsync<int>(L.T("Onglets sur un serveur"),
                L.F("À la fermeture, {0} onglet(s) étaient ouverts sur : {1}.\nSe reconnecter pour les rouvrir ?", all.Count, names),
                (L.T("Rouvrir"), 1), (L.T("Toujours rouvrir"), 2), (L.T("Ne jamais rouvrir"), 3), (L.T("Pas cette fois"), null));
            if (choice is null) return; // déjà gardés de côté
            if (choice is 2 or 3)
            {
                // Le choix est retenu (modifiable dans Options → Démarrage).
                _settings.Startup.RemoteTabs = choice == 2 ? RemoteTabsRestore.Always : RemoteTabsRestore.Never;
                try { _store.Save(_settings); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                if (choice == 3)
                {
                    _unopenedTabs.Clear(); // « Ne jamais rouvrir » : on les laisse partir
                    return;
                }
            }
        }

        // Toutes les connexions d'abord : chaque onglet reprend ensuite son rang, quel que soit l'ordre des serveurs.
        var opened = new Dictionary<Guid, RemoteSession>();
        foreach (var profile in profiles)
        {
            if (RemoteProtocols.IsUnencrypted(profile.Protocol) && !profile.AcceptUnencrypted) continue; // jamais sans prévenir
            StatusMessage = L.F("Reconnexion à {0}…", profile.Name);
            RemoteSession? session;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                session = await Task.Run(() => _remote.ConnectAsync(profile, _connections, _prompts, cts.Token));
            }
            catch (Exception ex) when (ex is RemoteException or IOException or InvalidOperationException or OperationCanceledException or UnauthorizedAccessException)
            {
                StatusMessage = L.F("Impossible de rouvrir {0} : {1}", profile.Name, ex.Message);
                continue;
            }
            if (session is null) continue; // mot de passe refusé de donner : on n'insiste pas
            opened[profile.Id] = session;
        }
        foreach (var (panel, tab) in all.OrderBy(x => x.Tab.Position))
        {
            if (!opened.TryGetValue(tab.ProfileId, out var session)) continue; // reste de côté
            await panel.AddTabAsync(session.PathFor(tab.Path).ToString(), panel.Current.SortColumn, panel.Current.SortDescending, select: tab.WasActive, tab.Lock, tab.Name,
                insertAt: Math.Min(tab.Position, panel.Tabs.Count));
            _unopenedTabs.Remove((panel, tab)); // ouvert : il est maintenant dans le panneau
        }
        StatusMessage = _unopenedTabs.Count > 0 ? L.F("{0} onglet(s) de serveur pas rouverts : ils reviendront au prochain démarrage.", _unopenedTabs.Count) : L.T("Onglets rouverts.");
    }

    // ---- Ouvrir / fermer -------------------------------------------------------------------------------

    /// <summary>Ctrl+F : gestionnaire de connexions.</summary>
    [RelayCommand]
    private async Task OpenConnectionManager()
    {
        if (_connections is null || _remote is null || _prompts is null) return;
        var profile = await _dialogs.ShowConnectionManagerAsync(_connections, _remote, _prompts);
        if (profile is not null) await ConnectAsync(profile);
    }

    /// <summary>Ctrl+N : connexion rapide par une adresse.</summary>
    [RelayCommand]
    private async Task QuickConnect()
    {
        if (_connections is null || _prompts is null) return;
        if (await _dialogs.AskQuickConnectAsync() is not { } r) return;
        var profile = r.Profile;
        if (r.Save)
        {
            profile.Name = _connections.UniqueName(profile.Name);
            try { _connections.SaveProfile(profile); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowErrorAsync(L.T("Connexion rapide"), L.T("Fiche non enregistrée : ") + ex.Message);
            }
        }
        await ConnectAsync(profile, r.Password);
    }

    /// <summary>Menu Connexions : une connexion enregistrée.</summary>
    [RelayCommand]
    private Task ConnectProfile(ConnectionProfile? profile) => profile is null ? Task.CompletedTask : ConnectAsync(profile);

    /// <summary>Ouvre (ou retrouve) la connexion et l'affiche dans le panneau actif.</summary>
    public Task ConnectAsync(ConnectionProfile profile, string? typedPassword = null) =>
        WithSessionAsync(profile, async session =>
        {
            if (await ActivePanel.Current.NavigateAsync(session.StartPath.ToString()))
            {
                StatusMessage = L.F("Connecté à {0} ({1}).", profile.Name, RemoteProtocols.Label(profile.Protocol));
                if (profile.LocalDirectory.Length > 0 && Directory.Exists(profile.LocalDirectory))
                    await OtherPanel.Current.NavigateAsync(profile.LocalDirectory);
            }
        }, typedPassword);

    /// <summary>Ouvre (ou retrouve) la connexion de la fiche, puis fait « then » ; les erreurs sont affichées ici.</summary>
    private async Task WithSessionAsync(ConnectionProfile profile, Func<RemoteSession, Task> then, string? typedPassword = null)
    {
        if (_remote is null || _prompts is null || IsConnecting) return;
        if (RemoteProtocols.IsUnencrypted(profile.Protocol) && !profile.AcceptUnencrypted
            && !await _dialogs.ConfirmAsync(L.T("Connexion non chiffrée"),
                L.F("« {0} » utilise {1} : identifiant, mot de passe et fichiers ", profile.Name, RemoteProtocols.Label(profile.Protocol)) +
                L.T("circulent en clair sur le réseau.\nSe connecter quand même ?"))) return;

        IsConnecting = true;
        StatusMessage = L.F("Connexion à {0} ({1})…", profile.Name, profile.Summary);
        try
        {
            IConnectionPrompts prompts = string.IsNullOrEmpty(typedPassword) ? _prompts : new PrefilledPrompts(_prompts, typedPassword);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            // Hors du fil de l'interface : les questions éventuelles y sont ramenées automatiquement.
            // Une connexion rapide non enregistrée n'a pas de fiche : rien n'est lu ni retenu dans le coffre,
            // mais les serveurs connus sont vérifiés comme pour les autres.
            var session = await Task.Run(() => _remote.ConnectAsync(profile, _connections, prompts, cts.Token));
            if (session is null) { StatusMessage = L.T("Connexion annulée."); return; }
            await then(session);
        }
        catch (OperationCanceledException) { StatusMessage = L.T("Connexion annulée."); }
        catch (MissingKeyException ex)
        {
            StatusMessage = L.T("Clé manquante pour cette connexion.");
            if (await _dialogs.ConfirmAsync(L.T("Clé manquante"), ex.Message + "\n\n" + L.T("Ouvrir le Gestionnaire de connexions ?")))
            {
                IsConnecting = false;
                await OpenConnectionManager();
            }
        }
        catch (Exception ex) when (ex is RemoteException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            StatusMessage = L.T("Échec de la connexion.");
            await _dialogs.ShowErrorAsync(L.T("Connexion"), ex.Message);
        }
        finally { IsConnecting = false; }
    }

    /// <summary>Ctrl+Maj+F : ferme la connexion affichée dans le panneau actif.</summary>
    [RelayCommand]
    private async Task Disconnect(RemoteSession? session = null)
    {
        if (_remote is null) return;
        session ??= SessionOf(ActivePanel.Current) ?? SessionOf(OtherPanel.Current);
        if (session is null)
        {
            StatusMessage = _remote.Sessions.Count == 0 ? L.T("Aucune connexion ouverte.") : L.T("Le panneau actif n'affiche pas de serveur (menu Connexions pour choisir).");
            return;
        }
        _remoteEdits?.Forget(session);
        await _remote.CloseAsync(session);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var panel in new[] { Left, Right })
            foreach (var tab in panel.Tabs.Where(t => (PanelRemotePath.TryParse(t.CurrentPath, out var p) && p.Session.Equals(session.Alias, StringComparison.OrdinalIgnoreCase))
                                                      || ServerArchives.Find(t.CurrentPath)?.IsOn(session.Alias) == true))
                await tab.NavigateAsync(home);
        StatusMessage = L.F("Déconnecté de {0}.", session.Profile.Name);
    }

    /// <summary>Menu Connexions : afficher une connexion déjà ouverte dans le panneau actif.</summary>
    [RelayCommand]
    private Task ShowSession(RemoteSession? session) =>
        session is null ? Task.CompletedTask : ActivePanel.Current.NavigateAsync(session.PathFor(session.HomeDirectory).ToString());

    /// <summary>
    /// Propriétés (Alt+Entrée) des éléments marqués, ou de l'élément sous le curseur, ou du dossier affiché :
    /// droits / propriétaire / groupe (serveur, Linux), attributs et sécurité NTFS (Windows).
    /// </summary>
    [RelayCommand]
    private async Task ShowProperties()
    {
        var tab = ActivePanel.Current;
        if (tab.IsInsideArchive)
        {
            await _dialogs.ShowErrorAsync(L.T("Propriétés"), L.T("Les propriétés ne sont pas disponibles à l'intérieur d'une archive."));
            return;
        }
        var items = tab.TargetEntries;
        if (items.Count == 0)
        {
            // Rien sous le curseur (ou « [..] ») : propriétés du dossier affiché.
            var path = tab.CurrentPath;
            string name = PanelRemotePath.TryParse(path, out var rp) ? (RemotePath.FileName(rp.Path) is { Length: > 0 } n ? n : "/")
                : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } ln ? ln : path;
            items = [new FileEntry(name, path, true, 0, DateTime.Now, FileAttributes.Directory)];
        }
        var session = SessionOf(tab);
        if (tab.IsRemote && session is null)
        {
            await _dialogs.ShowErrorAsync(L.T("Propriétés"), L.T("La connexion de ce panneau n'est plus ouverte."));
            return;
        }
        if (await _dialogs.ShowPropertiesAsync(items, session)) await tab.RefreshAsync();
    }

    /// <summary>Ancienne commande « Droits (chmod) » : ouvre maintenant la fenêtre Propriétés.</summary>
    [RelayCommand]
    private Task ChangePermissions() => ShowProperties();

    // ---- Fichiers distants : voir, éditer, ouvrir --------------------------------------------------------

    /// <summary>Copie locale temporaire d'un fichier distant (null en cas d'erreur, déjà signalée).</summary>
    private async Task<string?> DownloadTempAsync(FileEntry entry, bool quiet = false)
    {
        if (_remote?.FindForPath(entry.FullPath) is not { } session) return null;
        try
        {
            if (!quiet) StatusMessage = L.F("Téléchargement de {0}…", entry.Name);
            var local = await Task.Run(() => RemoteTransfer.DownloadToTempAsync(session, ToRemoteItem(entry), CancellationToken.None));
            if (!quiet) StatusMessage = L.T("Prêt");
            return local;
        }
        catch (Exception ex) when (ex is RemoteException or IOException or UnauthorizedAccessException)
        {
            if (!quiet) await _dialogs.ShowErrorAsync(L.T("Fichier distant"), ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Entrée / double-clic sur un fichier distant : téléchargé puis ouvert avec l'application associée, et surveillé
    /// comme avec F4 : à chaque enregistrement, TwinPanel propose de le renvoyer sur le serveur (comme Total Commander).
    /// </summary>
    private async Task OpenRemoteFileAsync(FileEntry entry)
    {
        if (_remote?.FindForPath(entry.FullPath) is not { } session || await DownloadTempAsync(entry) is not { } local) return;
        _remoteEdits?.Watch(session, local, RemotePart(entry.FullPath));
        await RunShellAsync(() => ShellService.Open(local));
        StatusMessage = _settings.Behavior.AutoReUpload
            ? L.F("{0} ouvert : chaque enregistrement sera renvoyé sur le serveur.", entry.Name)
            : L.F("{0} ouvert : à chaque enregistrement, TwinPanel proposera de le renvoyer sur le serveur.", entry.Name);
    }

    private async Task<bool> RemoteViewAsync(FileEntry f)
    {
        if (!ActivePanel.Current.IsRemote) return false;
        if (await DownloadTempAsync(f) is { } local && !await _tools.TryExternalViewAsync(local)) _dialogs.ShowViewer([local], 0);
        return true;
    }

    /// <summary>F4 sur un serveur : édition d'une copie, renvoyée sur le serveur à chaque enregistrement (avec confirmation).</summary>
    private async Task<bool> RemoteEditAsync(FileEntry f, bool internalEditor)
    {
        if (!ActivePanel.Current.IsRemote) return false;
        if (_remote?.FindForPath(f.FullPath) is not { } session || await DownloadTempAsync(f) is not { } local) return true;
        _remoteEdits?.Watch(session, local, RemotePart(f.FullPath));
        StatusMessage = _settings.Behavior.AutoReUpload
            ? L.F("Édition de {0} : chaque enregistrement sera renvoyé sur le serveur.", f.Name)
            : L.F("Édition de {0} : à chaque enregistrement, TwinPanel proposera de le renvoyer sur le serveur.", f.Name);
        if (internalEditor) await _dialogs.ShowEditorAsync(local);
        else await _tools.EditAsync(local);
        return true;
    }

    // ---- F5 / F6 avec un serveur -----------------------------------------------------------------------------

    /// <summary>Copie / déplacement quand l'un des panneaux (ou la destination tapée) est un serveur. False : pas concerné.</summary>
    private async Task<bool> RemoteTransferAsync(bool move, IReadOnlyList<FileEntry> items, string? destination, bool fromPanel, ConflictPolicy? forced)
    {
        if (_remote is null) return false;
        var src = fromPanel ? SessionOf(ActivePanel.Current) : null;
        var defaultDestination = destination ?? OtherPanel.Current.CurrentPath;
        bool destRemote = PanelRemotePath.TryParse(defaultDestination, out _);
        if (src is null && !destRemote) return false;
        bool fromArchive = fromPanel && ActivePanel.Current.IsInsideArchive;
        if (fromArchive && move && await EditableArchiveAsync() is null) return true;

        var verb = move ? L.T("Déplacer") : L.T("Copier");
        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        var request = await _dialogs.AskTransferAsync(verb, what, defaultDestination);
        if (request is null || request.Destination.Length == 0) return true;
        var destText = request.Destination;

        RemoteSession? dst = null;
        string destPath;
        if (PanelRemotePath.TryParse(destText, out var remoteDest))
        {
            dst = _remote.Find(remoteDest.Session);
            if (dst is null) { await _dialogs.ShowErrorAsync(verb, L.F("La connexion « {0} » n'est pas ouverte.", remoteDest.Session)); return true; }
            destPath = remoteDest.Path;
        }
        else
        {
            if (src is null) return false; // local → local : le chemin habituel s'en charge
            destPath = Path.IsPathRooted(destText) ? destText : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), destText);
            if (ArchivePath.TrySplit(destPath, out var targetArchive, out var targetInner))
            {
                await AddToArchiveAsync(targetArchive, targetInner, items, move, forced ?? request.Policy, src);
                return true;
            }
            if (!Directory.Exists(destPath))
            {
                if (!await _dialogs.ConfirmAsync(L.T("Dossier inexistant"), L.F("Le dossier{0}{1}{2}n'existe pas. Le créer ?", Environment.NewLine, destPath, Environment.NewLine))) return true;
                try { Directory.CreateDirectory(destPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { await _dialogs.ShowErrorAsync(L.T("Erreur"), ex.Message); return true; }
            }
        }

        // Conflits (premier niveau) : on demande si la politique n'est pas déjà choisie.
        var policy = forced ?? request.Policy ?? ConflictPolicy.Overwrite;
        if (forced is null && request.Policy is null)
        {
            List<string> conflicts;
            try
            {
                if (dst is not null)
                {
                    var existing = await dst.UseAsync(fs => fs.ListAsync(destPath, CancellationToken.None), CancellationToken.None);
                    var names = existing.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
                    conflicts = items.Where(i => names.Contains(i.Name)).Select(i => RemotePath.Combine(destPath, i.Name)).ToList();
                }
                else conflicts = items.Select(i => Path.Combine(destPath, i.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            }
            catch (Exception ex) when (ex is RemoteException or IOException)
            {
                await _dialogs.ShowErrorAsync(verb, ex.Message);
                return true;
            }
            if (conflicts.Count > 0)
            {
                if (await _dialogs.AskConflictAsync(conflicts) is not { } chosen) return true;
                policy = chosen;
            }
        }

        if (fromArchive)
        {
            ArchivePath.TrySplit(ActivePanel.Current.CurrentPath, out var archive, out var innerBase);
            var keys = items.Select(i => ArchivePath.TrySplit(i.FullPath, out _, out var key) ? key : string.Empty).Where(k => k.Length > 0).ToList();
            var server = dst!;
            ActivePanel.Current.MarkAll(false);
            Operations.Start(new OperationJobViewModel(L.F("{0} de {1} vers {2}:{3}", (move ? L.T("Déplacement") : L.T("Envoi")), what, server.Profile.Name, destPath), transfer: true,
                (progress, ct, pause) => Task.Run(() => RemoteArchives.ExtractToServerAsync(server, archive, keys, innerBase, destPath, policy, move, progress, ct, pause), ct))
                { ExclusiveKey = move ? archive : null });
            return true;
        }

        ActivePanel.Current.MarkAll(false);
        string title;
        if (src is null) title = L.F("{0} de {1} vers {2}:{3}", (move ? L.T("Déplacement") : L.T("Envoi")), what, dst!.Profile.Name, destPath);
        else if (dst is null) title = L.F("{0} de {1} depuis {2} vers {3}", (move ? L.T("Déplacement") : L.T("Téléchargement")), what, src.Profile.Name, destPath);
        else if (src == dst && move) title = L.F("Déplacement de {0} sur {1} vers {2}", what, src.Profile.Name, destPath);
        else title = L.F("{0} de {1} de {2} vers {3}:{4}", (move ? L.T("Déplacement") : L.T("Copie")), what, src.Profile.Name, dst.Profile.Name, destPath);

        StartTransfer(new SavedTransfer
        {
            Title = title,
            SourceProfile = src?.Profile.Id,
            TargetProfile = dst?.Profile.Id,
            Destination = destPath,
            Policy = policy,
            Move = move,
            LocalPaths = items.Select(i => i.FullPath).ToList(),
            Items = items.Select(ToRemoteItem).ToList(),
        }, src, dst);
        return true;
    }

    // ---- F7 / F8 / Maj+F6 / Maj+F4 sur un serveur --------------------------------------------------------------

    private async Task<bool> RemoteMakeDirectoryAsync()
    {
        if (SessionOf(ActivePanel.Current) is not { } session) return false;
        var name = await _dialogs.PromptAsync(L.T("Nouveau dossier"), L.F("Nom du dossier (sur {0}) :", session.Profile.Name));
        if (string.IsNullOrWhiteSpace(name)) return true;
        name = name.Trim();
        if (!RemotePath.IsSafeName(name)) { await _dialogs.ShowErrorAsync(L.T("Nouveau dossier"), L.T("Nom invalide (pas de « / »).")); return true; }
        try
        {
            var dir = RemotePart(ActivePanel.Current.CurrentPath);
            await session.UseAsync(fs => fs.CreateDirectoryAsync(RemotePath.Combine(dir, name), CancellationToken.None), CancellationToken.None);
            await ActivePanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath, selectName: name);
        }
        catch (Exception ex) when (ex is RemoteException or IOException) { await _dialogs.ShowErrorAsync(L.T("Nouveau dossier"), ex.Message); }
        return true;
    }

    private async Task<bool> RemoteDeleteAsync()
    {
        if (SessionOf(ActivePanel.Current) is not { } session) return false;
        var items = ActivePanel.Current.TargetEntries;
        if (items.Count == 0) return true;
        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        if (!await _dialogs.ConfirmAsync(L.T("Supprimer"), L.F("Supprimer DÉFINITIVEMENT {0} sur {1} ?\n", what, session.Profile.Name) +
                                                      L.T("Il n'y a pas de corbeille sur un serveur."))) return true;
        var remoteItems = items.Select(ToRemoteItem).ToList();
        ActivePanel.Current.MarkAll(false);
        Operations.Start(new OperationJobViewModel(L.F("Suppression de {0} sur {1}", what, session.Profile.Name), transfer: false,
            (p, ct, _) => Task.Run(() => RemoteTransfer.DeleteAsync(session, remoteItems, p, ct), ct)));
        return true;
    }

    private async Task<bool> RemoteRenameAsync()
    {
        if (SessionOf(ActivePanel.Current) is not { } session) return false;
        if (ActivePanel.Current.CurrentItem is not { IsParentLink: false } item) return true;
        int selectLength = item.IsDirectory ? item.Name.Length : Path.GetFileNameWithoutExtension(item.Name).Length;
        var newName = await AskNewNameAsync(item.Name, selectLength == 0 ? item.Name.Length : selectLength);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return true;
        newName = newName.Trim();
        if (!RemotePath.IsSafeName(newName)) { await _dialogs.ShowErrorAsync(L.T("Renommer"), L.T("Nom invalide (pas de « / »).")); return true; }
        try
        {
            var from = RemotePart(item.FullPath);
            var to = RemotePath.Combine(RemotePath.Parent(from) ?? "/", newName);
            await session.UseAsync(fs => fs.RenameAsync(from, to, overwrite: false, CancellationToken.None), CancellationToken.None);
            await ActivePanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath, selectName: newName);
        }
        catch (Exception ex) when (ex is RemoteException or IOException) { await _dialogs.ShowErrorAsync(L.T("Renommer"), ex.Message); }
        return true;
    }

    private async Task<bool> RemoteNewFileAsync()
    {
        if (SessionOf(ActivePanel.Current) is not { } session) return false;
        var name = await _dialogs.PromptAsync(L.T("Nouveau fichier"), L.F("Nom du fichier (sur {0}) :", session.Profile.Name), L.T("nouveau.txt"), selectLength: L.T("nouveau.txt").Length - 4);
        if (string.IsNullOrWhiteSpace(name)) return true;
        name = name.Trim();
        if (!RemotePath.IsValidWindowsName(name)) { await _dialogs.ShowErrorAsync(L.T("Nouveau fichier"), L.T("Nom invalide.")); return true; }
        try
        {
            var remote = RemotePath.Combine(RemotePart(ActivePanel.Current.CurrentPath), name);
            var exists = await session.UseAsync(fs => fs.StatAsync(remote, CancellationToken.None), CancellationToken.None);
            if (exists is null)
                await session.UseAsync(fs => fs.UploadAsync(new MemoryStream(), remote, CancellationToken.None), CancellationToken.None);
            await ActivePanel.Current.NavigateAsync(ActivePanel.Current.CurrentPath, selectName: name);
            if (ActivePanel.Current.CurrentItem is { IsDirectory: false } created && created.Name == name)
                await RemoteEditAsync(created.Entry, internalEditor: false);
        }
        catch (Exception ex) when (ex is RemoteException or IOException) { await _dialogs.ShowErrorAsync(L.T("Nouveau fichier"), ex.Message); }
        return true;
    }

    /// <summary>Commandes pas encore prévues sur un serveur : message clair au lieu d'une erreur obscure.</summary>
    private async Task<bool> RefuseOnRemoteAsync(string what, bool alsoOtherPanel = false)
    {
        if (!ActivePanel.Current.IsRemote && !(alsoOtherPanel && OtherPanel.Current.IsRemote)) return false;
        await _dialogs.ShowErrorAsync(L.T("Connexion distante"), L.F("{0} n'est pas encore possible sur un serveur.\n", what) +
                                                           L.T("Copie d'abord les fichiers sur ton PC (F5), puis recommence."));
        return true;
    }
}
