// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using System.Runtime.Versioning;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Rights;

/// <summary>
/// Droits des fichiers du disque local sous Linux / macOS : chmod par .NET, propriétaire et groupe par les commandes
/// du système (find, chown, getent), les chemins étant toujours passés entre apostrophes.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class LocalUnixRights : IRightsTarget
{
    public bool SupportsPermissions => true;
    public bool SupportsOwnership => true;

    private static (int Exit, string Output, string Error) Run(string command)
    {
        var psi = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
        using var p = Process.Start(psi) ?? throw new IOException(L.T("Impossible de lancer /bin/sh."));
        var errTask = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, output, errTask.GetAwaiter().GetResult().Trim());
    }

    public Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct) => Task.Run<IReadOnlyList<RemoteItem>>(() =>
    {
        var result = new List<RemoteItem>();
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
        {
            ct.ThrowIfCancellationRequested();
            bool isLink = info.LinkTarget is not null;
            bool isDir = info is DirectoryInfo;
            int mode;
            try { mode = (int)File.GetUnixFileMode(info.FullName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { mode = -1; }
            result.Add(new RemoteItem(info.Name, info.FullName, isDir, isDir ? 0 : ((FileInfo)info).Length, info.LastWriteTime, isLink,
                mode >= 0 ? UnixMode.ToSymbolic(mode) : string.Empty, mode));
        }
        return result;
    }, ct);

    public Task<RightsDetails?> GetDetailsAsync(string path, CancellationToken ct) => Task.Run<RightsDetails?>(() =>
    {
        var (exit, output, _) = Run(UnixCommands.DetailsCommand(path));
        if (exit != 0 && output.Length == 0) return null;
        var d = UnixCommands.ParseDetails(path, output);
        if (d is null) return null;
        // ParseDetails normalise en chemin « Unix » : on garde le vrai chemin local.
        DateTime? created = null;
        try { created = File.GetCreationTime(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return d with { Path = path, Created = created };
    }, ct);

    public Task SetModeAsync(string path, int mode, CancellationToken ct) => Task.Run(() =>
    {
        try { File.SetUnixFileMode(path, (UnixFileMode)(mode & UnixMode.Mask)); }
        catch (UnauthorizedAccessException ex) { throw new IOException($"{Path.GetFileName(path)} : " + UnixCommands.ExplainRefusal("denied", owner: false), ex); }
    }, ct);

    public Task SetOwnerAsync(string path, UnixAccount? user, UnixAccount? group, CancellationToken ct) => Task.Run(() =>
    {
        if (UnixCommands.ChownCommand(path, user, group) is not { } command) return;
        var (exit, _, error) = Run(command);
        if (exit != 0) throw new IOException($"{Path.GetFileName(path)} : " + UnixCommands.ExplainRefusal(error, owner: true));
    }, ct);

    private UnixAccounts? _accounts;

    public Task<UnixAccounts?> GetAccountsAsync(CancellationToken ct) => Task.Run<UnixAccounts?>(() =>
    {
        if (_accounts is not null) return _accounts;
        var passwd = Run(UnixCommands.PasswdCommand);
        var group = Run(UnixCommands.GroupCommand);
        _accounts = UnixAccounts.Parse(passwd.Output, group.Output);
        return _accounts;
    }, ct);
}
