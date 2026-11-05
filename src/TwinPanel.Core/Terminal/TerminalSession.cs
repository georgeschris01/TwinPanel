// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;

namespace TwinPanel.Core.Terminal;

/// <summary>Ce qui est relié au terminal : un programme du PC, ou un shell sur un serveur.</summary>
public interface ITerminalConnection : IDisposable
{
    /// <summary>Arrive d'un autre fil d'exécution.</summary>
    event Action<byte[], int>? DataReceived;

    /// <summary>La connexion est finie. Le texte explique pourquoi s'il y a eu un problème, sinon il est nul.</summary>
    event Action<string?>? Closed;

    void Start();
    void Write(ReadOnlySpan<byte> data);
    void Resize(int columns, int rows);
}

/// <summary>Un terminal en marche : l'écran virtuel, sa connexion, et le verrou qui les protège.</summary>
public sealed class TerminalSession : IDisposable
{
    private readonly ITerminalConnection _connection;
    private int _closed;

    public TerminalSession(ITerminalConnection connection, int columns, int rows, int historyLimit = 10_000)
    {
        _connection = connection;
        Screen = new TerminalEmulator(columns, rows, historyLimit);
        Screen.Reply += Send;
        Screen.TitleChanged += () => TitleChanged?.Invoke();
        connection.DataReceived += OnData;
        connection.Closed += OnClosed;
    }

    /// <summary>À prendre pour lire l'écran : le fil de réception y écrit.</summary>
    public object Gate { get; } = new();
    public TerminalEmulator Screen { get; }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>L'écran a changé. Arrive d'un autre fil d'exécution.</summary>
    public event Action? Changed;
    public event Action? TitleChanged;
    /// <summary>La connexion est finie. Le texte explique pourquoi s'il y a eu un problème, sinon il est nul.</summary>
    public event Action<string?>? Closed;

    public void Start() => _connection.Start();

    private void OnData(byte[] data, int length)
    {
        lock (Gate) Screen.Feed(data.AsSpan(0, length));
        Changed?.Invoke();
    }

    private void OnClosed(string? reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        Closed?.Invoke(reason);
    }

    public void Send(string text) => Send(Encoding.UTF8.GetBytes(text));

    public void Send(ReadOnlySpan<byte> data)
    {
        if (!IsClosed) _connection.Write(data);
    }

    /// <summary>Message de TwinPanel affiché dans le terminal : il ne passe pas par le programme.</summary>
    public void ShowMessage(string text)
    {
        lock (Gate) Screen.Feed(Encoding.UTF8.GetBytes("\r\n\u001b[0m" + text + "\r\n"));
        Changed?.Invoke();
    }

    public void Resize(int columns, int rows)
    {
        lock (Gate)
        {
            if (columns == Screen.Columns && rows == Screen.Rows) return;
            Screen.Resize(columns, rows);
        }
        if (!IsClosed) _connection.Resize(columns, rows);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _closed, 1);
        _connection.DataReceived -= OnData;
        _connection.Closed -= OnClosed;
        _connection.Dispose();
    }
}
