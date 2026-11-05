// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace TwinPanel.Core.Terminal;

/// <summary>
/// Programme du PC relié au terminal par ConPTY, la console virtuelle de Windows (Windows 10 1809 ou plus récent).
/// Appels directs à Windows : aucune bibliothèque.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ConPtyConnection : ITerminalConnection
{
    private readonly string _commandLine;
    private readonly string? _workingDirectory;
    private readonly int _columns;
    private readonly int _rows;
    private readonly object _gate = new();

    private nint _console;
    private FileStream? _input;
    private FileStream? _output;
    private SafeFileHandle? _process;
    private int _disposed;

    public ConPtyConnection(string commandLine, string? workingDirectory, int columns, int rows)
    {
        _commandLine = commandLine;
        _workingDirectory = workingDirectory;
        _columns = columns;
        _rows = rows;
    }

    public event Action<byte[], int>? DataReceived;
    public event Action<string?>? Closed;

    public void Start()
    {
        var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        Check(Native.CreatePipe(out var inputRead, out var inputWrite, ref security, 0));
        SafeFileHandle? outputRead = null, outputWrite = null;
        try
        {
            Check(Native.CreatePipe(out outputRead, out outputWrite, ref security, 0));
            int result = Native.CreatePseudoConsole(new Coord(_columns, _rows), inputRead, outputWrite, 0, out _console);
            if (result != 0) throw new Win32Exception(result);

            _process = StartProcess();
            _input = new FileStream(inputWrite, FileAccess.Write, 1, isAsync: false);
            _output = new FileStream(outputRead, FileAccess.Read, 1, isAsync: false);
        }
        catch
        {
            inputWrite.Dispose();
            outputRead?.Dispose();
            throw;
        }
        finally
        {
            // Ces deux bouts appartiennent maintenant à la console virtuelle.
            inputRead.Dispose();
            outputWrite?.Dispose();
        }

        new Thread(ReadLoop) { IsBackground = true, Name = "Terminal : lecture" }.Start();
        new Thread(WaitForExit) { IsBackground = true, Name = "Terminal : attente" }.Start();
    }

    private SafeFileHandle StartProcess()
    {
        nint size = 0;
        Native.InitializeProcThreadAttributeList(0, 1, 0, ref size);
        nint attributes = Marshal.AllocHGlobal(size);
        try
        {
            Check(Native.InitializeProcThreadAttributeList(attributes, 1, 0, ref size));
            try
            {
                Check(Native.UpdateProcThreadAttribute(attributes, 0, Native.AttributePseudoConsole, _console, nint.Size, 0, 0));
                var info = new StartupInfoEx
                {
                    StartupInfo = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(), Flags = Native.UseStdHandles },
                    AttributeList = attributes,
                };
                Check(Native.CreateProcessW(null, _commandLine, 0, 0, false, Native.ExtendedStartupInfo | Native.UnicodeEnvironment,
                    0, _workingDirectory, ref info, out var created));
                Native.CloseHandle(created.Thread);
                return new SafeFileHandle(created.Process, ownsHandle: true);
            }
            finally { Native.DeleteProcThreadAttributeList(attributes); }
        }
        finally { Marshal.FreeHGlobal(attributes); }
    }

    private void ReadLoop()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            int read;
            while (_output is { } output && (read = output.Read(buffer, 0, buffer.Length)) > 0)
                DataReceived?.Invoke(buffer, read);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Le tuyau se ferme à la fin du programme : c'est la fin normale de cette lecture.
        }
    }

    private void WaitForExit()
    {
        if (_process is { } process)
        {
            Native.WaitForSingleObject(process, uint.MaxValue);
            // Laisse arriver les derniers messages du programme.
            Thread.Sleep(100);
        }
        if (Volatile.Read(ref _disposed) == 0) Closed?.Invoke(null);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        try
        {
            lock (_gate)
            {
                if (_input is not { } input) return;
                input.Write(data);
                input.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Le programme vient de se terminer : l'événement Closed va prévenir.
        }
    }

    public void Resize(int columns, int rows)
    {
        lock (_gate)
        {
            if (_console != 0) Native.ResizePseudoConsole(_console, new Coord(columns, rows));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        nint console;
        lock (_gate)
        {
            console = _console;
            _console = 0;
        }
        // ClosePseudoConsole peut attendre que la sortie soit lue : jamais sur le fil de l'interface.
        var input = _input;
        var output = _output;
        var process = _process;
        _input = null;
        Task.Run(() =>
        {
            if (console != 0) Native.ClosePseudoConsole(console);
            input?.Dispose();
            output?.Dispose();
            process?.Dispose();
        });
    }

    private static void Check(bool ok)
    {
        if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(int x, int y)
    {
        public readonly short X = (short)Math.Clamp(x, 1, short.MaxValue);
        public readonly short Y = (short)Math.Clamp(y, 1, short.MaxValue);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    private static class Native
    {
        public const int UseStdHandles = 0x100;
        public const uint ExtendedStartupInfo = 0x00080000;
        public const uint UnicodeEnvironment = 0x00000400;
        public const nint AttributePseudoConsole = 0x00020016;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, int size);

        [DllImport("kernel32.dll")]
        public static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);

        [DllImport("kernel32.dll")]
        public static extern int ResizePseudoConsole(nint console, Coord size);

        [DllImport("kernel32.dll")]
        public static extern void ClosePseudoConsole(nint console);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previous, nint returnSize);

        [DllImport("kernel32.dll")]
        public static extern void DeleteProcThreadAttributeList(nint list);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CreateProcessW(string? application, string commandLine, nint processAttributes, nint threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string? directory, ref StartupInfoEx info, out ProcessInformation created);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(nint handle);
    }
}
