// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;
using TwinPanel.Core.Rights;

namespace TwinPanel.Core.Remote;

/// <summary>Clés et certificats acceptés « une fois » pendant une session (ne sont pas enregistrés).</summary>
public sealed class SessionTrust
{
    private readonly ConcurrentDictionary<string, byte> _accepted = new();
    public bool Contains(string key) => _accepted.ContainsKey(key);
    public void Add(string key) => _accepted[key] = 0;
}

/// <summary>Tout ce qu'il faut pour ouvrir une connexion (et en rouvrir d'autres pour les transferts).</summary>
public sealed class ConnectionContext
{
    public required ConnectionProfile Profile { get; init; }
    public required ResolvedCredentials Credentials { get; init; }
    public required IConnectionPrompts Prompts { get; init; }
    public KnownHosts? KnownHosts { get; init; }
    public SessionTrust Trust { get; init; } = new();
    /// <summary>Serveur de rebond (SSH), avec ses propres identifiants.</summary>
    public ConnectionContext? Jump { get; init; }

    internal volatile bool ServerKeyRejected;
}

/// <summary>Préparation commune SFTP / SCP : méthodes d'authentification, proxy, rebond, vérification de la clé du serveur.</summary>
internal static class SshConnector
{
    /// <summary>Tunnel par un serveur de rebond : une connexion SSH au rebond + une redirection de port locale.</summary>
    internal sealed class Tunnel(SshClient jump, ForwardedPortLocal port, Prepared through) : IDisposable
    {
        public SshClient Client { get; } = jump;
        public uint LocalPort => port.BoundPort;
        public void Dispose()
        {
            try { if (port.IsStarted) port.Stop(); } catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException) { }
            port.Dispose();
            Client.Dispose();
            // Les rebonds d'avant, et les clés de celui-ci : ils vivent aussi longtemps que le tunnel.
            through.Dispose();
        }
    }

    internal sealed class Prepared(ConnectionInfo info, Tunnel? tunnel, List<IDisposable> keys) : IDisposable
    {
        public ConnectionInfo Info { get; } = info;
        public Tunnel? Tunnel { get; } = tunnel;
        public void Dispose()
        {
            foreach (var k in keys) k.Dispose();
            Tunnel?.Dispose();
        }
    }

    public static async Task<Prepared> PrepareAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var p = ctx.Profile;
        Tunnel? tunnel = null;
        string host = p.Host;
        int port = p.Port;
        if (ctx.Jump is { } jumpCtx)
        {
            tunnel = await OpenTunnelAsync(jumpCtx, p, ct).ConfigureAwait(false);
            host = "127.0.0.1";
            port = (int)tunnel.LocalPort;
        }

        var keys = new List<IDisposable>();
        var methods = new List<AuthenticationMethod>();
        var c = ctx.Credentials;
        if (p.Auth is AuthMode.Key or AuthMode.KeyAndPassword && c.KeyText is not null)
        {
            PrivateKeyFile file;
            try { file = SshKeys.Open(c.KeyText, c.KeyPassphrase); }
            catch (KeyPassphraseException ex)
            {
                tunnel?.Dispose();
                throw new RemoteException(L.F("Clé « {0} » : {1}", c.KeyName, ex.Message), ex);
            }
            keys.Add(file);
            methods.Add(new PrivateKeyAuthenticationMethod(c.UserName, file));
        }
        if (p.Auth is AuthMode.Password or AuthMode.KeyAndPassword)
        {
            if (c.Password is not null) methods.Add(new PasswordAuthenticationMethod(c.UserName, c.Password));
            methods.Add(KeyboardInteractive(ctx));
        }
        if (methods.Count == 0) methods.Add(KeyboardInteractive(ctx));

        var (proxyType, proxyHost, proxyPort) = tunnel is not null || p.Proxy == ProxyKind.None
            ? (ProxyTypes.None, string.Empty, 0)
            : (p.Proxy switch { ProxyKind.Http => ProxyTypes.Http, ProxyKind.Socks4 => ProxyTypes.Socks4, _ => ProxyTypes.Socks5 }, p.ProxyHost, p.ProxyPort);
        var info = proxyType == ProxyTypes.None
            ? new ConnectionInfo(host, port, c.UserName, [.. methods])
            : new ConnectionInfo(host, port, c.UserName, proxyType, proxyHost, proxyPort, p.ProxyUser, c.ProxyPassword ?? string.Empty, [.. methods]);
        info.Timeout = TimeSpan.FromSeconds(p.TimeoutSeconds);
        info.Encoding = Encoding.UTF8;
        return new Prepared(info, tunnel, keys);
    }

    /// <summary>Joint un serveur de rebond (lui-même par son rebond s'il en a un), puis ouvre le passage vers la cible.</summary>
    private static async Task<Tunnel> OpenTunnelAsync(ConnectionContext jumpCtx, ConnectionProfile target, CancellationToken ct)
    {
        var through = await PrepareAsync(jumpCtx, ct).ConfigureAwait(false);
        var client = new SshClient(through.Info);
        ForwardedPortLocal? forward = null;
        try
        {
            await ConnectAsync(client, jumpCtx, ct).ConfigureAwait(false);
            forward = new ForwardedPortLocal("127.0.0.1", 0, target.Host, (uint)target.Port);
            client.AddForwardedPort(forward);
            forward.Start();
            return new Tunnel(client, forward, through);
        }
        catch
        {
            forward?.Dispose();
            client.Dispose();
            through.Dispose();
            throw;
        }
    }

    private static KeyboardInteractiveAuthenticationMethod KeyboardInteractive(ConnectionContext ctx)
    {
        var c = ctx.Credentials;
        var kbd = new KeyboardInteractiveAuthenticationMethod(c.UserName);
        bool passwordUsed = false;
        kbd.AuthenticationPrompt += (_, e) =>
        {
            var prompts = e.Prompts.ToList();
            if (prompts.Count == 0) return;
            // Cas courant : le serveur demande juste le mot de passe sous cette forme.
            if (!passwordUsed && c.Password is not null && prompts.Count == 1 && !prompts[0].IsEchoed
                && (prompts[0].Request.Contains("assword", StringComparison.OrdinalIgnoreCase) || prompts[0].Request.Contains("mot de passe", StringComparison.OrdinalIgnoreCase)))
            {
                prompts[0].Response = c.Password;
                passwordUsed = true;
                return;
            }
            var answers = ctx.Prompts.AnswerInteractiveAsync(
                    $"{c.UserName}@{ctx.Profile.Host}",
                    e.Instruction ?? string.Empty,
                    prompts.Select(q => new InteractivePrompt(q.Request, q.IsEchoed)).ToList())
                .GetAwaiter().GetResult();
            for (int i = 0; i < prompts.Count; i++) prompts[i].Response = answers is not null && i < answers.Count ? answers[i] : string.Empty;
        };
        return kbd;
    }

    /// <summary>Connexion avec contrôle de la clé du serveur (connue, nouvelle ou CHANGÉE).</summary>
    public static async Task ConnectAsync(BaseClient client, ConnectionContext ctx, CancellationToken ct)
    {
        var p = ctx.Profile;
        client.HostKeyReceived += (_, e) => e.CanTrust = CheckServerKey(ctx, e.HostKey);
        if (p.KeepAliveSeconds > 0) client.KeepAliveInterval = TimeSpan.FromSeconds(p.KeepAliveSeconds);
        try
        {
            await Task.Run(() => client.ConnectAsync(ct), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ctx.ServerKeyRejected)
        {
            throw new RemoteException(L.T("Connexion annulée : la clé du serveur n'a pas été acceptée."), ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && RemoteErrors.IsTranslatable(ex))
        {
            throw RemoteErrors.Translate(ex, L.F("Connexion à {0}", p.Host));
        }
    }

    private static bool CheckServerKey(ConnectionContext ctx, byte[] blob)
    {
        var p = ctx.Profile;
        var type = blob.Length > 4 ? ReadFirstString(blob) : L.T("inconnu");
        var fp = KnownHosts.Sha256Fingerprint(blob);
        var trustKey = $"ssh|{p.Host}|{p.Port}|{type}|{fp}";
        if (ctx.Trust.Contains(trustKey)) return true;
        var status = ctx.KnownHosts?.Check(p.Host, p.Port, type, blob) ?? HostKeyStatus.Unknown;
        if (status == HostKeyStatus.Trusted) { ctx.Trust.Add(trustKey); return true; }

        var decision = ctx.Prompts.ConfirmHostKeyAsync(new HostKeyPrompt(p.Host, p.Port, type, fp, status)).GetAwaiter().GetResult();
        if (decision == HostKeyDecision.Reject) { ctx.ServerKeyRejected = true; return false; }
        if (decision == HostKeyDecision.AcceptAndSave) ctx.KnownHosts?.Trust(p.Host, p.Port, type, blob);
        ctx.Trust.Add(trustKey);
        return true;
    }

    internal static string ReadFirstString(byte[] blob)
    {
        int len = BinaryPrimitives.ReadInt32BigEndian(blob);
        return len > 0 && len <= blob.Length - 4 ? Encoding.ASCII.GetString(blob, 4, len) : L.T("inconnu");
    }
}

/// <summary>Base commune : une seule opération à la fois sur la connexion, erreurs traduites.</summary>
public abstract class RemoteFileSystemBase : IRemoteFileSystem
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public abstract RemoteProtocol Protocol { get; }
    public string HomeDirectory { get; protected set; } = "/";
    public abstract bool IsConnected { get; }
    public virtual bool SupportsPermissions => false;

    protected async Task<T> RunAsync<T>(string? what, Func<T> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(work, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not RemoteException && RemoteErrors.IsTranslatable(ex))
        {
            throw RemoteErrors.Translate(ex, what);
        }
        finally { _gate.Release(); }
    }

    protected async Task<T> RunAsync<T>(string? what, Func<Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await work().ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not RemoteException && RemoteErrors.IsTranslatable(ex))
        {
            throw RemoteErrors.Translate(ex, what);
        }
        finally { _gate.Release(); }
    }

    protected Task RunAsync(string? what, Action work, CancellationToken ct) => RunAsync<bool>(what, () => { work(); return true; }, ct);
    protected Task RunAsync(string? what, Func<Task> work, CancellationToken ct) => RunAsync<bool>(what, async () => { await work().ConfigureAwait(false); return true; }, ct);

    public abstract Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct);
    public abstract Task<RemoteItem?> StatAsync(string path, CancellationToken ct);
    public abstract Task DownloadAsync(string path, Stream target, CancellationToken ct);
    public abstract Task UploadAsync(Stream source, string path, CancellationToken ct);
    public virtual bool CanResumeDownload => false;
    public virtual bool CanResumeUpload => false;
    public virtual Task DownloadFromAsync(string path, Stream target, long offset, CancellationToken ct) =>
        throw new RemoteException(L.T("Ce protocole ne permet pas de reprendre un téléchargement."));
    public virtual Task AppendAsync(Stream source, string path, long offset, CancellationToken ct) =>
        throw new RemoteException(L.T("Ce protocole ne permet pas de reprendre un envoi."));
    public abstract Task CreateDirectoryAsync(string path, CancellationToken ct);
    public abstract Task DeleteFileAsync(string path, CancellationToken ct);
    public abstract Task DeleteDirectoryAsync(string path, CancellationToken ct);
    public abstract Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct);
    public abstract Task SetModifiedAsync(string path, DateTime modified, CancellationToken ct);
    public virtual Task SetPermissionsAsync(string path, int mode, CancellationToken ct) =>
        throw new RemoteException(L.T("Ce protocole ne permet pas de changer les droits."));

    // ---- Propriétés / droits (IRightsTarget) ----

    public virtual bool SupportsOwnership => false;

    /// <summary>Par défaut : la ligne de l'élément dans la liste de son dossier (droits et propriétaire si le serveur les donne).</summary>
    public virtual async Task<RightsDetails?> GetDetailsAsync(string path, CancellationToken ct)
    {
        var norm = RemotePath.Normalize(path);
        if (RemotePath.Parent(norm) is { } parent)
        {
            try
            {
                var name = RemotePath.FileName(norm);
                if ((await ListAsync(parent, ct).ConfigureAwait(false)).FirstOrDefault(i => i.Name == name) is { } item)
                    return new RightsDetails(norm, item.IsDirectory, item.IsLink, item.Size, item.Modified)
                    {
                        Mode = UnixMode.Of(item),
                        Owner = item.Owner.Length > 0 ? item.Owner : null,
                        Group = item.Group.Length > 0 ? item.Group : null,
                    };
            }
            catch (RemoteException) { /* dossier parent illisible : on se contente de StatAsync */ }
        }
        return await StatAsync(norm, ct).ConfigureAwait(false) is { } s
            ? new RightsDetails(norm, s.IsDirectory, s.IsLink, s.Size, s.Modified) { Mode = UnixMode.Of(s) }
            : null;
    }

    public virtual Task SetModeAsync(string path, int mode, CancellationToken ct) => SetPermissionsAsync(path, UnixMode.ToDigits(mode), ct);

    public virtual Task SetOwnerAsync(string path, UnixAccount? user, UnixAccount? group, CancellationToken ct) =>
        throw new RemoteException(L.T("Ce protocole ne permet pas de changer le propriétaire ou le groupe."));

    public virtual Task<UnixAccounts?> GetAccountsAsync(CancellationToken ct) => Task.FromResult<UnixAccounts?>(null);

    public abstract ValueTask DisposeAsync();

    protected static string NameFor(string path) => RemotePath.FileName(path) is { Length: > 0 } n ? n : path;
}

