// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Sources;

namespace TwinPanel.Core.Remote;

/// <summary>Ouvre la bonne connexion selon le protocole.</summary>
public static class RemoteConnector
{
    public static async Task<IRemoteFileSystem> OpenAsync(ConnectionContext ctx, CancellationToken ct) => ctx.Profile.Protocol switch
    {
        RemoteProtocol.Sftp => await SftpFileSystem.ConnectAsync(ctx, ct).ConfigureAwait(false),
        RemoteProtocol.Scp => await ScpFileSystem.ConnectAsync(ctx, ct).ConfigureAwait(false),
        RemoteProtocol.Ftp or RemoteProtocol.FtpsExplicit or RemoteProtocol.FtpsImplicit => await FtpFileSystem.ConnectAsync(ctx, ct).ConfigureAwait(false),
        RemoteProtocol.WebDavHttp or RemoteProtocol.WebDavHttps => await WebDavFileSystem.ConnectAsync(ctx, ct).ConfigureAwait(false),
        _ => throw new RemoteException(L.T("Protocole non pris en charge.")),
    };

    /// <summary>
    /// Prépare le contexte : identifiants (coffre ou question), rebond éventuel. Null si l'utilisateur annule.
    /// </summary>
    public static async Task<ConnectionContext?> PrepareAsync(ConnectionProfile profile, ConnectionStore? store, IConnectionPrompts prompts, SessionTrust? trust = null)
    {
        var errors = profile.Validate().Where(e => !e.StartsWith(L.T("Donne un nom"), StringComparison.Ordinal)).ToList();
        if (errors.Count > 0) throw new RemoteException(string.Join(Environment.NewLine, errors));
        trust ??= new SessionTrust();

        if (store is null && profile.JumpHostId is not null) throw new RemoteException(L.T("Le serveur de rebond de cette connexion n'existe plus."));

        // Du premier serveur à joindre vers la connexion : chacun passe par le précédent.
        ConnectionContext? jump = null;
        foreach (var hop in (store?.JumpChain(profile) ?? []).Reverse())
        {
            var hopCreds = await CredentialResolver.ResolveAsync(hop, store, prompts).ConfigureAwait(false);
            if (hopCreds is null) return null;
            jump = new ConnectionContext { Profile = hop, Credentials = hopCreds, Prompts = prompts, KnownHosts = store?.KnownHosts, Trust = trust, Jump = jump };
        }

        var creds = await CredentialResolver.ResolveAsync(profile, store, prompts).ConfigureAwait(false);
        if (creds is null) return null;
        return new ConnectionContext { Profile = profile, Credentials = creds, Prompts = prompts, KnownHosts = store?.KnownHosts, Trust = trust, Jump = jump };
    }

    /// <summary>Noms des rebonds, dans l'ordre où ils sont joints.</summary>
    private static IEnumerable<string> JumpNames(ConnectionContext ctx)
    {
        var names = new List<string>();
        for (var hop = ctx.Jump; hop is not null; hop = hop.Jump) names.Insert(0, hop.Profile.Name);
        return names;
    }

