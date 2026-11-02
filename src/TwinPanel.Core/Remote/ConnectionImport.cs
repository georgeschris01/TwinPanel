// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using System.Xml.Linq;

namespace TwinPanel.Core.Remote;

/// <summary>Une connexion trouvée dans un autre programme, prête à être ajoutée (après choix de l'utilisateur).</summary>
public sealed class ImportedConnection
{
    public required ConnectionProfile Profile { get; init; }
    /// <summary>Mot de passe trouvé en clair dans la source (FileZilla) ; jamais pour Total Commander (chiffré par lui).</summary>
    public string? Password { get; init; }
    /// <summary>Fichier de clé privée indiqué par la source.</summary>
    public string? KeyFile { get; init; }
    /// <summary>Nom d'une autre connexion importée servant de rebond (ProxyJump).</summary>
    public string? JumpHostName { get; init; }
    public string Source { get; init; } = string.Empty;
}

public enum ImportSource { SshConfig, FileZilla, TotalCommander, WinScp, Putty }

/// <summary>Lecture des connexions d'OpenSSH, FileZilla, Total Commander, WinSCP et PuTTY (lecture seule, rien n'est modifié chez eux).</summary>
public static class ConnectionImport
{
    public static string Label(ImportSource s) => s switch
    {
        ImportSource.SshConfig => "OpenSSH (~/.ssh/config)",
        ImportSource.FileZilla => "FileZilla (sitemanager.xml)",
        ImportSource.TotalCommander => "Total Commander (wcx_ftp.ini, sftpplug.ini)",
        ImportSource.WinScp => "WinSCP",
        ImportSource.Putty => "PuTTY",
        _ => s.ToString(),
    };