/// <summary>SFTP (SSH File Transfer Protocol) avec SSH.NET.</summary>
public sealed class SftpFileSystem : RemoteFileSystemBase, IServerSideMove, IServerFolderSizes
{
    private readonly SftpClient _client;
    private readonly SshConnector.Prepared _prepared;
    private readonly ConnectionContext _ctx;
    private SshClient? _shell;
    private bool _shellRefused;

    private SftpFileSystem(SftpClient client, SshConnector.Prepared prepared, ConnectionContext ctx)
    {
        _client = client;
        _prepared = prepared;
        _ctx = ctx;
    }

    private bool? _hashCommand;

    /// <summary>
    /// Déplacement que le renommage SFTP refuse (le plus souvent : destination sur un autre disque du serveur, où
    /// SFTP répond seulement « Failure ») : commande « mv » exécutée par le serveur lui-même, sans passer par le PC.
    /// Serveur sans accès aux commandes (SFTP seul) : NotAvailable, l'appelant fera une copie puis une suppression.
    /// </summary>
    public Task<ServerMoveResult> TryMoveOnServerAsync(string from, string to, CancellationToken ct) =>
        // -T : la cible n'est jamais prise pour un dossier dans lequel déplacer ; chemins entre apostrophes.
        RunServerCommandAsync($"mv -T -- {ScpFileSystem.Quote(from)} {ScpFileSystem.Quote(to)}", L.T("le déplacement"), null, ct);

