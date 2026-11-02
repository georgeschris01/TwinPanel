// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Threading;

namespace TwinPanel.App.ViewModels;

/// <summary>Un serveur ne prévient pas quand un dossier change : celui affiché est relu de temps en temps, tant que TwinPanel est au premier plan.</summary>
public partial class MainWindowViewModel
{
    private static readonly TimeSpan RemotePollTick = TimeSpan.FromSeconds(1);
    private readonly DispatcherTimer _remotePoll = new() { Interval = RemotePollTick };
    private bool _appActive = true;

    private void StartRemoteRefresh()
    {
        _remotePoll.Tick += (_, _) =>
        {
            PollRemoteFolders(force: false);
            CloseUnusedServerArchives();
        };
        _remotePoll.Start();
    }

    /// <summary>Revenir sur TwinPanel relit tout de suite le dossier affiché.</summary>
    public void SetAppActive(bool active)
    {
        _appActive = active;
        if (active) PollRemoteFolders(force: true);
    }

    private void PollRemoteFolders(bool force)
    {
        int seconds = _settings.Behavior.RemoteRefreshSeconds;
        if (seconds <= 0 || !_appActive) return;
        foreach (var panel in new[] { Left, Right })
        {
            // Connexion perdue : surtout pas de reconnexion en douce, elle pourrait demander un mot de passe toutes les quelques secondes.
            if (SessionOf(panel.Current) is { IsConnected: true }) _ = panel.Current.PollRemoteAsync(TimeSpan.FromSeconds(seconds), force);
        }
    }
}
