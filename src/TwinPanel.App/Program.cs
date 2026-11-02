// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Threading;

namespace TwinPanel.App;

internal static class Program
{
    /// <summary>Relancer TwinPanel après sa fermeture (changement de langue).</summary>
    public static bool RestartAfterExit { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) => { CrashLog.Write(e.Exception, fatal: false); e.SetObserved(); };
        try
        {
            L.Use(L.Resolve(TwinPanel.Core.Settings.SettingsStore.Default.Load().Language, System.Globalization.CultureInfo.CurrentUICulture));
            BuildAvaloniaApp().AfterSetup(_ =>
            {
                // Une erreur imprévue dans l'interface est notée dans crash.log au lieu de fermer TwinPanel.
                Dispatcher.UIThread.UnhandledException += (_, e) =>
                {
                    CrashLog.Write(e.Exception, fatal: false);
                    e.Handled = true;
                };
                HangWatchdog.Start();
            }).StartWithClassicDesktopLifetime(args);
            // Dans une AppImage, le programme vit dans un dossier temporaire qui disparaît à la fermeture : on repart du fichier AppImage.
            if (RestartAfterExit && (Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath) is { } program)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(program) { UseShellExecute = false });
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, fatal: true);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

/// <summary>
/// Journal des erreurs imprévues : %APPDATA%\TwinPanel\crash.log et, à côté du programme, crash.log.
/// Aucune donnée personnelle n'y est écrite volontairement (seulement le type d'erreur et l'endroit dans le code).
/// </summary>
internal static class CrashLog
{
    public static void Write(Exception? ex, bool fatal)
    {
        if (ex is null) return;
        var text = $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {(fatal ? "ARRÊT" : "erreur rattrapée")} ====={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
        foreach (var dir in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TwinPanel"),
                     AppContext.BaseDirectory,
                 })
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), text);
            }
            catch (Exception) { /* le journal ne doit jamais provoquer lui-même un plantage */ }
        }
    }
}

/// <summary>
/// Repère les blocages de l'interface : un fil à part vérifie chaque seconde que l'interface répond.
/// Au-delà de 5 secondes sans réponse, il écrit dans crash.log les dernières étapes notées (<see cref="Steps"/>).
/// </summary>
internal static class HangWatchdog
{
    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private static long _lastPongTicks = DateTime.UtcNow.Ticks;

    /// <summary>Note une étape (gardée en mémoire, les 40 dernières).</summary>
    public static void Step(string what)
    {
        lock (Gate)
        {
            Recent.Enqueue($"{DateTime.Now:HH:mm:ss.fff}  {what}");
            while (Recent.Count > 40) Recent.Dequeue();
        }
    }

    public static void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Surveillance de l'interface" };
        thread.Start();
    }

    private static void Run()
    {
        bool reported = false;
        while (true)
        {
            Thread.Sleep(1000);
            Dispatcher.UIThread.Post(() => Interlocked.Exchange(ref _lastPongTicks, DateTime.UtcNow.Ticks));
            var silent = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastPongTicks), DateTimeKind.Utc);
            if (silent > TimeSpan.FromSeconds(5) && !reported)
            {
                reported = true;
                string steps;
                lock (Gate) steps = string.Join(Environment.NewLine, Recent);
                CrashLog.Write(new TimeoutException($"INTERFACE BLOQUÉE depuis {silent.TotalSeconds:0} s. Dernières étapes :{Environment.NewLine}{steps}"), fatal: false);
            }
            else if (silent < TimeSpan.FromSeconds(2) && reported)
            {
                reported = false;
                Step("l'interface répond de nouveau");
            }
        }
    }
}
