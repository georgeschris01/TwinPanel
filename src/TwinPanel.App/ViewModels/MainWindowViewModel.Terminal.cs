// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Terminal;

namespace TwinPanel.App.ViewModels;

public partial class MainWindowViewModel
{
    private TerminalCommandsViewModel? _terminalCommands;

    /// <summary>Panneau du terminal intégré, sous les deux listes.</summary>
    public TerminalPanelViewModel Terminal { get; } = new();

    /// <summary>Commandes enregistrées, à droite du terminal.</summary>
    public TerminalCommandsViewModel TerminalCommands => _terminalCommands ??= CreateTerminalCommands();

    public bool IsTerminalEnabled => _settings.Terminal.IntegratedEnabled;

    /// <summary>Serveurs SSH proposés par le bouton Terminal : les plus récemment utilisés d'abord.</summary>
    public IReadOnlyList<ConnectionProfile> TerminalServers =>
        (_connections?.Profiles ?? []).Where(p => RemoteProtocols.IsSsh(p.Protocol))
            .OrderByDescending(p => p.LastUsedUtc ?? DateTime.MinValue).ThenBy(p => p.Name).Take(15).ToList();

    public bool IsServerConnected(ConnectionProfile profile) => _remote?.FindByProfile(profile.Id) is not null;

    private TerminalCommandsViewModel CreateTerminalCommands()
    {
        var commands = new TerminalCommandsViewModel(Terminal, _dialogs, () => _settings.Terminal, SaveTerminalCommands, text => StatusMessage = text);
        commands.Refresh();
        return commands;
    }

