// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Compare;
using TwinPanel.Core.Navigation;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Search;
using TwinPanel.Core.Settings;

namespace TwinPanel.App.ViewModels;

/// <summary>Une ligne de la liste des dossiers récents.</summary>
public sealed record RecentFolderEntry(string Label, string Stored);

/// <summary>Lot Navigation : dossiers récents.</summary>
public partial class MainWindowViewModel
{
    private readonly RecentFolders _recent;
    private readonly SyncProfileStore _syncProfiles;

    /// <summary>Alt+Bas : la fenêtre ouvre la liste des dossiers récents.</summary>
    public event EventHandler? HistoryMenuRequested;

    [RelayCommand] private void ShowHistory() => HistoryMenuRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Note le dossier affiché. Connexion rapide non enregistrée : rien à retenir (pas de fiche pour y revenir).</summary>
    private void RecordVisit(string path)
    {
        if (!PanelRemotePath.TryParse(path, out var remote)) { _recent.Visit(path); return; }
        if (_remote?.Find(remote.Session) is { } session && _connections?.FindProfile(session.Profile.Id) is not null)
            _recent.Visit(SavedRemoteTab.Encode(session.Profile.Id, remote.Path));
    }

    /// <summary>Forme durable d'un dossier ; null pour une connexion rapide, qui n'a pas de fiche pour y revenir.</summary>
    private string? ToStoredFolder(string path)
    {
        if (!PanelRemotePath.TryParse(path, out var remote)) return path;
        return _remote?.Find(remote.Session) is { } session && _connections?.FindProfile(session.Profile.Id) is not null
            ? SavedRemoteTab.Encode(session.Profile.Id, remote.Path)
            : null;
    }

    /// <summary>Chemin de panneau d'un dossier gardé ; null si son serveur n'est pas ouvert.</summary>
    private string? FromStoredFolder(string stored) => SavedRemoteTab.TryDecode(stored, out var id, out var path)
        ? _remote?.FindByProfile(id)?.PathFor(path).ToString()
        : stored;

    /// <summary>Ouvre les serveurs d'un profil de synchronisation ; faux si l'un d'eux reste fermé (l'erreur a été dite).</summary>
    private async Task<bool> OpenServersOfAsync(SyncProfile profile)
    {
        foreach (var stored in new[] { profile.Left, profile.Right })
        {
            if (!SavedRemoteTab.TryDecode(stored, out var id, out _) || _remote?.FindByProfile(id) is not null) continue;
            if (_connections?.FindProfile(id) is not { } connection)
            {
                await _dialogs.ShowErrorAsync(L.T("Synchroniser"), L.F("La connexion du profil « {0} » n'existe plus.", profile.Name));
                return false;
            }
            await WithSessionAsync(connection, _ => Task.CompletedTask);
            if (_remote?.FindByProfile(id) is null) return false;
        }
        return true;
    }

    /// <summary>Lignes à afficher : sans le dossier actuel, ni ceux d'une connexion supprimée depuis.</summary>
    public IReadOnlyList<RecentFolderEntry> RecentEntries()
    {
        var current = ActivePanel.Current.CurrentPath;
        var list = new List<RecentFolderEntry>();
        foreach (var stored in _recent.Items)
        {
            if (!SavedRemoteTab.TryDecode(stored, out var id, out var path))
            {
                if (!RecentFolders.IsSame(stored, current)) list.Add(new RecentFolderEntry(stored, stored));
                continue;
            }
            if (_connections?.FindProfile(id) is not { } profile) continue;
            bool here = PanelRemotePath.TryParse(current, out var cur) && cur.Path == path
                        && _remote?.Find(cur.Session)?.Profile.Id == id;
            if (!here) list.Add(new RecentFolderEntry($"{profile.Name} : {path}", stored));
        }
        return list;
    }

    /// <summary>Ouvre un dossier récent dans le panneau actif (reconnexion par la fiche si besoin).</summary>
    [RelayCommand]
    private async Task OpenRecent(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return;
        if (!SavedRemoteTab.TryDecode(stored, out var id, out var path))
        {
            await ActivePanel.Current.NavigateAsync(stored);
            return;
        }
        if (_connections?.FindProfile(id) is not { } profile) { StatusMessage = L.T("Cette connexion n'existe plus."); return; }
        if (_remote?.FindByProfile(id) is { } open) await ActivePanel.Current.NavigateAsync(open.PathFor(path).ToString());
        else await WithSessionAsync(profile, session => ActivePanel.Current.NavigateAsync(session.PathFor(path).ToString()));
    }

