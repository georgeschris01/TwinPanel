// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections;
using System.Runtime.Versioning;
using System.Text;
using TwinPanel.Core.Terminal;

namespace TwinPanel.Core.Tests;

/// <summary>Un test qui n'a de sens que sous Linux : ailleurs il apparaît comme ignoré, pas comme réussi.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Linux seulement";
    }
}

/// <summary>Un test d'un comportement propre à Windows (chemins en « \\ », majuscules ignorées) : ailleurs il est ignoré.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows seulement";
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows seulement";
    }
}

public class UnixPtyConnectionTests
{
    private const int Patience = 10_000;

    // Simple calcul sur une liste de textes : il tourne aussi sous Windows, l'attribut ne sert qu'à l'analyseur.
    [Fact]
    [SupportedOSPlatform("linux")]
    public void The_program_receives_our_terminal_type_and_not_the_size_of_the_old_one()
    {
        var variables = UnixPtyConnection.Variables(new Hashtable { ["TERM"] = "dumb", ["LINES"] = "12", ["COLUMNS"] = "40", ["HOME"] = "/home/moi" }).ToList();
        Assert.Contains("HOME=/home/moi", variables);
        Assert.Contains("TERM=xterm-256color", variables);
        Assert.Single(variables, v => v.StartsWith("TERM=", StringComparison.Ordinal));
        Assert.DoesNotContain(variables, v => v.StartsWith("LINES=", StringComparison.Ordinal) || v.StartsWith("COLUMNS=", StringComparison.Ordinal));
    }

    [SupportedOSPlatform("linux")]
    private sealed class Run : IDisposable
    {
        private readonly StringBuilder _text = new();
        private readonly ManualResetEventSlim _closed = new();
        private readonly UnixPtyConnection _connection;

        public Run(string script, string? directory = null, int columns = 80, int rows = 24)
        {
            _connection = new UnixPtyConnection("/bin/sh", ["-c", script], directory, columns, rows);
            _connection.DataReceived += (data, count) =>
            {
                lock (_text) _text.Append(Encoding.UTF8.GetString(data, 0, count));
            };
            _connection.Closed += _ => _closed.Set();
            _connection.Start();
        }

        public string Text
        {
            get
            {
                lock (_text) return _text.ToString();
            }
        }

        public void Type(string text) => _connection.Write(Encoding.UTF8.GetBytes(text));

        public void Resize(int columns, int rows) => _connection.Resize(columns, rows);

        public bool WaitFor(string text) => SpinWait.SpinUntil(() => Text.Contains(text, StringComparison.Ordinal), Patience);

        public bool WaitForEnd() => _closed.Wait(Patience);

        public void Dispose() => _connection.Dispose();
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void The_program_sees_a_real_terminal_of_the_right_size()
    {
        using var run = new Run("test -t 0 && test -t 1 && echo console; stty size; echo \"$TERM\"", columns: 91, rows: 33);
        Assert.True(run.WaitFor("console"), run.Text);
        Assert.True(run.WaitFor("33 91"), run.Text);
        Assert.True(run.WaitFor("xterm-256color"), run.Text);
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void The_end_of_the_program_is_announced()
    {
        using var run = new Run("exit 3");
        Assert.True(run.WaitForEnd());
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void It_starts_in_the_chosen_folder()
    {
        var folder = Directory.CreateTempSubdirectory("twinpanel-été ").FullName;
        try
        {
            using var run = new Run("pwd -P; echo fini", folder);
            Assert.True(run.WaitFor("fini"), run.Text);
            Assert.Contains(new DirectoryInfo(folder).Name, run.Text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(folder); }
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void Typed_text_with_accents_comes_back()
    {
        using var run = new Run("stty -echo; read ligne; echo \"recu:$ligne\"");
        run.Type("été\n");
        Assert.True(run.WaitFor("recu:été"), run.Text);
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void Ctrl_c_stops_the_program_because_the_console_controls_it()
    {
        using var run = new Run("trap 'echo interrompu; exit' INT; echo pret; while :; do sleep 0.1; done");
        Assert.True(run.WaitFor("pret"), run.Text);
        run.Type("\u0003");
        Assert.True(run.WaitFor("interrompu"), run.Text);
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void The_program_learns_the_new_size()
    {
        using var run = new Run("trap 'stty size; exit' WINCH; echo pret; while :; do sleep 0.1; done");
        Assert.True(run.WaitFor("pret"), run.Text);
        run.Resize(100, 40);
        Assert.True(run.WaitFor("40 100"), run.Text);
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void Closing_the_terminal_ends_the_program()
    {
        var marker = Path.Combine(Path.GetTempPath(), "twinpanel-" + Guid.NewGuid().ToString("N"));
        var run = new Run($"trap 'touch {marker}; exit' HUP; echo pret; while :; do sleep 0.1; done");
        Assert.True(run.WaitFor("pret"), run.Text);
        run.Dispose();
        try
        {
            Assert.True(SpinWait.SpinUntil(() => File.Exists(marker), Patience));
        }
        finally { File.Delete(marker); }
    }

    [LinuxFact]
    [SupportedOSPlatform("linux")]
    public void A_missing_program_is_a_clear_error_not_an_empty_terminal()
    {
        using var connection = new UnixPtyConnection("/inexistant/programme", [], null, 80, 24);
        Assert.Throws<IOException>(connection.Start);
    }
}
