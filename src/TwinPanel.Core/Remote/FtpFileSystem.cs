// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentFTP;
using FluentFTP.Proxy.AsyncProxy;

namespace TwinPanel.Core.Remote;

/// <summary>Contrôle d'un certificat TLS : accepté par Windows, épinglé, accepté pour la session, ou demandé à l'utilisateur.</summary>
internal static class CertificateCheck
{
    public static bool Validate(ConnectionContext ctx, X509Certificate? certificate, SslPolicyErrors errors)
    {
        if (certificate is null) return false;
        if (errors == SslPolicyErrors.None) return true;

        var p = ctx.Profile;
        var cert = certificate as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        var sha = Convert.ToHexString(SHA256.HashData(cert.RawData));
        var trustKey = $"tls|{p.Host}|{p.Port}|{sha}";
        if (ctx.Trust.Contains(trustKey) || ctx.KnownHosts?.IsCertificateTrusted(p.Host, p.Port, sha) == true) return true;

        var problems = new List<string>();
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch)) problems.Add(L.F("Le certificat n'est pas au nom de « {0} ».", p.Host));
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors)) problems.Add(L.T("Le certificat n'est pas reconnu par Windows (auto-signé, autorité inconnue, ou expiré)."));
        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable)) problems.Add(L.T("Le serveur n'a pas fourni de certificat."));
        if (DateTime.Now > cert.NotAfter) problems.Add(L.F("Le certificat a expiré le {0:dd/MM/yyyy}.", cert.NotAfter));
        if (DateTime.Now < cert.NotBefore) problems.Add(L.F("Le certificat n'est valable qu'à partir du {0:dd/MM/yyyy}.", cert.NotBefore));

        var decision = ctx.Prompts.ConfirmCertificateAsync(new CertificatePrompt(p.Host, p.Port, cert.Subject, cert.Issuer,
            cert.NotBefore, cert.NotAfter, FormatSha(sha), problems)).GetAwaiter().GetResult();
        if (decision == CertificateDecision.Reject) { ctx.ServerKeyRejected = true; return false; }
        if (decision == CertificateDecision.AlwaysTrust) ctx.KnownHosts?.TrustCertificate(p.Host, p.Port, sha, cert.Subject, cert.NotAfter.ToUniversalTime());
        ctx.Trust.Add(trustKey);
        return true;
    }

    /// <summary>« AB:CD:EF:… » (plus lisible à comparer).</summary>
    public static string FormatSha(string hex) =>
        string.Join(':', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));

    public static SslProtocols Protocols(TlsMinimum min) => min == TlsMinimum.Tls13 ? SslProtocols.Tls13 : SslProtocols.Tls12 | SslProtocols.Tls13;

    public static X509Certificate2? LoadClientCertificate(ConnectionContext ctx)
    {
        var path = ctx.Profile.ClientCertificatePath;
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) throw new RemoteException(L.F("Certificat client introuvable : {0}", path));
        try { return X509CertificateLoader.LoadPkcs12FromFile(path, string.IsNullOrEmpty(ctx.Credentials.CertificatePassword) ? null : ctx.Credentials.CertificatePassword); }
        catch (CryptographicException ex) { throw new RemoteException(L.T("Certificat client illisible ou mot de passe incorrect."), ex); }
    }
}

/// <summary>FTP, FTPS explicite (AUTH TLS) et FTPS implicite, avec FluentFTP.</summary>
public sealed class FtpFileSystem : RemoteFileSystemBase
{
    static FtpFileSystem() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private readonly AsyncFtpClient _client;
    private readonly RemoteProtocol _protocol;

    private FtpFileSystem(AsyncFtpClient client, RemoteProtocol protocol)
    {
        _client = client;
        _protocol = protocol;
    }

    public static async Task<FtpFileSystem> ConnectAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var p = ctx.Profile;
        var c = ctx.Credentials;
        var config = new FtpConfig
        {
            EncryptionMode = p.Protocol switch
            {
                RemoteProtocol.FtpsExplicit => FtpEncryptionMode.Explicit,
                RemoteProtocol.FtpsImplicit => FtpEncryptionMode.Implicit,
                _ => FtpEncryptionMode.None,
            },
            ValidateAnyCertificate = false,
            SslProtocols = CertificateCheck.Protocols(p.TlsMinimum),
            DataConnectionType = p.FtpDataMode switch
            {
                FtpDataMode.Passive => FtpDataConnectionType.PASV,
                FtpDataMode.Active => FtpDataConnectionType.AutoActive,
                _ => FtpDataConnectionType.AutoPassive,
            },
            ConnectTimeout = p.TimeoutSeconds * 1000,
            ReadTimeout = p.TimeoutSeconds * 1000,
            DataConnectionConnectTimeout = p.TimeoutSeconds * 1000,
            DataConnectionReadTimeout = p.TimeoutSeconds * 1000,
            RetryAttempts = 1,
        };
        if (p.KeepAliveSeconds > 0) config.Noop = true;
        if (CertificateCheck.LoadClientCertificate(ctx) is { } clientCert) config.ClientCertificates.Add(clientCert);