    /// <summary>« Tester la connexion » : connexion, lecture du dossier de départ, déconnexion. Retourne un compte rendu.</summary>
    public static async Task<string> TestAsync(ConnectionProfile profile, ConnectionStore? store, IConnectionPrompts prompts, CancellationToken ct)
    {
        var ctx = await PrepareAsync(profile, store, prompts).ConfigureAwait(false) ?? throw new OperationCanceledException();
        var started = DateTime.UtcNow;
        await using var fs = await OpenAsync(ctx, ct).ConfigureAwait(false);
        var start = profile.RemoteDirectory.Length > 0 ? RemotePath.Normalize(profile.RemoteDirectory) : fs.HomeDirectory;
        var items = await fs.ListAsync(start, ct).ConfigureAwait(false);
        var elapsed = DateTime.UtcNow - started;
        ctx.Credentials.CommitRemembered();
        var sb = new StringBuilder();
        sb.AppendLine(L.F("Connexion réussie à {0}:{1} ({2}) en {3:0.0} s.", profile.Host, profile.Port, RemoteProtocols.Label(profile.Protocol), elapsed.TotalSeconds));
        if (ctx.Jump is not null) sb.AppendLine(L.F("Par le serveur de rebond {0}.", string.Join(" → ", JumpNames(ctx))));
        sb.AppendLine(L.F("Dossier {0} : {1} élément(s).", start, items.Count));
        if (RemoteProtocols.IsUnencrypted(profile.Protocol)) sb.AppendLine(L.T("Attention : cette connexion n'est pas chiffrée."));
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Une connexion ouverte et affichable dans les panneaux sous le nom <see cref="Alias"/> (« sftp://chrypox/… »).
/// Une connexion sert à parcourir ; chaque copie / suppression en ouvre une autre (pas d'attente derrière un gros transfert).
/// </summary>
public sealed class RemoteSession : IAsyncDisposable
{
    private readonly ConnectionContext _ctx;
    private IRemoteFileSystem _browser;
    private readonly SemaphoreSlim _reconnect = new(1, 1);

    internal RemoteSession(string alias, ConnectionContext ctx, IRemoteFileSystem browser)
    {
        Alias = alias;
        _ctx = ctx;
        _browser = browser;
    }

    public string Alias { get; }
    public ConnectionProfile Profile => _ctx.Profile;
    public string Scheme => RemoteProtocols.Scheme(Profile.Protocol);
    public string HomeDirectory => _browser.HomeDirectory;
    public bool SupportsPermissions => _browser.SupportsPermissions;
    public bool SupportsOwnership => _browser.SupportsOwnership;
    public DateTime OpenedUtc { get; } = DateTime.UtcNow;

    public PanelRemotePath PathFor(string remotePath) => new(Scheme, Alias, RemotePath.Normalize(remotePath));

    /// <summary>Dossier d'arrivée : celui de la fiche, sinon le dossier personnel.</summary>
    public PanelRemotePath StartPath => PathFor(Profile.RemoteDirectory.Length > 0 ? Profile.RemoteDirectory : HomeDirectory);

    /// <summary>Opération sur la connexion de navigation, avec une reconnexion automatique si elle a été coupée.</summary>
    /// <summary>La connexion qui sert à parcourir est-elle ouverte ?</summary>
    public bool IsConnected => _browser.IsConnected;

    /// <summary>Ouvre un terminal sur ce serveur, avec les mêmes identifiants et le même rebond que le panneau.</summary>
    public async Task<SshTerminalConnection> OpenTerminalAsync(int columns, int rows, string? startDirectory, CancellationToken ct)
    {
        if (!RemoteProtocols.IsSsh(Profile.Protocol)) throw new RemoteException(L.T("Le terminal intégré ne s'ouvre que sur un serveur SSH (SFTP / SCP)."));
        return await SshTerminalConnection.ConnectAsync(_ctx, columns, rows, startDirectory, ct).ConfigureAwait(false);
    }

    public async Task<T> UseAsync<T>(Func<IRemoteFileSystem, Task<T>> work, CancellationToken ct)
    {
        try
        {
            if (!_browser.IsConnected) await ReconnectAsync(ct).ConfigureAwait(false);
            return await work(_browser).ConfigureAwait(false);
        }
        catch (RemoteException) when (!_browser.IsConnected && !ct.IsCancellationRequested)
        {
            await ReconnectAsync(ct).ConfigureAwait(false);
            return await work(_browser).ConfigureAwait(false);
        }
    }

    public Task UseAsync(Func<IRemoteFileSystem, Task> work, CancellationToken ct) =>
        UseAsync<bool>(async fs => { await work(fs).ConfigureAwait(false); return true; }, ct);

    /// <summary>Nouvelle connexion indépendante (mêmes identifiants, clés déjà acceptées) pour une tâche de fond.</summary>
    public Task<IRemoteFileSystem> OpenWorkerAsync(CancellationToken ct) => RemoteConnector.OpenAsync(_ctx, ct);

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _reconnect.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_browser.IsConnected) return;
            var old = _browser;
            _browser = await RemoteConnector.OpenAsync(_ctx, ct).ConfigureAwait(false);
            try { await old.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) when (RemoteErrors.IsTranslatable(ex) || ex is ObjectDisposedException) { }
        }
        finally { _reconnect.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _browser.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) when (RemoteErrors.IsTranslatable(ex) || ex is ObjectDisposedException) { }
    }
}

/// <summary>Connexions ouvertes, par nom court.</summary>
public sealed class RemoteSessionManager
{
    private readonly Dictionary<string, RemoteSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public event EventHandler? SessionsChanged;

    public IReadOnlyList<RemoteSession> Sessions { get { lock (_gate) return [.. _sessions.Values]; } }