    /// <summary>Copie faite par le serveur (« cp -a » : dates et droits gardés), la cible n'existant pas encore.</summary>
    public Task<ServerMoveResult> TryCopyOnServerAsync(string from, string to, CancellationToken ct) =>
        RunServerCommandAsync($"cp -a -- {ScpFileSystem.Quote(from)} {ScpFileSystem.Quote(to)}", L.T("la copie"), to, ct);

    private readonly SemaphoreSlim _shellOpen = new(1, 1);

    /// <summary>Connexion pour les commandes, ouverte une seule fois même si plusieurs mesures la demandent en même temps.</summary>
    private async Task<SshClient> ShellAsync(CancellationToken ct)
    {
        if (_shell is not null) return _shell;
        await _shellOpen.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_shell is not null) return _shell;
            var shell = new SshClient(_prepared.Info);
            try { await SshConnector.ConnectAsync(shell, _ctx, ct).ConfigureAwait(false); }
            catch { shell.Dispose(); throw; }
            return _shell = shell;
        }
        finally { _shellOpen.Release(); }
    }

    /// <summary>« du » lit seulement les tailles. Pas de commandes, ou « du » sans -b : pas de mesure, la copie n'en a pas besoin.</summary>
    public async Task<long?> MeasureOnServerAsync(string path, CancellationToken ct)
    {
        if (_shellRefused) return null;
        try
        {
            using var cmd = (await ShellAsync(ct).ConfigureAwait(false)).CreateCommand($"du -sb -- {ScpFileSystem.Quote(path)}");
            await cmd.ExecuteAsync(ct).ConfigureAwait(false);
            return cmd.ExitStatus == 0 ? RemoteTransfer.ParseDiskUsage(cmd.Result) : null;
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException) { return null; }
    }

    /// <summary>Taille du dossier et de ses sous-dossiers, mesurée par le serveur (« du », lecture seule).</summary>
    public async Task<IReadOnlyDictionary<string, long>?> MeasureTreeOnServerAsync(string path, int depth, CancellationToken ct)
    {
        if (_shellRefused) return null;
        try
        {
            using var cmd = (await ShellAsync(ct).ConfigureAwait(false)).CreateCommand(ServerFolderSizes.Command(ScpFileSystem.Quote(path), depth));
            await cmd.ExecuteAsync(ct).ConfigureAwait(false);
            // Code 1 : une partie était illisible (lost+found…) ; les tailles lues restent bonnes.
            return ServerFolderSizes.Parse(cmd.Result, path);
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException) { return null; }
    }

    public async Task<ServerHashResult> TryHashOnServerAsync(string path, CancellationToken ct)
    {
        if (_shellRefused) return ServerHashResult.NotAvailable;
        try
        {
            var shell = await ShellAsync(ct).ConfigureAwait(false);
            if (_hashCommand is null)
            {
                using var probe = shell.CreateCommand(ServerHashResult.ProbeCommand);
                await probe.ExecuteAsync(ct).ConfigureAwait(false);
                _hashCommand = ServerHashResult.IsProbeOk(probe.ExitStatus, probe.Result);
            }
            if (_hashCommand == false) return ServerHashResult.NotAvailable;
            using var cmd = shell.CreateCommand($"sha256sum -- {ScpFileSystem.Quote(path)}");
            await cmd.ExecuteAsync(ct).ConfigureAwait(false);
            return ServerHashResult.From(cmd.ExitStatus, cmd.Result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException)
        {
            if (ConnectionFailure.IsLost(ex, null) || _shell is { IsConnected: false }) throw new RemoteException(L.T("La connexion a été coupée pendant la comparaison des fichiers."), ex);
            _shellRefused = true; // pas de commandes sur ce serveur (SFTP seul, chroot…)
            return ServerHashResult.NotAvailable;
        }
    }

    /// <param name="partialTarget">Cible créée par la commande : retirée si l'utilisateur annule en cours (elle n'existait pas avant).</param>
    private async Task<ServerMoveResult> RunServerCommandAsync(string command, string what, string? partialTarget, CancellationToken ct)
    {
        if (_shellRefused) return ServerMoveResult.NotAvailable;
        try
        {
            var shell = await ShellAsync(ct).ConfigureAwait(false);
            using var cmd = shell.CreateCommand(command);
            try { await cmd.ExecuteAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (partialTarget is not null)
            {
                try
                {
                    using var clean = shell.CreateCommand($"rm -rf -- {ScpFileSystem.Quote(partialTarget)}");
                    await clean.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is SshException or InvalidOperationException or ObjectDisposedException) { }
                throw;
            }
            if (cmd.ExitStatus == 0) return ServerMoveResult.Done;
            var err = (cmd.Error ?? string.Empty).Trim();
            return new ServerMoveResult(false, err.Length > 0 ? err : L.F("le serveur a refusé {0} (code {1}).", what, cmd.ExitStatus));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException)
        {
            // Connexion perdue : surtout pas « serveur sans commandes », ce serait recommencer par le PC ce que le serveur fait encore.
            if (ConnectionFailure.IsLost(ex, null) || _shell is { IsConnected: false }) throw ConnectionFailure.DuringCommand(what, ex);
            // Pas de commandes sur ce serveur (SFTP seul, chroot…) : on ne réessaie plus pour cette connexion.
            _shellRefused = true;
            return ServerMoveResult.NotAvailable;
        }
    }

    public static async Task<SftpFileSystem> ConnectAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var prepared = await SshConnector.PrepareAsync(ctx, ct).ConfigureAwait(false);
        var client = new SftpClient(prepared.Info) { OperationTimeout = TimeSpan.FromSeconds(Math.Max(ctx.Profile.TimeoutSeconds, 30)) };
        try
        {
            await SshConnector.ConnectAsync(client, ctx, ct).ConfigureAwait(false);
            return new SftpFileSystem(client, prepared, ctx) { HomeDirectory = RemotePath.Normalize(client.WorkingDirectory) };
        }
        catch
        {
            client.Dispose();
            prepared.Dispose();
            throw;
        }
    }

    public override RemoteProtocol Protocol => RemoteProtocol.Sftp;
    public override bool IsConnected => _client.IsConnected;
    public override bool SupportsPermissions => true;

    public override Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct) =>
        RunAsync<IReadOnlyList<RemoteItem>>(L.F("Lecture de {0}", path), () =>
        {
            var result = new List<RemoteItem>();
            var accounts = LoadAccounts(); // noms des propriétaires / groupes (lus une seule fois par connexion)
            foreach (var f in _client.ListDirectory(path))
            {
                ct.ThrowIfCancellationRequested();
                if (!RemotePath.IsSafeName(f.Name)) continue;
                bool isDir = f.IsDirectory;
                long size = f.Length;
                var modified = f.LastWriteTime;
                if (f.IsSymbolicLink)
                {
                    // Un lien : on regarde ce qu'il y a au bout (dossier ou fichier). Lien cassé : affiché comme fichier vide.
                    try
                    {
                        var target = _client.GetAttributes(f.FullName);
                        isDir = target.IsDirectory;
                        size = target.Size;
                        modified = target.LastWriteTime;
                    }
                    catch (SshException) { size = 0; }
                }
                var a = f.Attributes;
                result.Add(new RemoteItem(f.Name, RemotePath.Combine(path, f.Name), isDir, isDir ? 0 : size, modified, f.IsSymbolicLink, Permissions(f), ModeOf(a),
                    accounts?.UserName(a.UserId) ?? string.Empty, accounts?.GroupName(a.GroupId) ?? string.Empty, a.UserId, a.GroupId));
            }
            return result;
        }, ct);

    private static string Permissions(Renci.SshNet.Sftp.ISftpFile f)
    {
        var sb = new StringBuilder(9);
        sb.Append(f.OwnerCanRead ? 'r' : '-').Append(f.OwnerCanWrite ? 'w' : '-').Append(f.OwnerCanExecute ? 'x' : '-');
        sb.Append(f.GroupCanRead ? 'r' : '-').Append(f.GroupCanWrite ? 'w' : '-').Append(f.GroupCanExecute ? 'x' : '-');
        sb.Append(f.OthersCanRead ? 'r' : '-').Append(f.OthersCanWrite ? 'w' : '-').Append(f.OthersCanExecute ? 'x' : '-');
        return sb.ToString();
    }

    public override Task<RemoteItem?> StatAsync(string path, CancellationToken ct) =>
        RunAsync<RemoteItem?>(null, () =>
        {
            if (!_client.Exists(path)) return null;
            var a = _client.GetAttributes(path);
            return new RemoteItem(NameFor(path), RemotePath.Normalize(path), a.IsDirectory, a.IsDirectory ? 0 : a.Size, a.LastWriteTime, a.IsSymbolicLink, Mode: ModeOf(a));
        }, ct);

    /// <summary>Droits complets (bits spéciaux compris) à partir des attributs SFTP.</summary>
    internal static int ModeOf(Renci.SshNet.Sftp.SftpFileAttributes a)
    {
        int m = 0;
        bool[] bits =
        [
            a.OwnerCanRead, a.OwnerCanWrite, a.OwnerCanExecute,
            a.GroupCanRead, a.GroupCanWrite, a.GroupCanExecute,
            a.OthersCanRead, a.OthersCanWrite, a.OthersCanExecute,
        ];
        for (int i = 0; i < 9; i++) if (bits[i]) m |= 1 << (8 - i);
        if (a.IsUIDBitSet) m |= UnixMode.SetUid;
        if (a.IsGroupIDBitSet) m |= UnixMode.SetGid;
        if (a.IsStickyBitSet) m |= UnixMode.Sticky;
        return m;
    }

    private UnixAccounts? _accounts;
    private bool _accountsRead;

    /// <summary>Lit /etc/passwd et /etc/group par SFTP (une fois par connexion). Appelé sous le verrou de la connexion.</summary>
    private UnixAccounts? LoadAccounts()
    {
        if (_accountsRead) return _accounts;
        _accountsRead = true;
        string? Read(string p)
        {
            try { return _client.ReadAllText(p, Encoding.UTF8); }
            catch (SshException) { return null; }
        }
        var passwd = Read("/etc/passwd");
        var group = Read("/etc/group");
        _accounts = passwd is null && group is null ? null : UnixAccounts.Parse(passwd, group);
        return _accounts;
    }

    public override bool SupportsOwnership => true;

    public override Task<RightsDetails?> GetDetailsAsync(string path, CancellationToken ct) =>
        RunAsync<RightsDetails?>(L.F("Propriétés de {0}", NameFor(path)), () =>
        {
            if (!_client.Exists(path)) return null;
            var self = _client.Get(path); // l'élément lui-même (un lien reste un lien)
            var a = self.Attributes;
            bool isLink = self.IsSymbolicLink;
            if (isLink)
            {
                try { a = _client.GetAttributes(path); } // ce qu'il y a au bout du lien
                catch (SshException) { /* lien cassé */ }
            }
            var accounts = LoadAccounts();
            return new RightsDetails(RemotePath.Normalize(path), a.IsDirectory, isLink, a.IsDirectory ? 0 : a.Size, a.LastWriteTime)
            {
                Accessed = a.LastAccessTime,
                Mode = ModeOf(a),
                UserId = a.UserId,
                GroupId = a.GroupId,
                Owner = accounts?.UserName(a.UserId),
                Group = accounts?.GroupName(a.GroupId),
            };
        }, ct);

    public override Task SetModeAsync(string path, int mode, CancellationToken ct) =>
        RunAsync(L.F("Droits de {0}", NameFor(path)), () =>
        {
            try { _client.ChangePermissions(path, (short)UnixMode.ToDigits(mode)); }
            catch (SftpPermissionDeniedException ex) { throw new RemoteException(L.F("Droits de {0} : ", NameFor(path)) + UnixCommands.ExplainRefusal("denied", owner: false), ex); }
        }, ct);

    public override Task SetOwnerAsync(string path, UnixAccount? user, UnixAccount? group, CancellationToken ct) =>
        RunAsync(L.F("Propriétaire de {0}", NameFor(path)), () =>
        {
            if (user is null && group is null) return;
            var a = _client.GetAttributes(path);
            if (user is not null) a.UserId = user.Id;
            if (group is not null) a.GroupId = group.Id;
            try { _client.SetAttributes(path, a); }
            catch (SftpPermissionDeniedException ex) { throw new RemoteException(L.F("Propriétaire de {0} : ", NameFor(path)) + UnixCommands.ExplainRefusal("denied", owner: true), ex); }
        }, ct);

    public override Task<UnixAccounts?> GetAccountsAsync(CancellationToken ct) =>
        RunAsync<UnixAccounts?>(null, LoadAccounts, ct);

    public override Task DownloadAsync(string path, Stream target, CancellationToken ct) =>
        RunAsync(L.F("Téléchargement de {0}", NameFor(path)), () => _client.DownloadFile(path, target), ct);

    public override Task UploadAsync(Stream source, string path, CancellationToken ct) =>
        RunAsync(L.F("Envoi de {0}", NameFor(path)), () => _client.UploadFile(source, path, true), ct);

    public override bool CanResumeDownload => true;
    public override bool CanResumeUpload => true;

    public override Task DownloadFromAsync(string path, Stream target, long offset, CancellationToken ct) =>
        RunAsync(L.F("Téléchargement de {0}", NameFor(path)), () =>
        {
            using var remote = _client.Open(path, FileMode.Open, FileAccess.Read);
            remote.Seek(offset, SeekOrigin.Begin);
            remote.CopyTo(target, 1 << 16);
        }, ct);

    public override Task AppendAsync(Stream source, string path, long offset, CancellationToken ct) =>
        RunAsync(L.F("Envoi de {0}", NameFor(path)), () =>
        {
            using var remote = _client.Open(path, FileMode.Open, FileAccess.Write);
            remote.Seek(offset, SeekOrigin.Begin);
            source.CopyTo(remote, 1 << 16);
        }, ct);

    public override Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Création de {0}", NameFor(path)), () => _client.CreateDirectory(path), ct);

    public override Task DeleteFileAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Suppression de {0}", NameFor(path)), () => _client.DeleteFile(path), ct);

    public override Task DeleteDirectoryAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Suppression de {0}", NameFor(path)), () => _client.DeleteDirectory(path), ct);

    public override Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct) =>
        RunAsync(L.F("Renommage de {0}", NameFor(from)), () =>
        {
            if (_client.Exists(to))
            {
                if (!overwrite) throw new RemoteException(L.F("« {0} » existe déjà sur le serveur.", NameFor(to)));
                var a = _client.GetAttributes(to);
                if (a.IsDirectory) throw new RemoteException(L.F("« {0} » est un dossier : impossible de le remplacer.", NameFor(to)));
                try
                {
                    // En une seule opération : une coupure ne peut pas laisser le serveur sans le fichier.
                    _client.RenameFile(from, to, isPosix: true);
                    return;
                }
                catch (NotSupportedException) { ReplaceStepByStep(from, to); return; }
            }
            _client.RenameFile(from, to);
        }, ct);

    /// <summary>Serveur sans renommage atomique : l'ancien fichier est mis de côté, et remis en place si la suite échoue.</summary>
    private void ReplaceStepByStep(string from, string to)
    {
        var aside = to + RemoteTransfer.AsideSuffix;
        _client.RenameFile(to, aside);
        try { _client.RenameFile(from, to); }
        catch (SshException)
        {
            try { _client.RenameFile(aside, to); } catch (SshException) { /* l'ancien fichier reste à côté, sous ce nom */ }
            throw;
        }
        try { _client.DeleteFile(aside); } catch (SshException) { /* remplacement réussi : il ne reste qu'un fichier en trop */ }
    }

    public override Task SetModifiedAsync(string path, DateTime modified, CancellationToken ct) =>
        RunAsync(null, () =>
        {
            try
            {
                var a = _client.GetAttributes(path);
                a.LastWriteTime = modified;
                a.LastAccessTime = modified;
                _client.SetAttributes(path, a);
            }
            catch (SshException) { /* serveur qui refuse : la date reste celle de l'envoi */ }
        }, ct);

    public override Task SetPermissionsAsync(string path, int mode, CancellationToken ct) =>
        RunAsync(L.F("Droits de {0}", NameFor(path)), () => _client.ChangePermissions(path, (short)mode), ct);

    public override ValueTask DisposeAsync()
    {
        try { if (_client.IsConnected) _client.Disconnect(); } catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException) { }
        _client.Dispose();
        if (_shell is not null)
        {
            try { if (_shell.IsConnected) _shell.Disconnect(); } catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException) { }
            _shell.Dispose();
        }
        _prepared.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// SCP : transferts par la commande scp du serveur, et listage / dossiers / suppressions par des commandes shell