        var credentials = new NetworkCredential(c.UserName, c.Password ?? string.Empty);
        AsyncFtpClient client;
        if (p.Proxy == ProxyKind.None)
            client = new AsyncFtpClient(p.Host, credentials, p.Port, config);
        else
        {
            var profile = new FtpProxyProfile
            {
                ProxyHost = p.ProxyHost,
                ProxyPort = p.ProxyPort,
                ProxyCredentials = p.ProxyUser.Length > 0 ? new NetworkCredential(p.ProxyUser, c.ProxyPassword ?? string.Empty) : null,
                FtpHost = p.Host,
                FtpPort = p.Port,
                FtpCredentials = credentials,
            };
            client = p.Proxy switch
            {
                ProxyKind.Http => new AsyncFtpClientHttp11Proxy(profile),
                ProxyKind.Socks4 => new AsyncFtpClientSocks4Proxy(profile),
                _ => new AsyncFtpClientSocks5Proxy(profile),
            };
            client.Config = config;
        }
        if (p.FtpEncoding is { Length: > 0 } enc && !enc.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            try { client.Encoding = Encoding.GetEncoding(enc); }
            catch (ArgumentException) { throw new RemoteException(L.F("Encodage inconnu : {0}", enc)); }
        }
        client.ValidateCertificate += (_, e) => e.Accept = CertificateCheck.Validate(ctx, e.Certificate, e.PolicyErrors);