    public RemoteSession? Find(string alias) { lock (_gate) return _sessions.GetValueOrDefault(alias); }

    public RemoteSession? FindByProfile(Guid profileId) { lock (_gate) return _sessions.Values.FirstOrDefault(s => s.Profile.Id == profileId); }

    public RemoteSession? FindForPath(string path) =>
        PanelRemotePath.TryParse(path, out var p) ? Find(p.Session) : null;

    /// <summary>Ouvre une connexion (ou rend celle déjà ouverte pour cette fiche). Null si l'utilisateur annule.</summary>
    public async Task<RemoteSession?> ConnectAsync(ConnectionProfile profile, ConnectionStore? store, IConnectionPrompts prompts, CancellationToken ct)
    {
        if (FindByProfile(profile.Id) is { } existing) return existing;
        var ctx = await RemoteConnector.PrepareAsync(profile, store, prompts).ConfigureAwait(false);
        if (ctx is null) return null;
        var fs = await RemoteConnector.OpenAsync(ctx, ct).ConfigureAwait(false);
        ctx.Credentials.CommitRemembered();
        if (store?.FindProfile(profile.Id) is not null) store.MarkUsed(profile.Id);

        RemoteSession session;
        lock (_gate)
        {
            session = new RemoteSession(MakeAlias(profile.Name.Length > 0 ? profile.Name : profile.Host), ctx, fs);
            _sessions[session.Alias] = session;
        }
        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }

    public async Task CloseAsync(RemoteSession session)
    {
        lock (_gate) _sessions.Remove(session.Alias);
        await session.DisposeAsync().ConfigureAwait(false);
        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CloseAllAsync()
    {
        foreach (var s in Sessions) await CloseAsync(s).ConfigureAwait(false);
    }

    /// <summary>Nom court utilisable dans un chemin : minuscules, sans espaces ni caractères spéciaux, unique.</summary>
    private string MakeAlias(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-') sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var alias = sb.ToString().Trim('-', '.');
        if (alias.Length == 0) alias = L.T("serveur");
        var candidate = alias;
        for (int i = 2; _sessions.ContainsKey(candidate); i++) candidate = $"{alias}-{i}";
        return candidate;
    }
}

/// <summary>Les connexions ouvertes vues comme des dossiers dans les panneaux.</summary>
public sealed class RemoteFileSource(RemoteSessionManager sessions) : IFileSource
{
    public string Id => "remote";
    public bool SupportsWatching => false;

    public bool CanHandle(string path) => PanelRemotePath.TryParse(path, out _);

    public string Normalize(string path) =>
        PanelRemotePath.TryParse(path, out var p) ? p.ToString() : throw new ArgumentException(L.F("Chemin distant invalide : {0}", path));

    public string? GetParent(string path) =>
        PanelRemotePath.TryParse(path, out var p) && RemotePath.Parent(p.Path) is { } parent ? p.WithPath(parent).ToString() : null;

    public async Task<IReadOnlyList<FileEntry>> ListAsync(string path, bool showHidden, CancellationToken ct)
    {
        if (!PanelRemotePath.TryParse(path, out var p)) throw new DirectoryNotFoundException(path);
        var session = sessions.Find(p.Session)
                      ?? throw new RemoteException(L.F("La connexion « {0} » est fermée. Rouvre-la depuis le gestionnaire (Ctrl+F).", p.Session));
        var items = await session.UseAsync(fs => fs.ListAsync(p.Path, ct), ct).ConfigureAwait(false);

        var result = new List<FileEntry>();
        if (GetParent(path) is { } parent)
            result.Add(new FileEntry("..", parent, true, 0, default, FileAttributes.Directory, IsParentLink: true));
        foreach (var i in items)
        {
            bool hidden = i.Name.StartsWith('.');
            if (hidden && !showHidden) continue;
            var attrs = i.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
            if (hidden) attrs |= FileAttributes.Hidden;
            if (i.IsLink) attrs |= FileAttributes.ReparsePoint;
            result.Add(new FileEntry(i.Name, p.WithPath(i.FullPath).ToString(), i.IsDirectory, i.Size, i.Modified, attrs)
            {
                Mode = Rights.UnixMode.Of(i),
                UserId = i.Uid,
                GroupId = i.Gid,
                Owner = i.Owner,
                Group = i.Group,
            });
        }
        return result;
    }
}
