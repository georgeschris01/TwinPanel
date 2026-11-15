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
            // Version portable, premier démarrage sur un PC où TwinPanel est installé : reprendre ses réglages ?
            if (AppPaths.CanImportFromInstalled(AppPaths.Current, AppPaths.InstalledDirectory))
            {
                var ask = new PortableImportWindow(accepted =>
                {
                    if (accepted) ImportInstalledSettings();
                    StartMain(desktop).Show(); // la fenêtre principale s'ouvre avant que la question se ferme
                });
                desktop.MainWindow = ask;
            }
            else StartMain(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ImportInstalledSettings()
    {
        try
        {
            AppPaths.ImportFromInstalled(AppPaths.InstalledDirectory, AppPaths.DataDirectory);
            L.Use(L.Resolve(SettingsStore.Default.Load().Language, System.Globalization.CultureInfo.CurrentUICulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write(ex, fatal: false);
        }
    }

    private static MainWindow StartMain(IClassicDesktopStyleApplicationLifetime desktop)
    {
        {
            var window = new MainWindow();
            var vm = new MainWindowViewModel(new DialogService(window), SettingsStore.Default, FileSourceRegistry.Default);
            vm.LoadSettings();

            // Lot 6 : connexions distantes (liste, coffre et serveurs connus dans %APPDATA%\TwinPanel).
            // Version portable : coffre protégé seulement par le mot de passe maître (il doit s'ouvrir sur n'importe quel PC).
            var connections = new TwinPanel.Core.Remote.ConnectionStore(TwinPanel.Core.Remote.ConnectionStore.DefaultDirectory,
                AppPaths.IsPortable ? new PortableDataProtector() : null,
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
            if (AppPaths.Current.ReadOnlyCopy)
            {
                // Portable sur un dossier en lecture seule : prévenu une fois, TwinPanel fonctionne sans rien garder.
                var dialogs = new DialogService(window);
                window.Opened += async (_, _) => await dialogs.ShowErrorAsync(L.T("Version portable"),
                    L.F("Le dossier de TwinPanel est en lecture seule :{0}{1}{0}{0}", Environment.NewLine, AppPaths.Current.PortableData ?? string.Empty) +
                    L.T("TwinPanel fonctionne normalement, mais tes changements (réglages, onglets, connexions) ne seront pas gardés après la fermeture."));
            }
            return window;
        }
    }
}
