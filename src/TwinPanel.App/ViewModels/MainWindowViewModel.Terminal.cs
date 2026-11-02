// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Terminal;

namespace TwinPanel.App.ViewModels;

public partial class MainWindowViewModel
{
    /// <summary>Panneau du terminal intégré, sous les deux listes.</summary>
    public TerminalPanelViewModel Terminal { get; } = new();

    public bool IsTerminalEnabled => _settings.Terminal.IntegratedEnabled;

    /// <summary>F12 : ouvre le panneau, y met le clavier, ou le masque si le clavier y est déjà. Les terminaux continuent de tourner.</summary>
    [RelayCommand]
    private async Task ToggleTerminal()
    {
        if (!IsTerminalEnabled)
        {
            StatusMessage = L.T("Le terminal intégré est désactivé (Options → Terminal).");
            return;
        }
        if (Terminal.IsOpen)
        {
            if (Terminal.HasFocus) Terminal.Hide();
            else Terminal.RequestFocus();
            return;
        }
        if (Terminal.Tabs.Count == 0) await NewTerminal();
        else Terminal.Show();
    }

    /// <summary>Nouveau terminal dans le dossier du panneau actif : sur ce PC, ou sur le serveur SSH affiché.</summary>
    [RelayCommand]
    private async Task NewTerminal()
    {
        if (!IsTerminalEnabled)
        {
            StatusMessage = L.T("Le terminal intégré est désactivé (Options → Terminal).");
            return;
        }
        ApplyTerminalSettings();
        var current = ActivePanel.Current;
        if (current.IsRemote) await OpenRemoteTerminalAsync(current);
        else await OpenLocalTerminalAsync(current);
    }

    private async Task OpenLocalTerminalAsync(FolderTabViewModel folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            await _dialogs.ShowErrorAsync("Terminal", L.T("Le terminal intégré n'existe pour l'instant que sous Windows."));
            return;
        }
        var directory = !folder.IsInsideArchive && Directory.Exists(folder.CurrentPath)
            ? folder.CurrentPath
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tab = Terminal.Add(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : directory);
        try
        {
            tab.Start(new ConPtyConnection(TerminalShells.CommandLine(_settings.Terminal), directory, Terminal.Columns, Terminal.Rows), Terminal.Columns, Terminal.Rows);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or DllNotFoundException or EntryPointNotFoundException)
        {
            Terminal.CloseTab(tab);
            await _dialogs.ShowErrorAsync("Terminal", L.F("Le terminal n'a pas pu démarrer : {0}", ex.Message));
        }
    }

    private async Task OpenRemoteTerminalAsync(FolderTabViewModel folder)
    {
        if (SessionOf(folder) is not { } session || !RemoteProtocols.IsSsh(session.Profile.Protocol))
        {
            await _dialogs.ShowErrorAsync("Terminal", L.T("Un terminal ne peut s'ouvrir que sur ton PC ou sur un serveur SSH (SFTP / SCP)."));
            return;
        }
        var directory = !folder.IsInsideArchive && PanelRemotePath.TryParse(folder.CurrentPath, out var path) ? path.Path : null;
        var tab = Terminal.Add(session.Profile.Name);
        var cancellation = tab.Cancellation.Token;
        try
        {
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
            Terminal.CloseTab(tab);
        }
        catch (Exception ex) when (ex is RemoteException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Terminal.CloseTab(tab);
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
