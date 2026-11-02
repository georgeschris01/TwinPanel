// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TwinPanel.Core.Terminal;

/// <summary>
/// Programme du PC relié au terminal par une console virtuelle de Linux. Appels directs à la bibliothèque C : aucune bibliothèque de terminal.
/// Le programme est lancé par posix_spawn et non par fork : dupliquer un programme .NET en marche n'est pas sûr.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class UnixPtyConnection : ITerminalConnection
{
    private const string TerminalType = "xterm-256color";
    private const int WriteTimeoutMilliseconds = 5000;

    private readonly string _program;
    private readonly IReadOnlyList<string> _arguments;
    private readonly string? _workingDirectory;
    private readonly int _columns;
    private readonly int _rows;
    private readonly object _gate = new();

    private int _master = -1;
    private int _process;
    private int _disposed;

    public UnixPtyConnection(string program, IReadOnlyList<string> arguments, string? workingDirectory, int columns, int rows)
    {
        _program = program;
        _arguments = arguments;
        _workingDirectory = workingDirectory;
        _columns = columns;
        _rows = rows;
    }

    public event Action<byte[], int>? DataReceived;
    public event Action<string?>? Closed;

    public void Start()
    {
        int master = Native.posix_openpt(Native.ReadWrite | Native.NoControllingTerminal | Native.CloseOnExec | Native.NonBlocking);
        if (master < 0) throw LastError();
        try
        {
            if (Native.grantpt(master) != 0 || Native.unlockpt(master) != 0) throw LastError();
            var name = new byte[256];
            int result = Native.ptsname_r(master, name, name.Length);
            if (result != 0) throw Error(result);
            SetSize(master, _columns, _rows);
            _process = Spawn(System.Text.Encoding.UTF8.GetString(name, 0, Array.IndexOf(name, (byte)0)));
            _master = master;
        }
        catch
        {
            Native.close(master);
            throw;
        }

        new Thread(ReadLoop) { IsBackground = true, Name = "Terminal : lecture" }.Start();
        new Thread(WaitForExit) { IsBackground = true, Name = "Terminal : attente" }.Start();
    }

    /// <summary>
    /// Le programme ouvre lui-même sa console après avoir créé sa session : c'est ce qui en fait sa console de contrôle,
    /// sans quoi Ctrl+C ne l'atteindrait pas. Les signaux reprennent leur effet normal : .NET en ignore certains, et cela se transmettrait.
    /// </summary>
    private int Spawn(string console)
    {
        const int BufferSize = 1024; // Plus grand que ces structures de la bibliothèque C, dont la taille n'est pas publique.
        nint actions = Marshal.AllocHGlobal(BufferSize);
        nint attributes = Marshal.AllocHGlobal(BufferSize);
        nint signals = Marshal.AllocHGlobal(BufferSize);
        try
        {
            Check(Native.posix_spawn_file_actions_init(actions));
            try
            {
                Check(Native.posix_spawnattr_init(attributes));
                try
                {
                    if (_workingDirectory is { Length: > 0 }) Check(Native.posix_spawn_file_actions_addchdir_np(actions, _workingDirectory));
                    Check(Native.posix_spawn_file_actions_addopen(actions, 0, console, Native.ReadWrite, 0));
                    Check(Native.posix_spawn_file_actions_adddup2(actions, 0, 1));
                    Check(Native.posix_spawn_file_actions_adddup2(actions, 0, 2));

                    Check(Native.posix_spawnattr_setflags(attributes, Native.SpawnNewSession | Native.SpawnSignalDefaults | Native.SpawnSignalMask));
                    Native.sigfillset(signals);
                    Check(Native.posix_spawnattr_setsigdefault(attributes, signals));
                    Native.sigemptyset(signals);
                    Check(Native.posix_spawnattr_setsigmask(attributes, signals));

                    nint[] argv = TextList([_program, .. _arguments]);
                    nint[] environment = TextList(Variables(Environment.GetEnvironmentVariables()));
                    try
                    {
                        Check(Native.posix_spawn(out int process, _program, actions, attributes, argv, environment));
                        return process;
                    }
                    finally
                    {
                        FreeTextList(argv);
                        FreeTextList(environment);
                    }
                }
                finally { Native.posix_spawnattr_destroy(attributes); }
            }
            finally { Native.posix_spawn_file_actions_destroy(actions); }
        }
        finally
        {
            Marshal.FreeHGlobal(signals);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(actions);
        }
    }

    /// <summary>Liste de textes UTF-8 terminée par un pointeur nul, comme la bibliothèque C l'attend.</summary>
    private static nint[] TextList(IEnumerable<string> texts) => [.. texts.Select(Marshal.StringToCoTaskMemUTF8), 0];

    private static void FreeTextList(nint[] list)
    {
        foreach (var text in list) Marshal.FreeCoTaskMem(text);
    }

    /// <summary>Variables du programme lancé : celles de TwinPanel, sauf ce qui décrit le terminal.</summary>
    internal static IEnumerable<string> Variables(IDictionary current)
    {
        foreach (DictionaryEntry variable in current)
        {
            var name = (string)variable.Key;
            if (name is "TERM" or "LINES" or "COLUMNS" or "TERMCAP") continue;
            yield return $"{name}={variable.Value}";
        }
        yield return "TERM=" + TerminalType;
    }

    private void ReadLoop()
    {
        var buffer = new byte[16 * 1024];
        int master = _master;
        var wait = new Native.PollEntry { File = master, Wanted = Native.Readable };
        // On regarde régulièrement si le terminal a été fermé : fermer la console pendant une lecture ne réveillerait pas celle-ci.
        while (Volatile.Read(ref _disposed) == 0)
        {
            wait.Happened = 0;
            int ready = Native.poll(ref wait, 1, 200);
            if (ready == 0) continue;
            if (ready < 0)
            {
                if (Marshal.GetLastPInvokeError() == Native.Interrupted) continue;
                break;
            }
            nint read = Native.read(master, buffer, buffer.Length);
            if (read > 0)
            {
                DataReceived?.Invoke(buffer, (int)read);
                continue;
            }
            // Linux répond EIO quand plus aucun programme n'utilise la console : c'est la fin normale de cette lecture.
            if (read == 0 || Marshal.GetLastPInvokeError() is not (Native.Interrupted or Native.TryAgain)) break;
        }

        // Fermer la console met fin au programme : Linux envoie le signal de raccrochage au shell et au programme au premier plan.
        lock (_gate)
        {
            _master = -1;
            Native.close(master);
        }
    }

    private void WaitForExit()
    {
        while (Native.waitpid(_process, out _, 0) < 0 && Marshal.GetLastPInvokeError() == Native.Interrupted) { }
        // Laisse arriver les derniers messages du programme.
        Thread.Sleep(100);
        if (Volatile.Read(ref _disposed) == 0) Closed?.Invoke(null);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        long deadline = Environment.TickCount64 + WriteTimeoutMilliseconds;
        lock (_gate)
        {
            while (data.Length > 0 && _master >= 0)
            {
                nint written = Native.write(_master, ref MemoryMarshal.GetReference(data), data.Length);
                if (written > 0)
                {
                    data = data[(int)written..];
                    continue;
                }
                int error = Marshal.GetLastPInvokeError();
                if (error == Native.Interrupted) continue;
                // Une erreur veut dire que le programme vient de se terminer : l'événement Closed va prévenir.
                if (error != Native.TryAgain) return;

                // Le programme ne lit plus : mieux vaut perdre la fin d'un collage que figer la fenêtre.
                if (Environment.TickCount64 >= deadline) return;
                var wait = new Native.PollEntry { File = _master, Wanted = Native.Writable };
                Native.poll(ref wait, 1, 100);
            }
        }
    }

    public void Resize(int columns, int rows)
    {
        lock (_gate)
        {
            if (_master >= 0) SetSize(_master, columns, rows);
        }
    }

    private static void SetSize(int master, int columns, int rows)
    {
        var size = new Native.WindowSize
        {
            Rows = (ushort)Math.Clamp(rows, 1, ushort.MaxValue),
            Columns = (ushort)Math.Clamp(columns, 1, ushort.MaxValue),
        };
        Native.ioctl(master, Native.SetWindowSize, ref size);
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    private static void Check(int result)
    {
        if (result != 0) throw Error(result);
    }

    private static IOException LastError() => Error(Marshal.GetLastPInvokeError());

    private static IOException Error(int code) => new(Marshal.GetPInvokeErrorMessage(code));

    private static class Native
    {
        // Le nom exact : « libc.so » n'existe que là où les outils de développement sont installés, et n'y est pas une vraie bibliothèque.
        private const string C = "libc.so.6";

        public const int ReadWrite = 0x2;
        public const int NoControllingTerminal = 0x100;
        public const int NonBlocking = 0x800;
        public const int CloseOnExec = 0x80000;

        public const short SpawnSignalDefaults = 0x04;
        public const short SpawnSignalMask = 0x08;
        public const short SpawnNewSession = 0x80;

        public const short Readable = 0x1;
        public const short Writable = 0x4;

        public const int Interrupted = 4;
        public const int TryAgain = 11;

        public const nuint SetWindowSize = 0x5414;

        [StructLayout(LayoutKind.Sequential)]
        public struct PollEntry
        {
            public int File;
            public short Wanted;
            public short Happened;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WindowSize
        {
            public ushort Rows;
            public ushort Columns;
            public ushort PixelWidth;
            public ushort PixelHeight;
        }

        [DllImport(C, SetLastError = true)] public static extern int posix_openpt(int flags);
        [DllImport(C, SetLastError = true)] public static extern int grantpt(int file);
        [DllImport(C, SetLastError = true)] public static extern int unlockpt(int file);
        [DllImport(C)] public static extern int ptsname_r(int file, byte[] buffer, nint size);
        [DllImport(C, SetLastError = true)] public static extern int close(int file);
        [DllImport(C, SetLastError = true)] public static extern nint read(int file, byte[] buffer, nint count);
        [DllImport(C, SetLastError = true)] public static extern nint write(int file, ref byte buffer, nint count);
        [DllImport(C, SetLastError = true)] public static extern int poll(ref PollEntry entries, nuint count, int milliseconds);
        [DllImport(C, SetLastError = true)] public static extern int ioctl(int file, nuint request, ref WindowSize size);
        [DllImport(C, SetLastError = true)] public static extern int waitpid(int process, out int status, int options);

        [DllImport(C)] public static extern int sigfillset(nint set);
        [DllImport(C)] public static extern int sigemptyset(nint set);

        [DllImport(C)] public static extern int posix_spawn_file_actions_init(nint actions);
        [DllImport(C)] public static extern int posix_spawn_file_actions_destroy(nint actions);
        [DllImport(C)] public static extern int posix_spawn_file_actions_addchdir_np(nint actions, [MarshalAs(UnmanagedType.LPUTF8Str)] string directory);
        [DllImport(C)] public static extern int posix_spawn_file_actions_addopen(nint actions, int file, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);
        [DllImport(C)] public static extern int posix_spawn_file_actions_adddup2(nint actions, int file, int copy);
        [DllImport(C)] public static extern int posix_spawnattr_init(nint attributes);
        [DllImport(C)] public static extern int posix_spawnattr_destroy(nint attributes);
        [DllImport(C)] public static extern int posix_spawnattr_setflags(nint attributes, short flags);
        [DllImport(C)] public static extern int posix_spawnattr_setsigdefault(nint attributes, nint set);
        [DllImport(C)] public static extern int posix_spawnattr_setsigmask(nint attributes, nint set);

        [DllImport(C)]
        public static extern int posix_spawn(out int process, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint actions, nint attributes, nint[] arguments, nint[] environment);
    }
}