    // ---- Miniatures ---------------------------------------------------------------------------

    /// <summary>Tailles proposées par Ctrl + molette.</summary>
    private static readonly double[] ThumbnailSizes = [96, 160, 256];

    private IRemoteFileSystem? _thumbnailFs;
    private string? _thumbnailSession;

    /// <summary>Ctrl+Maj+F1 : l'onglet passe en miniatures, ou revient à la liste.</summary>
    [RelayCommand] private void ToggleThumbnails() => ActivePanel.Current.IsThumbnails = !ActivePanel.Current.IsThumbnails;

    /// <summary>Taille suivante (+1), précédente (-1), ou normale (0).</summary>
    public void ChangeThumbnailSize(int delta)
    {
        var a = _settings.Appearance;
        int index = Array.FindLastIndex(ThumbnailSizes, s => s <= a.ThumbnailSize);
        a.ThumbnailSize = delta == 0 ? ThumbnailSizes[1] : ThumbnailSizes[Math.Clamp(index + delta, 0, ThumbnailSizes.Length - 1)];
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
        StatusMessage = L.F("Miniatures : {0:0} points (Ctrl + molette ; Ctrl + clic milieu : taille normale).", a.ThumbnailSize);
    }

    /// <summary>
    /// Image d'un serveur pour sa vignette, sur une connexion à part, gardée d'une image à l'autre.
    /// Appelé par un seul fil, jamais en parallèle ; une fiche déjà connectée ne redemande rien.
    /// </summary>
    private async Task<byte[]?> ReadRemoteThumbnailAsync(FileEntry entry, CancellationToken ct)
    {
        if (!PanelRemotePath.TryParse(entry.FullPath, out var path) || _remote?.Find(path.Session) is not { } session) return null;
        if (_thumbnailFs is not { IsConnected: true } || _thumbnailSession != path.Session)
        {
            await CloseThumbnailConnectionAsync();
            _thumbnailFs = await session.OpenWorkerAsync(ct);
            _thumbnailSession = path.Session;
        }
        using var data = new MemoryStream();
        await _thumbnailFs.DownloadAsync(path.Path, data, ct);
        return data.ToArray();
    }

    private async Task CloseThumbnailConnectionAsync()
    {
        var fs = _thumbnailFs;
        _thumbnailFs = null;
        _thumbnailSession = null;
        if (fs is not null) await fs.DisposeAsync();
    }

    // ---- Jeux d'onglets ----------------------------------------------------------------------

    /// <summary>Ctrl+Maj+J : la fenêtre ouvre la liste des jeux d'onglets.</summary>
    public event EventHandler? TabSetsMenuRequested;

    public IReadOnlyList<TabSet> TabSets => _settings.TabSets;