/// (find, mkdir, rm…). Les chemins sont toujours mis entre apostrophes (pas d'injection de commande).
/// Utile quand le serveur n'a pas de SFTP ; sinon SFTP est préférable.
/// </summary>
public sealed class ScpFileSystem : RemoteFileSystemBase, IServerSideMove, IServerFolderSizes
{
    private readonly SshClient _shell;
    private readonly ScpClient _scp;
    private readonly SshConnector.Prepared _prepared;

    private ScpFileSystem(SshClient shell, ScpClient scp, SshConnector.Prepared prepared)
    {
        _shell = shell;
        _scp = scp;
        _prepared = prepared;
    }

    private bool? _hashCommand;

    public Task<ServerMoveResult> TryMoveOnServerAsync(string from, string to, CancellationToken ct) =>
        ServerCommandAsync($"mv -T -- {Quote(from)} {Quote(to)}", L.T("le déplacement"), null, ct);

    /// <summary>Copie faite par le serveur (« cp -a »), sans passer par le PC ; la cible n'existe pas encore.</summary>
    public Task<ServerMoveResult> TryCopyOnServerAsync(string from, string to, CancellationToken ct) =>
        ServerCommandAsync($"cp -a -- {Quote(from)} {Quote(to)}", L.T("la copie"), to, ct);

