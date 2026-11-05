// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TwinPanel.App.Services;
using TwinPanel.App.ViewModels;
using TwinPanel.App.Views;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Sources;

namespace TwinPanel.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Texte coupé par « … » : infobulle avec le texte entier, dans toutes les fenêtres.
            Views.TrimmedTextTips.Install();
            var window = new MainWindow();
            var vm = new MainWindowViewModel(new DialogService(window), SettingsStore.Default, FileSourceRegistry.Default);
            vm.LoadSettings();

            // Lot 6 : connexions distantes (liste, coffre et serveurs connus dans %APPDATA%\TwinPanel).
            var connections = new TwinPanel.Core.Remote.ConnectionStore(TwinPanel.Core.Remote.ConnectionStore.DefaultDirectory,
                openSshKnownHosts: TwinPanel.Core.Remote.KnownHosts.DefaultOpenSshFile);
            var sessions = new TwinPanel.Core.Remote.RemoteSessionManager();
            FileSourceRegistry.Default.Register(new TwinPanel.Core.Remote.RemoteFileSource(sessions));
            vm.AttachRemote(connections, sessions, new ConnectionPrompts());
            // À la fermeture : déconnexion propre (5 s au plus, hors du fil de l'interface pour ne rien bloquer).
            desktop.ShutdownRequested += (_, _) =>
            {
                vm.StopRemoteEdits();
                TwinPanel.Core.Archives.ArchivePasswords.ForgetAll();
                Task.Run(vm.ShutdownRemoteAsync).Wait(TimeSpan.FromSeconds(5));
            };

            window.Width = vm.Settings.WindowWidth;
            window.Height = vm.Settings.WindowHeight;
            if (vm.Settings.WindowMaximized) window.WindowState = WindowState.Maximized;

            window.DataContext = vm;
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