    [RelayCommand] private void ShowTabSets() => TabSetsMenuRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Enregistre les onglets des deux panneaux sous un nom (même nom : remplace, après accord).</summary>
    [RelayCommand]
    private async Task SaveTabSet()
    {
        var name = await _dialogs.PromptAsync(L.T("Enregistrer les onglets"), L.T("Nom du jeu d'onglets :"), ActivePanel.Current.Title);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        int index = _settings.TabSets.FindIndex(s => s.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (index >= 0 && !await _dialogs.ConfirmAsync(L.T("Enregistrer les onglets"), L.F("« {0} » existe déjà. Le remplacer ?", name))) return;

        // Les onglets d'une connexion rapide non enregistrée n'ont pas de fiche pour y revenir : ils ne sont pas gardés.
        var set = new TabSet
        {
            Name = name,
            Left = WithSavedRemoteTabs(Left.CaptureState(withView: true)),
            Right = WithSavedRemoteTabs(Right.CaptureState(withView: true)),
        };
        int lost = Left.Tabs.Count + Right.Tabs.Count - set.Left.Tabs.Count - set.Right.Tabs.Count;
        if (index >= 0) _settings.TabSets[index] = set;
        else _settings.TabSets.Add(set);
        SaveTabSets();
        StatusMessage = lost > 0
            ? L.F("« {0} » enregistré, sans {1} onglet(s) de connexion rapide (pas de fiche enregistrée).", name, lost)
            : L.F("« {0} » enregistré : {1}.", name, set.Summary);
    }

    /// <summary>Rappelle un jeu d'onglets dans les deux panneaux : les serveurs d'abord, puis les onglets sont remplacés.</summary>
    [RelayCommand]
    private async Task OpenTabSet(TabSet? set)
    {
        if (set is null) return;
        int locked = Left.Tabs.Concat(Right.Tabs).Count(t => t.IsLocked);
        if (locked > 0 && !await _dialogs.ConfirmAsync(L.T("Jeu d'onglets"),
                L.F("{0} onglet(s) verrouillé(s) seront fermés par « {1} ». Continuer ?", locked, set.Name))) return;

        int skipped = await ReplaceAllTabsAsync(set);
        StatusMessage = L.F("Jeu d'onglets « {0} » ouvert.", set.Name);
        if (skipped > 0)
            await _dialogs.ShowErrorAsync(L.T("Jeu d'onglets"), L.F("{0} onglet(s) de « {1} » n'ont pas été rouverts : ", skipped, set.Name) +
                L.T("dossier disparu, connexion supprimée ou connexion refusée."));
    }

    /// <summary>Remplace les onglets des deux panneaux ; un panneau sans onglet possible garde les siens. Retourne le nombre d'onglets pas rouverts.</summary>
    private async Task<int> ReplaceAllTabsAsync(TabSet set)
    {
        var sessions = await OpenSessionsOfAsync(set);
        var (left, leftActive, leftSkipped) = await ResolveTabsAsync(set.Left, sessions);
        var (right, rightActive, rightSkipped) = await ResolveTabsAsync(set.Right, sessions);
        await Left.ReplaceTabsAsync(left, leftActive, set.Left.SortColumn, set.Left.SortDescending);
        await Right.ReplaceTabsAsync(right, rightActive, set.Right.SortColumn, set.Right.SortDescending);
        _unopenedTabs.Clear(); // ils appartenaient à la disposition remplacée
        return leftSkipped + rightSkipped;
    }

    /// <summary>
    /// Onglets d'une sauvegarde ou d'une synchronisation : ils remplacent ceux ouverts, après accord.
    /// Ceux d'un dossier d'un autre système, ou d'une connexion absente d'ici, sont écartés et comptés.
    /// </summary>
    private async Task OpenReceivedTabsAsync(TransferredTabs received)
    {
        var (set, notHere) = TabTransfer.ForThisMachine(received, _connections?.Profiles ?? [], OperatingSystem.IsWindows());
        int possible = set.Left.Tabs.Count + set.Right.Tabs.Count;
        string title = L.T("Onglets reçus");
        if (possible == 0)
        {
            await _dialogs.ShowErrorAsync(title, L.F("Aucun des {0} onglet(s) reçus ne peut s'ouvrir ici : ", received.Count) +
                L.T("dossiers d'un autre système, ou connexions absentes de cette machine."));
            return;
        }
        int open = Left.Tabs.Count + Right.Tabs.Count;
        int locked = Left.Tabs.Concat(Right.Tabs).Count(t => t.IsLocked);
        var question = L.F("Remplacer les {0} onglet(s) ouverts par les {1} onglet(s) reçus ?", open, possible);
        if (locked > 0) question += "\n" + L.F("{0} onglet(s) verrouillé(s) seront fermés.", locked);
        if (notHere > 0) question += "\n" + L.F("{0} onglet(s) reçus ne peuvent pas s'ouvrir ici (dossier d'un autre système ou connexion absente).", notHere);
        if (!await _dialogs.ConfirmAsync(title, question)) return;

        int skipped = notHere + await ReplaceAllTabsAsync(set);
        StatusMessage = skipped > 0 ? L.F("Onglets reçus ouverts, sauf {0}.", skipped) : L.T("Onglets reçus ouverts.");
    }

    /// <summary>Ouvre les connexions du jeu ; celles refusées ou annulées sont simplement absentes du résultat.</summary>
    private async Task<Dictionary<Guid, RemoteSession>> OpenSessionsOfAsync(TabSet set)
    {
        var sessions = new Dictionary<Guid, RemoteSession>();
        var ids = set.Left.Tabs.Concat(set.Right.Tabs).Select(t => SavedRemoteTab.TryDecode(SavedTab.Decode(t).Path, out var id, out _) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty).Distinct();
        foreach (var id in ids)
        {
            if (_connections?.FindProfile(id) is not { } profile) continue;
            if (_remote?.FindByProfile(id) is { } open) { sessions[id] = open; continue; }
            await WithSessionAsync(profile, session =>
            {
                sessions[id] = session;
                return Task.CompletedTask;
            });
        }
        return sessions;
    }

    /// <summary>Onglets encore possibles, avec leur vrai chemin ; ActiveTab suit l'onglet actif malgré ceux écartés.</summary>
    private async Task<(List<SavedTab> Tabs, int Active, int Skipped)> ResolveTabsAsync(PanelState state, Dictionary<Guid, RemoteSession> sessions)
    {
        var tabs = new List<SavedTab>();
        int active = 0, skipped = 0;
        for (int i = 0; i < state.Tabs.Count; i++)
        {
            var saved = SavedTab.Decode(state.Tabs[i]);
            if (await ResolveTabPathAsync(saved.Path, sessions) is not { } path) { skipped++; continue; }
            if (i == state.ActiveTab) active = tabs.Count;
            tabs.Add(saved with { Path = path });
        }
        return (tabs, active, skipped);
    }

    private static async Task<string?> ResolveTabPathAsync(string stored, Dictionary<Guid, RemoteSession> sessions)
    {
        if (!SavedRemoteTab.TryDecode(stored, out var id, out var path))
            return Directory.Exists(stored) || (TwinPanel.Core.Archives.ArchivePath.TrySplit(stored, out var archive, out _) && File.Exists(archive)) ? stored : null;
        if (!sessions.TryGetValue(id, out var session)) return null;
        try
        {
            var item = await session.UseAsync(fs => fs.StatAsync(path, CancellationToken.None), CancellationToken.None);
            return item is { IsDirectory: true } ? session.PathFor(path).ToString() : null;
        }
        catch (Exception ex) when (ex is RemoteException or IOException) { return null; }
    }

    [RelayCommand]
    private async Task DeleteTabSet(TabSet? set)
    {
        if (set is null || !await _dialogs.ConfirmAsync(L.T("Jeu d'onglets"), L.F("Supprimer le jeu « {0} » ?", set.Name))) return;
        _settings.TabSets.Remove(set);
        SaveTabSets();
        StatusMessage = L.F("Jeu d'onglets « {0} » supprimé.", set.Name);
    }

    private void SaveTabSets()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = L.T("Jeux d'onglets non enregistrés : ") + ex.Message;
        }
    }

    // ---- Vue à plat --------------------------------------------------------------------------

    /// <summary>Ctrl+B : tous les fichiers des sous-dossiers dans la liste, ou retour à la liste normale.</summary>
    [RelayCommand] private void ToggleFlatView() => ActivePanel.Current.ToggleFlat();

    /// <summary>Lecture d'un serveur sur une connexion à part : les panneaux restent utilisables, aucun identifiant n'est redemandé.</summary>
    private async IAsyncEnumerable<TwinPanel.Core.FileEntry> ListRemoteFlatAsync(string path, bool showHidden, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!PanelRemotePath.TryParse(path, out var root) || _remote?.Find(root.Session) is not { } session)
            throw new RemoteException(L.T("La connexion de ce panneau n'est plus ouverte."));
        await using var fs = await session.OpenWorkerAsync(ct);
        await foreach (var e in RemoteSearch.SearchAsync(fs, root, FlatListing.Criteria(path, showHidden), null, ct))
            yield return e;
    }

    /// <summary>
    /// Vue à plat : plusieurs éléments marqués peuvent porter le même nom ; dans un seul dossier d'arrivée, ils s'écraseraient.
    /// Rend false si l'utilisateur renonce ; sinon la conduite imposée (null : aucun doublon, conduite habituelle).
    /// </summary>
    private async Task<(bool Go, ConflictPolicy? Forced)> CheckSameNamesAsync(IReadOnlyList<TwinPanel.Core.FileEntry> items, string verb)
    {
        var duplicates = FlatListing.DuplicateNames(items.Select(i => i.Name));
        if (duplicates.Count == 0) return (true, null);
        var choice = await _dialogs.ChooseAsync<ConflictPolicy>(verb,
            L.F("{0} nom(s) reviennent plusieurs fois, par exemple « {1} ».\n", duplicates.Count, duplicates[0]) +
            L.T("Dans un même dossier, ces fichiers s'écraseraient. Les garder tous, en numérotant les doublons ?"),
            (L.T("Tout garder"), ConflictPolicy.RenameNew), (L.T("Annuler"), null));
        return (choice is not null, choice);
    }

    [RelayCommand]
    private void ClearRecent()
    {
        _recent.Clear();
        SaveRecent();
        StatusMessage = L.T("Historique des dossiers vidé.");
    }

    private void SaveRecent()
    {
        try { _recent.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(L.F("Historique non sauvegardé : {0}", ex.Message));
        }
    }
}