    /// <summary>« du » lit seulement les tailles. « du » sans -b : pas de mesure, la copie n'en a pas besoin.</summary>
    public async Task<long?> MeasureOnServerAsync(string path, CancellationToken ct)
    {
        try
        {
            using var cmd = _shell.CreateCommand($"du -sb -- {Quote(path)}");
            await cmd.ExecuteAsync(ct).ConfigureAwait(false);
            return cmd.ExitStatus == 0 ? RemoteTransfer.ParseDiskUsage(cmd.Result) : null;
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException) { return null; }
    }

    /// <summary>Taille du dossier et de ses sous-dossiers, mesurée par le serveur (« du », lecture seule).</summary>
    public async Task<IReadOnlyDictionary<string, long>?> MeasureTreeOnServerAsync(string path, int depth, CancellationToken ct)
    {
        try
        {
            using var cmd = _shell.CreateCommand(ServerFolderSizes.Command(Quote(path), depth));
            await cmd.ExecuteAsync(ct).ConfigureAwait(false);
            return ServerFolderSizes.Parse(cmd.Result, path);
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException) { return null; }
    }

    public async Task<ServerHashResult> TryHashOnServerAsync(string path, CancellationToken ct)
    {
        try
        {
            if (_hashCommand is null)
            {
                using var probe = _shell.CreateCommand(ServerHashResult.ProbeCommand);
                await probe.ExecuteAsync(ct).ConfigureAwait(false);
                _hashCommand = ServerHashResult.IsProbeOk(probe.ExitStatus, probe.Result);
            }
            if (_hashCommand == false) return ServerHashResult.NotAvailable;
            using var cmd = _shell.CreateCommand($"sha256sum -- {Quote(path)}");
            await cmd.ExecuteAsync(ct).ConfigureAwait(false);
            return ServerHashResult.From(cmd.ExitStatus, cmd.Result);
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException)
        {
            throw new RemoteException(L.T("La connexion a été coupée pendant la comparaison des fichiers."), ex);
        }
    }

