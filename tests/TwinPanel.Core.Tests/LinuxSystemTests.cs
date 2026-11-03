// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Settings;
using TwinPanel.Core.Tools;

namespace TwinPanel.Core.Tests;

public class LinuxSystemTests
{
    [Theory]
    [InlineData("/", DriveType.Fixed, true)]
    [InlineData("/", DriveType.Ram, true)]
    [InlineData("/home", DriveType.Fixed, true)]
    [InlineData("/data", DriveType.Fixed, true)]
    [InlineData("/media/moi/CLE", DriveType.Fixed, true)]
    [InlineData("/run/media/moi/CLE", DriveType.Removable, true)]
    [InlineData("/mnt/partage", DriveType.Network, true)]
    [InlineData("/proc", DriveType.Ram, false)]
    [InlineData("/sys/fs/cgroup", DriveType.Ram, false)]
    [InlineData("/dev/shm", DriveType.Ram, false)]
    [InlineData("/run/user/1000", DriveType.Ram, false)]
    [InlineData("/run/lock", DriveType.Fixed, false)]
    [InlineData("/snap/core/1", DriveType.Fixed, false)]
    [InlineData("/boot/efi", DriveType.Fixed, false)]
    [InlineData("/tmp", DriveType.Ram, false)]
    [InlineData("/runtime", DriveType.Fixed, true)]
    public void Only_real_disks_are_shown(string mount, DriveType type, bool shown) =>
        Assert.Equal(shown, LocalDisks.IsForUser(mount, type));

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/media/moi/MA CLE", "MA CLE")]
    [InlineData("/data/", "data")]
    public void A_disk_has_a_short_name(string mount, string name) => Assert.Equal(name, LocalDisks.ShortName(mount));

    [Fact]
    public void The_most_precise_folder_wins()
    {
        string[] folders = ["/", "/home/moi", "/media/moi/CLE"];
        Assert.Equal("/home/moi", LocalDisks.Containing("/home/moi/Documents", folders));
        Assert.Equal("/", LocalDisks.Containing("/home/moi2", folders));
        Assert.Equal("/media/moi/CLE", LocalDisks.Containing("/media/moi/CLE", folders));
    }

    [Fact]
    public void A_printer_list_is_read()
    {
        Assert.Equal(["Bureau", "Salon"], LinuxPrinters.Names("Salon\nBureau\n"));
        Assert.Empty(LinuxPrinters.Names(""));
    }

    [Theory]
    [InlineData("system default destination: Bureau\n", "Bureau")]
    [InlineData("no system default destination\n", null)]
    [InlineData("", null)]
    public void The_default_printer_is_read(string text, string? name) => Assert.Equal(name, LinuxPrinters.DefaultName(text));

    [LinuxFact]
    public void The_external_terminal_of_linux_can_reach_a_server()
    {
        var settings = new TerminalSettings { Kind = TerminalKind.Custom };
        var (program, arguments) = Customization.TerminalCommand.Build(settings, new Customization.TerminalCommand.Target(null, "moi", "exemple.test", 2222));
        Assert.Equal("x-terminal-emulator", program);
        Assert.Equal(["-e", "ssh", "-p", "2222", "moi@exemple.test"], arguments);
    }

    [LinuxFact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void The_settings_folder_is_private()
    {
        var parent = Directory.CreateTempSubdirectory("twinpanel-").FullName;
        try
        {
            var folder = Path.Combine(parent, "TwinPanel");
            _ = new Remote.ConnectionStore(folder);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(folder));
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
}
