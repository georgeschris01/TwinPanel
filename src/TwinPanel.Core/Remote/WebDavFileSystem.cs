// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Xml.Linq;

namespace TwinPanel.Core.Remote;

/// <summary>
/// WebDAV (Nextcloud, ownCloud, NAS Synology / QNAP, Apache, nginx…) avec le HttpClient de .NET.
/// Les chemins du panneau sont relatifs à l'adresse du service (<see cref="ConnectionProfile.WebDavRoot"/>).
/// </summary>
public sealed class WebDavFileSystem : RemoteFileSystemBase
{
    private static readonly XNamespace Dav = "DAV:";
    private const string PropfindBody =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop>" +
        "<d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>";

    private readonly HttpClient _http;
    private readonly Uri _origin;
    private readonly string _root;
    private readonly RemoteProtocol _protocol;
    private bool _connected = true;

    private WebDavFileSystem(HttpClient http, Uri origin, string root, RemoteProtocol protocol)
    {
        _http = http;
        _origin = origin;
        _root = root;
        _protocol = protocol;
    }

    public static async Task<WebDavFileSystem> ConnectAsync(ConnectionContext ctx, CancellationToken ct)
    {
        var p = ctx.Profile;
        var c = ctx.Credentials;
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(p.TimeoutSeconds),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = false, // une redirection pourrait emmener le mot de passe ailleurs
            UseCookies = true,
            Credentials = new NetworkCredential(c.UserName, c.Password ?? string.Empty),
            PreAuthenticate = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = CertificateCheck.Protocols(p.TlsMinimum),
                RemoteCertificateValidationCallback = (_, cert, _, errors) => CertificateCheck.Validate(ctx, cert, errors),
            },
        };
        if (CertificateCheck.LoadClientCertificate(ctx) is { } clientCert)
            handler.SslOptions.ClientCertificates = [clientCert];
        if (p.Proxy != ProxyKind.None)
        {
            var scheme = p.Proxy switch { ProxyKind.Http => "http", ProxyKind.Socks4 => "socks4", _ => "socks5" };
            var proxy = new WebProxy(new Uri($"{scheme}://{p.ProxyHost}:{p.ProxyPort}"));
            if (p.ProxyUser.Length > 0) proxy.Credentials = new NetworkCredential(p.ProxyUser, c.ProxyPassword ?? string.Empty);
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        else handler.UseProxy = false;

        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TwinPanel", "1.0"));
        var origin = new Uri($"{(p.Protocol == RemoteProtocol.WebDavHttp ? "http" : "https")}://{FormatHost(p.Host)}:{p.Port}");
        var root = RemotePath.Normalize(p.WebDavRoot.Replace("{user}", c.UserName, StringComparison.OrdinalIgnoreCase));
        var fs = new WebDavFileSystem(http, origin, root, p.Protocol);
        try
        {
            // Première requête : vérifie l'adresse, le certificat et le mot de passe.
            using var response = await fs.PropfindAsync("/", depth: 0, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "/").ConfigureAwait(false);
            return fs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            http.Dispose();
            if (ctx.ServerKeyRejected) throw new RemoteException(L.T("Connexion annulée : le certificat du serveur n'a pas été accepté."), ex);
            if (ex is RemoteException) throw;
            if (RemoteErrors.IsTranslatable(ex)) throw RemoteErrors.Translate(ex, L.F("Connexion à {0}", p.Host));
            throw;
        }
    }

    private static string FormatHost(string host) => host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;

    public override RemoteProtocol Protocol => _protocol;
    public override bool IsConnected => _connected;

    /// <summary>Adresse complète d'un chemin du panneau (chaque segment encodé).</summary>
    internal Uri UrlFor(string path, bool directory = false)
    {
        var full = RemotePath.Normalize(_root.TrimEnd('/') + "/" + RemotePath.Normalize(path).TrimStart('/'));
        var encoded = string.Join('/', full.Split('/').Select(Uri.EscapeDataString));
        if (directory && !encoded.EndsWith('/')) encoded += "/";
        return new Uri(_origin, encoded);
    }

    /// <summary>Chemin du panneau à partir d'un « href » renvoyé par le serveur (null s'il sort de l'adresse du service).</summary>
    internal string? PathFromHref(string href)
    {
        var abs = Uri.TryCreate(href, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? u.AbsolutePath : href;
        var decoded = RemotePath.Normalize(Uri.UnescapeDataString(abs));
        var root = _root.TrimEnd('/');
        if (root.Length == 0) return decoded;
        if (decoded.Equals(root, StringComparison.Ordinal)) return "/";
        return decoded.StartsWith(root + "/", StringComparison.Ordinal) ? decoded[root.Length..] : null;
    }

    private Task<HttpResponseMessage> PropfindAsync(string path, int depth, CancellationToken ct)
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), UrlFor(path, directory: depth > 0))
        {
            Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", depth.ToString(CultureInfo.InvariantCulture));
        return _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage r, string path)
    {
        if (r.IsSuccessStatusCode) return;
        var name = RemotePath.FileName(path) is { Length: > 0 } n ? $"« {n} »" : L.T("le dossier");
        string msg = (int)r.StatusCode switch
        {
            401 => L.T("Identifiant ou mot de passe refusés par le serveur WebDAV."),
            403 => L.F("Accès refusé à {0}.", name),
            404 => L.F("{0}{1} est introuvable sur le serveur.", char.ToUpper(name[0]), name[1..]),
            405 => L.F("Opération refusée par le serveur pour {0} (il existe peut-être déjà).", name),
            409 => L.F("Le dossier parent de {0} n'existe pas.", name),
            412 => L.F("{0}{1} existe déjà sur le serveur.", char.ToUpper(name[0]), name[1..]),
            423 => L.F("{0}{1} est verrouillé par une autre application.", char.ToUpper(name[0]), name[1..]),
            507 => L.T("Espace insuffisant sur le serveur."),
            >= 300 and < 400 => L.F("Le serveur redirige vers {0} : vérifie l'adresse du service (HTTPS, chemin).", r.Headers.Location),
            _ => L.F("Le serveur a répondu {0} {1}.", (int)r.StatusCode, r.ReasonPhrase),
        };
        await Task.CompletedTask;
        throw new RemoteException(msg);
    }

    private static List<RemoteItem> ParseMultiStatus(string xml, Func<string, string?> toPath)
    {
        var result = new List<RemoteItem>();
        var doc = XDocument.Parse(xml);
        foreach (var response in doc.Descendants(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value;
            if (href is null || toPath(href) is not { } path) continue;
            var prop = response.Elements(Dav + "propstat")
                .Where(ps => (ps.Element(Dav + "status")?.Value ?? string.Empty).Contains(" 200", StringComparison.Ordinal))
                .Select(ps => ps.Element(Dav + "prop")).FirstOrDefault(pr => pr is not null);
            bool isDir = prop?.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null;
            long.TryParse(prop?.Element(Dav + "getcontentlength")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
            var modified = DateTime.TryParse(prop?.Element(Dav + "getlastmodified")?.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var m) ? m.ToLocalTime() : DateTime.Now;
            var name = RemotePath.FileName(path);
            result.Add(new RemoteItem(name.Length == 0 ? "/" : name, path, isDir, isDir ? 0 : size, modified));
        }
        return result;
    }

    internal static List<RemoteItem> ParseForTests(string xml, string root)
    {
        var fs = new WebDavFileSystem(new HttpClient(), new Uri("https://localhost"), root, RemoteProtocol.WebDavHttps);
        return ParseMultiStatus(xml, fs.PathFromHref);
    }

    public override Task<IReadOnlyList<RemoteItem>> ListAsync(string path, CancellationToken ct) =>
        RunAsync<IReadOnlyList<RemoteItem>>(L.F("Lecture de {0}", path), async () =>
        {
            using var r = await PropfindAsync(path, 1, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(r, path).ConfigureAwait(false);
            var xml = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var self = RemotePath.Normalize(path);
            return ParseMultiStatus(xml, PathFromHref)
                .Where(i => i.FullPath != self && RemotePath.Parent(i.FullPath) == self && RemotePath.IsSafeName(i.Name))
                .ToList();
        }, ct);

    public override Task<RemoteItem?> StatAsync(string path, CancellationToken ct) =>
        RunAsync<RemoteItem?>(null, async () =>
        {
            using var r = await PropfindAsync(path, 0, ct).ConfigureAwait(false);
            if (r.StatusCode == HttpStatusCode.NotFound) return null;
            await EnsureSuccessAsync(r, path).ConfigureAwait(false);
            var xml = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseMultiStatus(xml, PathFromHref).FirstOrDefault();
        }, ct);

    public override Task DownloadAsync(string path, Stream target, CancellationToken ct) =>
        RunAsync(L.F("Téléchargement de {0}", NameFor(path)), async () =>
        {
            using var r = await _http.GetAsync(UrlFor(path), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(r, path).ConfigureAwait(false);
            await using var body = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await body.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
        }, ct);

    public override bool CanResumeDownload => true;

    public override Task DownloadFromAsync(string path, Stream target, long offset, CancellationToken ct) =>
        RunAsync(L.F("Téléchargement de {0}", NameFor(path)), async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UrlFor(path));
            request.Headers.Range = new RangeHeaderValue(offset, null);
            using var r = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(r, path).ConfigureAwait(false);
            // Un serveur qui ignore la demande renvoie tout depuis le début : ce ne serait pas la suite du fichier.
            if (r.StatusCode != HttpStatusCode.PartialContent) throw new RemoteException(L.T("Ce serveur ne permet pas de reprendre un téléchargement."));
            await using var body = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await body.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
        }, ct);

    public override Task UploadAsync(Stream source, string path, CancellationToken ct) =>
        RunAsync(L.F("Envoi de {0}", NameFor(path)), async () =>
        {
            using var content = new StreamContent(source, 81920);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            if (source.CanSeek) content.Headers.ContentLength = source.Length - source.Position;
            using var request = new HttpRequestMessage(HttpMethod.Put, UrlFor(path)) { Content = content };
            using var r = await _http.SendAsync(request, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(r, path).ConfigureAwait(false);
        }, ct);

    public override Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        SendAsync("MKCOL", path, directory: true, L.F("Création de {0}", NameFor(path)), ct);

    public override Task DeleteFileAsync(string path, CancellationToken ct) =>
        SendAsync("DELETE", path, directory: false, L.F("Suppression de {0}", NameFor(path)), ct);

    public override Task DeleteDirectoryAsync(string path, CancellationToken ct) =>
        SendAsync("DELETE", path, directory: true, L.F("Suppression de {0}", NameFor(path)), ct);

    private Task SendAsync(string method, string path, bool directory, string what, CancellationToken ct) =>
        RunAsync(what, async () =>
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), UrlFor(path, directory));
            using var r = await _http.SendAsync(request, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(r, path).ConfigureAwait(false);
        }, ct);

    public override Task RenameAsync(string from, string to, bool overwrite, CancellationToken ct) =>
        RunAsync(L.F("Renommage de {0}", NameFor(from)), async () =>
        {
            using var request = new HttpRequestMessage(new HttpMethod("MOVE"), UrlFor(from));
            request.Headers.Add("Destination", UrlFor(to).AbsoluteUri);
            request.Headers.Add("Overwrite", overwrite ? "T" : "F");
            using var r = await _http.SendAsync(request, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(r, to).ConfigureAwait(false);
        }, ct);

    /// <summary>Date de modification : PROPPATCH (ignoré par beaucoup de serveurs, accepté par Nextcloud / ownCloud via lastmodified).</summary>
    public override Task SetModifiedAsync(string path, DateTime modified, CancellationToken ct) =>
        RunAsync(null, async () =>
        {
            var body = "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propertyupdate xmlns:d=\"DAV:\"><d:set><d:prop>" +
                       $"<d:lastmodified>{new DateTimeOffset(modified.ToUniversalTime()).ToUnixTimeSeconds()}</d:lastmodified>" +
                       "</d:prop></d:set></d:propertyupdate>";
            using var request = new HttpRequestMessage(new HttpMethod("PROPPATCH"), UrlFor(path))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml"),
            };
            try { using var _ = await _http.SendAsync(request, ct).ConfigureAwait(false); }
            catch (HttpRequestException) { /* pas grave : la date reste celle de l'envoi */ }
        }, ct);

    public override ValueTask DisposeAsync()
    {
        _connected = false;
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