    /// <summary>Fichier habituel de chaque programme (null pour WinSCP / PuTTY : base de registre).</summary>
    public static IReadOnlyList<string> DefaultFiles(ImportSource s)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return s switch
        {
            ImportSource.SshConfig => [Path.Combine(home, ".ssh", "config")],
            ImportSource.FileZilla => [Path.Combine(appData, "FileZilla", "sitemanager.xml")],
            ImportSource.TotalCommander => [Path.Combine(appData, "GHISLER", "wcx_ftp.ini"), Path.Combine(appData, "GHISLER", "sftpplug.ini")],
            ImportSource.WinScp => [Path.Combine(appData, "WinSCP.ini")],
            _ => [],
        };
    }

    /// <summary>Lit les connexions d'une source : ses fichiers habituels, ou le fichier donné.</summary>
    public static IReadOnlyList<ImportedConnection> Read(ImportSource source, string? file = null)
    {
        IReadOnlyList<string> files = file is not null ? [file] : DefaultFiles(source).Where(File.Exists).ToList();
        var result = new List<ImportedConnection>();
        foreach (var f in files)
        {
            var text = File.ReadAllText(f);
            result.AddRange(source switch
            {
                ImportSource.SshConfig => ParseSshConfig(text),
                ImportSource.FileZilla => ParseFileZilla(text),
                ImportSource.TotalCommander => ParseTotalCommander(text, Path.GetFileName(f)),
                ImportSource.WinScp => ParseWinScpIni(text),
                _ => [],
            });
        }
        if (file is null && OperatingSystem.IsWindows())
        {
            if (source == ImportSource.WinScp) result.AddRange(Registry.ReadWinScp());
            if (source == ImportSource.Putty) result.AddRange(Registry.ReadPutty());
        }
        return result;
    }

    // ---- OpenSSH -----------------------------------------------------------------------------

    public static IReadOnlyList<ImportedConnection> ParseSshConfig(string text)
    {
        var result = new List<ImportedConnection>();
        List<string>? names = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (names is null) return;
            foreach (var name in names.Where(n => !n.Contains('*') && !n.Contains('?') && !n.StartsWith('!')))
            {
                var host = values.GetValueOrDefault("HostName", name);
                var p = new ConnectionProfile
                {
                    Name = name,
                    Protocol = RemoteProtocol.Sftp,
                    Host = host,
                    Port = int.TryParse(values.GetValueOrDefault("Port"), out var port) ? port : 22,
                    UserName = values.GetValueOrDefault("User", string.Empty),
                    Group = "OpenSSH",
                };
                var key = values.GetValueOrDefault("IdentityFile");
                if (key is not null) { p.Auth = AuthMode.Key; key = ExpandHome(key.Trim('"')); }
                var jump = values.GetValueOrDefault("ProxyJump");
                result.Add(new ImportedConnection
                {
                    Profile = p,
                    KeyFile = key,
                    // « ProxyJump user@hôte:port » : on ne garde que le nom (le rebond doit être une autre entrée importée).
                    JumpHostName = jump is null || jump.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : jump.Split(',')[0].Split('@').Last().Split(':')[0],
                    Source = "OpenSSH",
                });
            }
            names = null;
            values.Clear();
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int sep = line.IndexOfAny([' ', '\t', '=']);
            if (sep < 0) continue;
            var key = line[..sep];
            var value = line[(sep + 1)..].Trim().TrimStart('=').Trim();
            if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                names = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            else if (key.Equals("Match", StringComparison.OrdinalIgnoreCase)) { Flush(); }
            else if (names is not null) values.TryAdd(key, value); // première valeur gagne, comme OpenSSH
        }
        Flush();
        return result;
    }

    private static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;

    // ---- FileZilla ----------------------------------------------------------------------------

    public static IReadOnlyList<ImportedConnection> ParseFileZilla(string xml)
    {
        var doc = XDocument.Parse(xml);
        var result = new List<ImportedConnection>();
        var servers = doc.Root?.Element("Servers");
        if (servers is null) return result;
        Walk(servers, string.Empty);
        return result;

        void Walk(XElement folder, string group)
        {
            foreach (var el in folder.Elements())
            {
                if (el.Name.LocalName == "Folder")
                {
                    var name = el.Nodes().OfType<XText>().Select(t => t.Value.Trim()).FirstOrDefault(t => t.Length > 0) ?? string.Empty;
                    Walk(el, group.Length == 0 ? name : $"{group}/{name}");
                }
                else if (el.Name.LocalName == "Server") result.Add(FromFileZilla(el, group));
            }
        }
    }

    private static ImportedConnection FromFileZilla(XElement s, string group)
    {
        string V(string n) => s.Element(n)?.Value.Trim() ?? string.Empty;
        var protocol = V("Protocol") switch
        {
            "1" => RemoteProtocol.Sftp,
            "3" => RemoteProtocol.FtpsImplicit,
            "4" => RemoteProtocol.FtpsExplicit,
            "6" => RemoteProtocol.Ftp,
            "0" => RemoteProtocol.FtpsExplicit, // « TLS si disponible » : on choisit le chiffrement (modifiable).
            _ => RemoteProtocol.Ftp,
        };
        var p = new ConnectionProfile
        {
            Name = V("Name") is { Length: > 0 } n ? n : V("Host"),
            Group = group.Length > 0 ? group : "FileZilla",
            Protocol = protocol,
            Host = V("Host"),
            Port = int.TryParse(V("Port"), out var port) ? port : RemoteProtocols.DefaultPort(protocol),
            UserName = V("User"),
            RemoteDirectory = DecodeFileZillaPath(V("RemoteDir")),
            LocalDirectory = V("LocalDir"),
            Notes = V("Comments"),
        };
        string? password = null;
        var pass = s.Element("Pass");
        if (pass is not null && (string?)pass.Attribute("encoding") == "base64")
        {
            try { password = Encoding.UTF8.GetString(Convert.FromBase64String(pass.Value.Trim())); }
            catch (FormatException) { }
        }
        else if (pass is not null && pass.Attribute("encoding") is null && pass.Value.Length > 0) password = pass.Value;
        // encoding="crypt" : protégé par le mot de passe maître de FileZilla, illisible pour nous (c'est voulu).

        string? key = null;
        switch (V("Logontype"))
        {
            case "0": p.Auth = protocol == RemoteProtocol.Sftp ? AuthMode.Password : AuthMode.Anonymous; break;
            case "5": p.Auth = AuthMode.Key; key = V("Keyfile"); password = null; break;
            default: p.Auth = AuthMode.Password; break;
        }
        if (V("PasvMode") == "MODE_ACTIVE") p.FtpDataMode = FtpDataMode.Active;
        if (V("EncodingType") == "Custom" && V("CustomEncoding") is { Length: > 0 } enc) p.FtpEncoding = enc.ToLowerInvariant();
        return new ImportedConnection { Profile = p, Password = password, KeyFile = key, Source = "FileZilla" };
    }

    /// <summary>Format FileZilla des dossiers : « 1 0 4 home 8 christop » → /home/christop.</summary>
    public static string DecodeFileZillaPath(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return string.Empty;
        int pos = 0;
        string? Token()
        {
            while (pos < encoded.Length && encoded[pos] == ' ') pos++;
            int start = pos;
            while (pos < encoded.Length && encoded[pos] != ' ') pos++;
            return pos > start ? encoded[start..pos] : null;
        }
        if (Token() is null) return string.Empty;            // type de serveur
        if (!int.TryParse(Token(), out var prefixLen)) return string.Empty;
        if (prefixLen > 0) { pos++; pos += prefixLen; }       // préfixe (serveurs VMS…) ignoré
        var segments = new List<string>();
        while (Token() is { } lenText && int.TryParse(lenText, out var len))
        {
            pos++; // espace
            if (pos + len > encoded.Length) break;
            segments.Add(encoded.Substring(pos, len));
            pos += len;
        }
        return "/" + string.Join('/', segments);
    }

    // ---- Total Commander ------------------------------------------------------------------------

    /// <summary>wcx_ftp.ini (FTP intégré) ou sftpplug.ini (extension SFTP). Les mots de passe chiffrés par Total Commander ne sont pas repris.</summary>
    public static IReadOnlyList<ImportedConnection> ParseTotalCommander(string text, string fileName = "")
    {
        var sections = ParseIni(text);
        bool sftpFile = fileName.Contains("sftp", StringComparison.OrdinalIgnoreCase);
        var result = new List<ImportedConnection>();
        foreach (var (name, v) in sections)
        {
            if (name.Equals("connections", StringComparison.OrdinalIgnoreCase) || name.StartsWith("default", StringComparison.OrdinalIgnoreCase)
                || name.Equals("General", StringComparison.OrdinalIgnoreCase) || name.Equals("Settings", StringComparison.OrdinalIgnoreCase)) continue;
            var hostRaw = v.GetValueOrDefault("host") ?? v.GetValueOrDefault("server") ?? v.GetValueOrDefault("hostname");
            if (string.IsNullOrWhiteSpace(hostRaw)) continue;

            bool sftp = sftpFile || hostRaw.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase) || v.ContainsKey("privkeyfile") || v.ContainsKey("pubkeyfile");
            bool tls = hostRaw.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) || v.GetValueOrDefault("ssl") == "1" || v.GetValueOrDefault("secure") == "1";
            var protocol = sftp ? RemoteProtocol.Sftp : tls ? RemoteProtocol.FtpsExplicit : RemoteProtocol.Ftp;
            var (host, port) = SplitHostPort(hostRaw, RemoteProtocols.DefaultPort(protocol));
            var p = new ConnectionProfile
            {
                Name = name,
                Group = "Total Commander",
                Protocol = protocol,
                Host = host,
                Port = port,
                UserName = v.GetValueOrDefault("username") ?? v.GetValueOrDefault("user") ?? string.Empty,
                RemoteDirectory = v.GetValueOrDefault("directory") ?? v.GetValueOrDefault("path") ?? string.Empty,
                LocalDirectory = v.GetValueOrDefault("localdir") ?? string.Empty,
            };
            if (v.GetValueOrDefault("pasvmode") == "0") p.FtpDataMode = FtpDataMode.Active;
            if (p.UserName.Equals("anonymous", StringComparison.OrdinalIgnoreCase) && !sftp) p.Auth = AuthMode.Anonymous;
            var key = v.GetValueOrDefault("privkeyfile") ?? v.GetValueOrDefault("keyfile");
            if (!string.IsNullOrWhiteSpace(key)) p.Auth = AuthMode.Key;
            result.Add(new ImportedConnection { Profile = p, KeyFile = string.IsNullOrWhiteSpace(key) ? null : key, Source = "Total Commander" });
        }
        return result;
    }

    // ---- WinSCP -----------------------------------------------------------------------------------

    public static IReadOnlyList<ImportedConnection> ParseWinScpIni(string text)
    {
        var result = new List<ImportedConnection>();
        foreach (var (name, v) in ParseIni(text))
        {
            if (!name.StartsWith("Sessions\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (FromWinScp(Uri.UnescapeDataString(name["Sessions\\".Length..]), k => v.GetValueOrDefault(k)) is { } c) result.Add(c);
        }
        return result;
    }

    internal static ImportedConnection? FromWinScp(string fullName, Func<string, string?> get)
    {
        var host = get("HostName");
        if (string.IsNullOrWhiteSpace(host) || fullName.Equals("Default Settings", StringComparison.OrdinalIgnoreCase)) return null;
        var ftps = get("Ftps") ?? "0";
        var protocol = (get("FSProtocol") ?? "2") switch
        {
            "0" => RemoteProtocol.Scp,
            "5" => ftps == "1" ? RemoteProtocol.FtpsImplicit : ftps is "2" or "3" ? RemoteProtocol.FtpsExplicit : RemoteProtocol.Ftp,
            "6" => ftps == "0" ? RemoteProtocol.WebDavHttp : RemoteProtocol.WebDavHttps,
            _ => RemoteProtocol.Sftp,
        };
        var slash = fullName.LastIndexOf('/');
        var p = new ConnectionProfile
        {
            Name = slash >= 0 ? fullName[(slash + 1)..] : fullName,
            Group = slash >= 0 ? "WinSCP/" + fullName[..slash] : "WinSCP",
            Protocol = protocol,
            Host = host,
            Port = int.TryParse(get("PortNumber"), out var port) ? port : RemoteProtocols.DefaultPort(protocol),
            UserName = get("UserName") ?? string.Empty,
            RemoteDirectory = Unescape(get("RemoteDirectory")),
            LocalDirectory = Unescape(get("LocalDirectory")),
        };
        var key = Unescape(get("PublicKeyFile"));
        if (key.Length > 0) p.Auth = AuthMode.Key;
        if (get("FtpPasvMode") == "0") p.FtpDataMode = FtpDataMode.Active;
        return new ImportedConnection { Profile = p, KeyFile = key.Length > 0 ? key : null, Source = "WinSCP" };
    }

    private static string Unescape(string? value) => string.IsNullOrEmpty(value) ? string.Empty : Uri.UnescapeDataString(value);

    internal static ImportedConnection? FromPutty(string name, Func<string, string?> get)
    {
        var host = get("HostName");
        if (string.IsNullOrWhiteSpace(host) || name.Equals("Default Settings", StringComparison.OrdinalIgnoreCase)) return null;
        if ((get("Protocol") ?? "ssh") != "ssh") return null;
        var user = get("UserName") ?? string.Empty;
        if (host.Contains('@')) { user = host[..host.IndexOf('@')]; host = host[(host.IndexOf('@') + 1)..]; }
        var key = get("PublicKeyFile") ?? string.Empty;
        var p = new ConnectionProfile
        {
            Name = name,
            Group = "PuTTY",
            Protocol = RemoteProtocol.Sftp,
            Host = host,
            Port = int.TryParse(get("PortNumber"), out var port) && port > 0 ? port : 22,
            UserName = user,
            Auth = key.Length > 0 ? AuthMode.Key : AuthMode.Password,
        };
        return new ImportedConnection { Profile = p, KeyFile = key.Length > 0 ? key : null, Source = "PuTTY" };
    }

    // ---- Outils ---------------------------------------------------------------------------------

    /// <summary>« hôte », « hôte:port », « ftp://hôte:port/dossier », « [::1]:2222 ».</summary>
    public static (string Host, int Port) SplitHostPort(string raw, int defaultPort)
    {
        var s = raw.Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        int slash = s.IndexOf('/');
        if (slash >= 0) s = s[..slash];
        if (s.Contains('@')) s = s[(s.LastIndexOf('@') + 1)..];
        if (s.StartsWith('['))
        {
            int end = s.IndexOf(']');
            if (end > 0)
                return (s[1..end], end + 2 < s.Length && s[end + 1] == ':' && int.TryParse(s[(end + 2)..], out var p6) ? p6 : defaultPort);
        }
        int colon = s.LastIndexOf(':');
        if (colon > 0 && s.IndexOf(':') == colon && int.TryParse(s[(colon + 1)..], out var p)) return (s[..colon], p);
        return (s, defaultPort);
    }

    /// <summary>Fichier .ini simple : sections dans l'ordre, clés insensibles à la casse.</summary>
    internal static List<(string Name, Dictionary<string, string> Values)> ParseIni(string text)
    {
        var result = new List<(string, Dictionary<string, string>)>();
        Dictionary<string, string>? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line.EndsWith(']'))
            {
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result.Add((line[1..^1], current));
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq > 0 && current is not null) current[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return result;
    }

    /// <summary>Base de registre de Windows (WinSCP, PuTTY), en lecture seule.</summary>
    private static class Registry
    {
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static IEnumerable<ImportedConnection> ReadWinScp() =>
            ReadSessions(@"Software\Martin Prikryl\WinSCP 2\Sessions", (name, get) => FromWinScp(name, get));

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static IEnumerable<ImportedConnection> ReadPutty() =>
            ReadSessions(@"Software\SimonTatham\PuTTY\Sessions", (name, get) => FromPutty(name, get));

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static List<ImportedConnection> ReadSessions(string path, Func<string, Func<string, string?>, ImportedConnection?> make)
        {
            var result = new List<ImportedConnection>();
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
            if (root is null) return result;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(sub);
                if (key is null) continue;
                var name = Uri.UnescapeDataString(sub);
                if (make(name, k => key.GetValue(k)?.ToString()) is { } c) result.Add(c);
            }
            return result;
        }
    }
}
