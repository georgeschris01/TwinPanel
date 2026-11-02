// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Renci.SshNet;
using Renci.SshNet.Common;
using TwinPanel.Core.Terminal;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Shell sur un serveur SSH. Connexion à part de celle des panneaux, préparée de la même façon :
/// coffre, clés, rebond et contrôle des serveurs connus.
/// </summary>
public sealed class SshTerminalConnection : ITerminalConnection
{
    private readonly SshConnector.Prepared _prepared;
    private readonly SshClient _client;
    private readonly ShellStream _shell;
    private readonly string? _startDirectory;
    private int _closed;

    private SshTerminalConnection(SshConnector.Prepared prepared, SshClient client, ShellStream shell, string? startDirectory)
    {
        _prepared = prepared;
        _client = client;
        _shell = shell;
        _startDirectory = startDirectory;
    }

    public event Action<byte[], int>? DataReceived;
    public event Action<string?>? Closed;

    internal static async Task<SshTerminalConnection> ConnectAsync(ConnectionContext ctx, int columns, int rows, string? startDirectory, CancellationToken ct)
    {
        var prepared = await SshConnector.PrepareAsync(ctx, ct).ConfigureAwait(false);
        SshClient? client = null;
        try
        {
            client = new SshClient(prepared.Info);
            await SshConnector.ConnectAsync(client, ctx, ct).ConfigureAwait(false);
            var shell = client.CreateShellStream("xterm-256color", (uint)columns, (uint)rows, 0, 0, 64 * 1024);
            return new SshTerminalConnection(prepared, client, shell, startDirectory);
        }
        catch
        {
            client?.Dispose();
            prepared.Dispose();
            throw;
        }
    }

    public void Start()
    {
        _shell.Closed += (_, _) => Close(null);
        _shell.ErrorOccurred += (_, e) => Close(e.Exception.Message);
        new Thread(ReadLoop) { IsBackground = true, Name = "Terminal : lecture SSH" }.Start();
        if (_startDirectory is { Length: > 0 } directory && CanTypePath(directory))
            Write(System.Text.Encoding.UTF8.GetBytes(ChangeDirectoryCommand(directory)));
    }

    /// <summary>
    /// Ce chemin est tapé dans le shell : un caractère de contrôle y agirait avant les guillemets. Ctrl+U, par exemple,
    /// effacerait le début de la ligne, et un dossier piégé ferait exécuter la fin de son nom.
    /// </summary>
    public static bool CanTypePath(string directory) => !directory.Any(char.IsControl);

    /// <summary>Commande qui place le shell dans le dossier du panneau, sans laisser de trace à l'écran.</summary>
    public static string ChangeDirectoryCommand(string directory) =>
        "cd -- '" + directory.Replace("'", "'\\''", StringComparison.Ordinal) + "' && clear\n";

    private void ReadLoop()
    {
        try
        {
            TerminalBacklog.Pump(_shell.Read, () => _shell.Length, (data, length) => DataReceived?.Invoke(data, length));
            Close(null);
        }
        catch (Exception ex) when (ex is SshException or IOException or ObjectDisposedException)
        {
            Close(ex.Message);
        }
    }

    private void Close(string? reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0) Closed?.Invoke(reason);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        try
        {
            _shell.Write(data.ToArray(), 0, data.Length);
            _shell.Flush();
        }
        catch (Exception ex) when (ex is SshException or IOException or ObjectDisposedException)
        {
            Close(ex.Message);
        }
    }

    public void Resize(int columns, int rows)
    {
        try { _shell.ChangeWindowSize((uint)columns, (uint)rows, 0, 0); }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException)
        {
            // Connexion en train de se fermer : l'écran local reste juste.
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _closed, 1);
        _shell.Dispose();
        _client.Dispose();
        _prepared.Dispose();
    }
}