    private async Task<ServerMoveResult> ServerCommandAsync(string command, string what, string? partialTarget, CancellationToken ct)
    {
        using var cmd = _shell.CreateCommand(command);
        try { await cmd.ExecuteAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (partialTarget is not null)
        {
            // Annulé en cours : la cible (qui n'existait pas avant) est retirée.
            try
            {
                using var clean = _shell.CreateCommand($"rm -rf -- {Quote(partialTarget)}");
                await clean.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SshException or InvalidOperationException or ObjectDisposedException) { }
            throw;
        }
        catch (Exception ex) when (ex is SshException or System.Net.Sockets.SocketException or InvalidOperationException or ObjectDisposedException)
        {
            throw ConnectionFailure.DuringCommand(what, ex);
        }
        if (cmd.ExitStatus == 0) return ServerMoveResult.Done;
        var err = (cmd.Error ?? string.Empty).Trim();
        return new ServerMoveResult(false, err.Length > 0 ? err : L.F("le serveur a refusé {0} (code {1}).", what, cmd.ExitStatus));
    }

    public static async Task<ScpFileSystem> ConnectAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var prepared = await SshConnector.PrepareAsync(ctx, ct).ConfigureAwait(false);
        var shell = new SshClient(prepared.Info);
        // Chemins mis entre apostrophes pour le shell du serveur (exigé par SSH.NET 2026 : protection contre l'injection de commande).
        var scp = new ScpClient(prepared.Info, RemotePathTransformation.ShellQuote);
        try
        {
            await SshConnector.ConnectAsync(shell, ctx, ct).ConfigureAwait(false);
            await SshConnector.ConnectAsync(scp, ctx, ct).ConfigureAwait(false);
            var fs = new ScpFileSystem(shell, scp, prepared);
            fs.HomeDirectory = RemotePath.Normalize(fs.Exec("pwd").Trim());
            return fs;
        }
        catch
        {
            shell.Dispose();
            scp.Dispose();
            prepared.Dispose();
            throw;
        }
    }