        try
        {
            await client.Connect(ct).ConfigureAwait(false);
            var fs = new FtpFileSystem(client, p.Protocol);
            try { fs.HomeDirectory = RemotePath.Normalize(await client.GetWorkingDirectory(ct).ConfigureAwait(false)); }
            catch (Exception ex) when (RemoteErrors.IsTranslatable(ex)) { fs.HomeDirectory = "/"; }
            return fs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            client.Dispose();
            if (ctx.ServerKeyRejected) throw new RemoteException(L.T("Connexion annulée : le certificat du serveur n'a pas été accepté."), ex);
            if (RemoteErrors.IsTranslatable(ex)) throw RemoteErrors.Translate(ex, L.F("Connexion à {0}", p.Host));
            throw;
        }
    }

    public override RemoteProtocol Protocol => _protocol;
    public override bool IsConnected => _client.IsConnected;

    public override Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct) =>
        RunAsync<IReadOnlyList<RemoteItem>>(L.F("Lecture de {0}", path), async () =>
        {
            var items = await _client.GetListing(path, FtpListOption.AllFiles, ct).ConfigureAwait(false);
            var result = new List<RemoteItem>();
            foreach (var i in items)
            {
                if (!RemotePath.IsSafeName(i.Name)) continue;
                bool isLink = i.Type == FtpObjectType.Link;
                bool isDir = i.Type == FtpObjectType.Directory
                             || (isLink && await _client.DirectoryExists(RemotePath.Combine(path, i.Name), ct).ConfigureAwait(false));
                result.Add(new RemoteItem(i.Name, RemotePath.Combine(path, i.Name), isDir, isDir ? 0 : Math.Max(0, i.Size),
                    i.Modified == DateTime.MinValue ? DateTime.Now : i.Modified, isLink, i.RawPermissions ?? string.Empty,
                    ModeOf(i), i.RawOwner ?? string.Empty, i.RawGroup ?? string.Empty));
            }
            return result;
        }, ct);

    public override Task<RemoteItem?> StatAsync(string path, CancellationToken ct) =>
        RunAsync<RemoteItem?>(null, async () =>
        {
            if (RemotePath.Normalize(path) == "/") return new RemoteItem("/", "/", true, 0, DateTime.Now);
            if (await _client.DirectoryExists(path, ct).ConfigureAwait(false))
                return new RemoteItem(NameFor(path), RemotePath.Normalize(path), true, 0, DateTime.Now);
            if (!await _client.FileExists(path, ct).ConfigureAwait(false)) return null;
            long size = await _client.GetFileSize(path, -1, ct).ConfigureAwait(false);
            DateTime modified;
            try { modified = await _client.GetModifiedTime(path, ct).ConfigureAwait(false); }
            catch (FluentFTP.Exceptions.FtpException) { modified = DateTime.Now; }
            return new RemoteItem(NameFor(path), RemotePath.Normalize(path), false, Math.Max(0, size), modified);
        }, ct);

    public override Task DownloadAsync(string path, Stream target, CancellationToken ct) =>
        RunAsync(L.F("Téléchargement de {0}", NameFor(path)), async () =>
        {
            if (!await _client.DownloadStream(target, path, 0, null, ct).ConfigureAwait(false))
                throw new RemoteException(L.F("Le serveur n'a pas envoyé « {0} ».", NameFor(path)));
        }, ct);

    public override bool CanResumeDownload => true;
    public override bool CanResumeUpload => true;

    public override Task DownloadFromAsync(string path, Stream target, long offset, CancellationToken ct) =>
        RunAsync(L.F("Téléchargement de {0}", NameFor(path)), async () =>
        {
            if (!await _client.DownloadStream(target, path, offset, null, ct).ConfigureAwait(false))
                throw new RemoteException(L.F("Le serveur n'a pas envoyé « {0} ».", NameFor(path)));
        }, ct);

    public override Task AppendAsync(Stream source, string path, long offset, CancellationToken ct) =>
        RunAsync(L.F("Envoi de {0}", NameFor(path)), async () =>
        {
            var status = await _client.UploadStream(source, path, FtpRemoteExists.Resume, false, null, ct).ConfigureAwait(false);
            if (status == FtpStatus.Failed) throw new RemoteException(L.F("Le serveur a refusé « {0} ».", NameFor(path)));
        }, ct);

    public override Task UploadAsync(Stream source, string path, CancellationToken ct) =>
        RunAsync(L.F("Envoi de {0}", NameFor(path)), async () =>
        {
            var status = await _client.UploadStream(source, path, FtpRemoteExists.Overwrite, false, null, ct).ConfigureAwait(false);
            if (status == FtpStatus.Failed) throw new RemoteException(L.F("Le serveur a refusé « {0} ».", NameFor(path)));
        }, ct);

    public override Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Création de {0}", NameFor(path)), async () =>
        {
            if (!await _client.CreateDirectory(path, false, ct).ConfigureAwait(false))
                throw new RemoteException(L.F("Impossible de créer « {0} » (il existe peut-être déjà).", NameFor(path)));
        }, ct);

    public override Task DeleteFileAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Suppression de {0}", NameFor(path)), () => _client.DeleteFile(path, ct), ct);

    public override Task DeleteDirectoryAsync(string path, CancellationToken ct) =>
        RunAsync(L.F("Suppression de {0}", NameFor(path)), () => _client.DeleteDirectory(path, ct), ct);

    public override Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct) =>
        RunAsync(L.F("Renommage de {0}", NameFor(from)), async () =>
        {
            if (await _client.FileExists(to, ct).ConfigureAwait(false))
            {
                if (!overwrite) throw new RemoteException(L.F("« {0} » existe déjà sur le serveur.", NameFor(to)));
                await _client.DeleteFile(to, ct).ConfigureAwait(false);
            }
            else if (await _client.DirectoryExists(to, ct).ConfigureAwait(false))
                throw new RemoteException(L.F("« {0} » existe déjà sur le serveur.", NameFor(to)));
            await _client.Rename(from, to, ct).ConfigureAwait(false);
        }, ct);

    public override Task SetModifiedAsync(string path, DateTime modified, CancellationToken ct) =>
        RunAsync(null, async () =>
        {
            try { await _client.SetModifiedTime(path, modified, ct).ConfigureAwait(false); }
            catch (FluentFTP.Exceptions.FtpException) { /* MFMT non pris en charge par le serveur */ }
        }, ct);

    /// <summary>Droits lus dans la liste du serveur (« rwxr-xr-x » ou chiffres) ; -1 si le serveur ne les donne pas (Windows/IIS…).</summary>
    private static int ModeOf(FtpListItem i)
    {
        if (Rights.UnixMode.TryParseSymbolic(i.RawPermissions, out var m)) return m;
        return i.Chmod > 0 ? Rights.UnixMode.FromDigits(i.Chmod) : -1;
    }

    /// <summary>SITE CHMOD : beaucoup de serveurs Unix l'acceptent, ceux sous Windows non (message du serveur affiché).</summary>
    public override bool SupportsPermissions => true;

    public override Task SetPermissionsAsync(string path, int mode, CancellationToken ct) =>
        RunAsync(L.F("Droits de {0}", NameFor(path)), () => _client.Chmod(path, mode, ct), ct);

    public override async ValueTask DisposeAsync()
    {
        try { if (_client.IsConnected) await _client.Disconnect().ConfigureAwait(false); }
        catch (Exception ex) when (RemoteErrors.IsTranslatable(ex) || ex is ObjectDisposedException) { }
        _client.Dispose();
    }
}