    private void SaveTerminalCommands()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusMessage = ex.Message; }
    }

    private bool TerminalAllowed()
    {
        if (IsTerminalEnabled) return true;
        StatusMessage = L.T("Le terminal intégré est désactivé (Options → Terminal).");
        return false;
    }

    /// <summary>F12 : ouvre le panneau, y met le clavier, ou le masque si le clavier y est déjà. Les terminaux continuent de tourner.</summary>
    [RelayCommand]
    private async Task ToggleTerminal()
    {
        if (!TerminalAllowed()) return;
        if (Terminal.IsOpen)
        {
            if (Terminal.HasFocus) Terminal.Hide();
            else Terminal.RequestFocus();
            return;
        }
        if (Terminal.Tabs.Count == 0) await NewTerminal();
        else Terminal.Show();
    }

    /// <summary>Un clic dans un menu prend le clavier : ici, on affiche ou on masque, sans regarder où il est.</summary>
    [RelayCommand]
    private async Task ToggleTerminalPanel()
    {
        if (Terminal.IsOpen) Terminal.Hide();
        else await ToggleTerminal();
    }

    /// <summary>Nouveau terminal dans le dossier du panneau actif : sur ce PC, ou sur le serveur SSH affiché.</summary>
    [RelayCommand]
    private async Task NewTerminal()
    {
        if (!TerminalAllowed()) return;
        var current = ActivePanel.Current;
        if (!current.IsRemote)
        {
            await OpenLocalTerminalAsync(_settings.Terminal.IntegratedShell, ActiveLocalFolder());
            return;
        }
        if (SessionOf(current) is not { } session || !RemoteProtocols.IsSsh(session.Profile.Protocol))
        {
            await _dialogs.ShowErrorAsync("Terminal", L.T("Un terminal ne peut s'ouvrir que sur ton PC ou sur un serveur SSH (SFTP / SCP)."));
            return;
        }
        var directory = !current.IsInsideArchive && PanelRemotePath.TryParse(current.CurrentPath, out var path) ? path.Path : null;
        await OpenServerTerminalAsync(session.Profile, directory);
    }

    /// <summary>Bouton Terminal : un programme précis, sur ce PC.</summary>
    [RelayCommand]
    private async Task NewShellTerminal(TerminalShell shell)
    {
        if (TerminalAllowed()) await OpenLocalTerminalAsync(shell, ActiveLocalFolder());
    }

    /// <summary>Bouton Terminal : un serveur, connecté ou non. La connexion passe par le coffre, comme pour un panneau.</summary>
    [RelayCommand]
    private async Task NewServerTerminal(ConnectionProfile? profile)
    {
        if (profile is not null && TerminalAllowed()) await OpenServerTerminalAsync(profile, null);
    }

    /// <summary>Rouvre le même programme ou le même serveur, dans le dossier de départ de ce terminal.</summary>
    [RelayCommand]
    private async Task DuplicateTerminal(TerminalTabViewModel? tab)
    {
        // Le bouton de l'onglet : la copie s'ouvre du même côté que l'original.
        if ((tab ?? Terminal.Selected) is { } source) await DuplicateTerminalTo(source, Terminal.RightTabs.Contains(source));
    }

    /// <summary>Clic droit sur un onglet : la copie s'ouvre du côté choisi.</summary>
    public async Task DuplicateTerminalTo(TerminalTabViewModel tab, bool right)
    {
        if (tab.Reopen is not { } reopen || !TerminalAllowed()) return;
        Terminal.ActivatePane(right);
        await reopen();
    }

    /// <summary>Clic droit sur un onglet : relance son terminal dans ce même onglet, même si la connexion a été coupée.</summary>
    public async Task ReconnectTerminal(TerminalTabViewModel tab)
    {
        if (tab.Restart is not { } restart || tab.IsRestarting || !TerminalAllowed() || !Terminal.Tabs.Contains(tab)) return;
        Terminal.SelectTab(tab);
        bool toServer = tab.ServerId is not null;
        tab.IsRestarting = true;
        try
        {
            if (tab.Session is { IsClosed: false } && !await _dialogs.ConfirmAsync("Terminal", toServer
                    ? L.T("Ce terminal semble encore connecté. Le reconnecter ferme ce qui s'y exécute. Continuer ?")
                    : L.T("Ce terminal est encore en marche. Le relancer ferme ce qui s'y exécute. Continuer ?")))
                return;
            if (!Terminal.Tabs.Contains(tab)) return;
            var previous = tab.Session;
            if (toServer) StatusMessage = L.F("Reconnexion à {0}…", tab.ServerName);
            await restart();
            if (tab.Session == previous) return;
            StatusMessage = toServer ? L.F("Terminal reconnecté à {0}.", tab.ServerName) : L.T("Terminal relancé.");
            Terminal.RequestFocus();
        }
        finally { tab.IsRestarting = false; }
    }

    /// <summary>Deux terminaux côte à côte ; s'il n'y en a qu'un, le second est sa copie.</summary>
    [RelayCommand]
    private async Task ToggleTerminalSplit()
    {
        if (!TerminalAllowed()) return;
        if (Terminal.IsSplit)
        {
            Terminal.Unsplit();
            return;
        }
        if (Terminal.Tabs.Count == 0) await NewTerminal();
        Terminal.Show();
        if (Terminal.Selected is not { } tab || Terminal.Split()) return;
        if (tab.Reopen is { } reopen) await reopen();
        else Terminal.Unsplit();
    }

    /// <summary>En côte à côte : donne le clavier à l'autre côté.</summary>
    [RelayCommand]
    private void SwitchTerminalSide()
    {
        if (!Terminal.IsSplit) return;
        Terminal.ActivatePane(!Terminal.ActiveIsRight);
        Terminal.Show();
    }

    [RelayCommand]
    private void SearchTerminal()
    {
        if (TerminalAllowed() && Terminal.Tabs.Count > 0) Terminal.OpenSearch();
    }

    private string ActiveLocalFolder()
    {
        var folder = ActivePanel.Current;
        return !folder.IsRemote && !folder.IsInsideArchive && Directory.Exists(folder.CurrentPath)
            ? folder.CurrentPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private async Task OpenLocalTerminalAsync(TerminalShell shell, string directory, TerminalFamily? family = null)
    {
        family ??= new TerminalFamily();
        ApplyTerminalSettings();
        var tab = Terminal.Add(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : directory);
        tab.CopyNumber = family.Next();
        tab.Reopen = () => OpenLocalTerminalAsync(shell, directory, family);
        tab.Restart = () => StartLocalTerminalAsync(tab, shell, directory);
        await StartLocalTerminalAsync(tab, shell, directory);
    }

    /// <summary>Un échec ferme l'onglet s'il est neuf ; sinon il garde son écran.</summary>
    private async Task StartLocalTerminalAsync(TerminalTabViewModel tab, TerminalShell shell, string directory)
    {
        var chosen = new TerminalSettings { IntegratedShell = shell, IntegratedShellPath = _settings.Terminal.IntegratedShellPath };
        try
        {
            tab.Start(TerminalShells.Connect(chosen, directory, Terminal.Columns, Terminal.Rows), Terminal.Columns, Terminal.Rows);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            if (tab.Session is null) Terminal.CloseTab(tab);
            await _dialogs.ShowErrorAsync("Terminal", L.F("Le terminal n'a pas pu démarrer : {0}", ex.Message));
        }
    }

    private async Task OpenServerTerminalAsync(ConnectionProfile profile, string? directory, TerminalFamily? family = null)
    {
        family ??= new TerminalFamily();
        ApplyTerminalSettings();
        if (_remote is null) return;
        if (directory is not null && !SshTerminalConnection.CanTypePath(directory))
        {
            StatusMessage = L.T("Le nom de ce dossier contient des caractères de contrôle : le terminal s'ouvre dans le dossier personnel.");
            directory = null;
        }
        var tab = Terminal.Add(profile.Name);
        tab.ServerId = profile.Id;
        tab.ServerName = profile.Name;
        tab.CopyNumber = family.Next();
        tab.Reopen = () => OpenServerTerminalAsync(profile, directory, family);
        tab.Restart = () => StartServerTerminalAsync(tab, profile, directory);
        _terminalCommands?.Refresh();
        await StartServerTerminalAsync(tab, profile, directory);
    }

    /// <summary>Un échec ferme l'onglet s'il est neuf ; sinon il garde son écran.</summary>
    private async Task StartServerTerminalAsync(TerminalTabViewModel tab, ConnectionProfile profile, string? directory)
    {
        var cancellation = tab.Cancellation.Token;
        try
        {
            // Rend la connexion déjà ouverte, ou en ouvre une avec les questions habituelles.
            var session = await Task.Run(() => _remote.ConnectAsync(profile, _connections, _prompts!, cancellation), cancellation);
            if (session is null)
            {
                if (tab.Session is null) Terminal.CloseTab(tab);
                return;
            }
            var connection = await session.OpenTerminalAsync(Terminal.Columns, Terminal.Rows, directory, cancellation);
            if (!Terminal.Tabs.Contains(tab))
            {
                connection.Dispose();
                return;
            }
            tab.Start(connection, Terminal.Columns, Terminal.Rows);
        }
        catch (OperationCanceledException)
        {
            if (tab.Session is null) Terminal.CloseTab(tab);
        }
        catch (Exception ex) when (ex is RemoteException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (tab.Session is null) Terminal.CloseTab(tab);
            await _dialogs.ShowErrorAsync("Terminal", ex.Message);
        }
    }

    /// <summary>Après les Options : taille du texte, et fermeture des terminaux si l'option a été décochée.</summary>
    private void ApplyTerminalSettings()
    {
        Terminal.FontSize = Math.Clamp(_settings.Terminal.IntegratedFontSize, 8, 32);
        if (!IsTerminalEnabled && Terminal.Tabs.Count > 0) Terminal.CloseAll();
    }
}