    public override RemoteProtocol Protocol => RemoteProtocol.Scp;
    public override bool IsConnected => _shell.IsConnected && _scp.IsConnected;
    public override bool SupportsPermissions => true;

    /// <summary>Met un texte entre apostrophes pour le shell POSIX ('l'\''arbre').</summary>
    public static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private string Exec(string command)
    {
        using var cmd = _shell.CreateCommand(command);
        var output = cmd.Execute();
        if (cmd.ExitStatus != 0)
        {
            var err = (cmd.Error ?? string.Empty).Trim();
            throw new RemoteException(err.Length > 0 ? err : L.F("La commande a échoué (code {0}).", cmd.ExitStatus));
        }
        return output;
    }

    // %y type, %Y type de la cible (liens), %s taille, %T@ date (secondes), %m droits octaux, %U %G numéros, %u %g noms, %f nom
    private const string Format = "'%y\\t%Y\\t%s\\t%T@\\t%m\\t%U\\t%G\\t%u\\t%g\\t%f\\n'";

    internal static RemoteItem? ParseFindLine(string dir, string line)
    {
        // 10 champs (format actuel) ; 6 champs = ancien format sans propriétaire (gardé pour les tests).
        var f = line.Split('\t', 10);
        if (f.Length == 10) f = [f[0], f[1], f[2], f[3], f[4], f[9], f[5], f[6], f[7], f[8]];
        else if (f.Length == 6) f = [.. f, "-1", "-1", string.Empty, string.Empty];
        else return null;
        if (!RemotePath.IsSafeName(f[5])) return null;
        bool isLink = f[0] == "l";
        bool isDir = (isLink ? f[1] : f[0]) == "d";
        long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
        var seconds = double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;
        var modified = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).LocalDateTime;
        return new RemoteItem(f[5], RemotePath.Combine(dir, f[5]), isDir, isDir ? 0 : size, modified, isLink, OctalToText(f[4]),
            UnixMode.TryParseOctal(f[4], out var mode) ? mode : -1, f[8], f[9],
            int.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid) ? uid : -1,
            int.TryParse(f[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var gid) ? gid : -1);
    }

    private static string OctalToText(string octal)
    {
        if (!int.TryParse(octal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return string.Empty;
        var digits = v.ToString(CultureInfo.InvariantCulture).PadLeft(3, '0')[^3..];
        var sb = new StringBuilder(9);
        foreach (var d in digits)
        {
            int n = d - '0';
            sb.Append((n & 4) != 0 ? 'r' : '-').Append((n & 2) != 0 ? 'w' : '-').Append((n & 1) != 0 ? 'x' : '-');
        }
        return sb.ToString();
    }

    public override Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct) =>
        RunAsync<IReadOnlyList<RemoteItem>>(L.F("Lecture de {0}", path), () =>
        {
            string output;
            try { output = Exec($"LC_ALL=C find {Quote(path)} -mindepth 1 -maxdepth 1 -printf {Format}"); }
            catch (RemoteException ex) when (ex.Message.Contains("printf", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                throw new RemoteException(L.T("Ce serveur ne permet pas de lister les dossiers en SCP (commande find trop ancienne). Utilise plutôt SFTP."), ex);
            }
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => ParseFindLine(path, l)).OfType<RemoteItem>().ToList();
        }, ct);

    public override Task<RemoteItem?> StatAsync(string path, CancellationToken ct) =>
        RunAsync<RemoteItem?>(null, () =>
        {
            using var cmd = _shell.CreateCommand($"LC_ALL=C find {Quote(path)} -maxdepth 0 -printf {Format}");
            var output = cmd.Execute();
            if (cmd.ExitStatus != 0) return null;
            var item = ParseFindLine(RemotePath.Parent(path) ?? "/", output.TrimEnd('\n'));
            return item is null ? null : item with { FullPath = RemotePath.Normalize(path) };
        }, ct);

    public override Task DownloadAsync(string path, Stream target, CancellationToken ct) =>
        RunAsync(L.F("Téléchargement de {0}", NameFor(path)), () => _scp.Download(path, target), ct);

    public override Task UploadAsync(Stream source, string path, CancellationToken ct) =>
        RunAsync(L.F("Envoi de {0}", NameFor(path)), () => _scp.Upload(source, path), ct);

    public override Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Création de {0}", NameFor(path)), () => { Exec($"mkdir -- {Quote(path)}"); }, ct);

    public override Task DeleteFileAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Suppression de {0}", NameFor(path)), () => { Exec($"rm -f -- {Quote(path)}"); }, ct);

    public override Task DeleteDirectoryAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Suppression de {0}", NameFor(path)), () => { Exec($"rmdir -- {Quote(path)}"); }, ct);

    public override Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct) =>
        RunAsync(L.F("Renommage de {0}", NameFor(from)), () =>
        {
            // -T : la cible n'est jamais prise pour un dossier dans lequel déplacer.
            if (!overwrite)
            {
                using var test = _shell.CreateCommand($"test -e {Quote(to)}");
                test.Execute();
                if (test.ExitStatus == 0) throw new RemoteException(L.F("« {0} » existe déjà sur le serveur.", NameFor(to)));
            }
            Exec($"mv -f -T -- {Quote(from)} {Quote(to)}");
        }, ct);

    public override Task SetModifiedAsync(string path, DateTime modified, CancellationToken ct) =>
        RunAsync(null, () =>
        {
            var epoch = new DateTimeOffset(modified.ToUniversalTime()).ToUnixTimeSeconds();
            using var cmd = _shell.CreateCommand($"touch -m -d @{epoch.ToString(CultureInfo.InvariantCulture)} -- {Quote(path)}");
            cmd.Execute(); // pas grave si « touch » ne comprend pas @ : la date reste celle de l'envoi
        }, ct);

    public override Task SetPermissionsAsync(string path, int mode, CancellationToken ct) =>
        RunAsync(L.F("Droits de {0}", NameFor(path)), () => { Exec($"chmod {mode.ToString(CultureInfo.InvariantCulture)} -- {Quote(path)}"); }, ct);

    public override bool SupportsOwnership => true;

    public override Task<RightsDetails?> GetDetailsAsync(string path, CancellationToken ct) =>
        RunAsync<RightsDetails?>(L.F("Propriétés de {0}", NameFor(path)), () =>
        {
            using var cmd = _shell.CreateCommand(UnixCommands.DetailsCommand(path));
            var output = cmd.Execute();
            return UnixCommands.ParseDetails(path, output);
        }, ct);

    public override Task SetModeAsync(string path, int mode, CancellationToken ct) =>
        RunAsync(L.F("Droits de {0}", NameFor(path)), () =>
        {
            try { Exec(UnixCommands.ChmodCommand(path, mode)); }
            catch (RemoteException ex) { throw new RemoteException(L.F("Droits de {0} : ", NameFor(path)) + UnixCommands.ExplainRefusal(ex.Message, owner: false), ex); }
        }, ct);

    public override Task SetOwnerAsync(string path, UnixAccount? user, UnixAccount? group, CancellationToken ct) =>
        RunAsync(L.F("Propriétaire de {0}", NameFor(path)), () =>
        {
            if (UnixCommands.ChownCommand(path, user, group) is not { } command) return;
            try { Exec(command); }
            catch (RemoteException ex) { throw new RemoteException(L.F("Propriétaire de {0} : ", NameFor(path)) + UnixCommands.ExplainRefusal(ex.Message, owner: true), ex); }
        }, ct);

    private UnixAccounts? _accounts;
    private bool _accountsRead;

    public override Task<UnixAccounts?> GetAccountsAsync(CancellationToken ct) =>
        RunAsync<UnixAccounts?>(null, () =>
        {
            if (_accountsRead) return _accounts;
            _accountsRead = true;
            string? Run(string command)
            {
                try { return Exec(command); }
                catch (RemoteException) { return null; }
            }
            var passwd = Run(UnixCommands.PasswdCommand);
            var group = Run(UnixCommands.GroupCommand);
            _accounts = passwd is null && group is null ? null : UnixAccounts.Parse(passwd, group);
            return _accounts;
        }, ct);

    public override ValueTask DisposeAsync()
    {
        foreach (var c in new BaseClient[] { _scp, _shell })
        {
            try { if (c.IsConnected) c.Disconnect(); } catch (Exception ex) when (ex is SshException or ObjectDisposedException or InvalidOperationException) { }
            c.Dispose();
        }
        _prepared.Dispose();
        return ValueTask.CompletedTask;
    }
}
