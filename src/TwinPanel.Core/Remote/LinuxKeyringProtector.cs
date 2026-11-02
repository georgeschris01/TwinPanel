// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;

namespace TwinPanel.Core.Remote;

/// <summary>Le coffre a été chiffré avec le trousseau de Linux, qui ne peut pas servir maintenant.</summary>
public sealed class VaultKeyringException(Exception? inner = null)
    : InvalidOperationException(L.T("Le trousseau de Linux ne répond pas, ou il ne contient plus la clé de TwinPanel. Ouvre-le, puis réessaie."), inner);

/// <summary>Résultat de l'outil du trousseau.</summary>
public readonly record struct KeyringToolResult(int ExitCode, string Output, string Errors);

/// <summary>
/// Protection liée au compte Linux : une clé tirée au hasard est gardée dans le trousseau du bureau (KDE, GNOME…),
/// et cette clé chiffre le coffre. L'accès passe par « secret-tool ». La clé circule uniquement par l'entrée et la sortie
/// de l'outil, jamais sur une ligne de commande, que tout programme du PC pourrait lire.
/// Sans trousseau, la protection est indisponible : le coffre demande alors un mot de passe maître.
/// </summary>
public sealed class LinuxKeyringProtector : IUserDataProtector
{
    private const int KeySize = 32;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] Attributes = ["application", "twinpanel", "kind", "vault-key"];

    private readonly Func<string[], string?, KeyringToolResult> _run;
    private readonly object _gate = new();
    private byte[]? _key;
    private DateTime _retryAfterUtc = DateTime.MinValue;

    /// <param name="run">Lance l'outil : arguments, texte envoyé à son entrée. Remplacé dans les tests.</param>
    public LinuxKeyringProtector(Func<string[], string?, KeyringToolResult>? run = null) => _run = run ?? RunTool;

    /// <summary>Le trousseau peut être interrogé pour de vrai : un outil installé ne suffit pas, il faut que le bureau en fournisse un.</summary>
    public bool IsAvailable => Key() is not null;

    public byte[] Protect(byte[] data) => SecretVault.AesSeal(Key() ?? throw new VaultKeyringException(), data);

    public byte[] Unprotect(byte[] data)
    {
        var key = Key() ?? throw new VaultKeyringException();
        try { return SecretVault.AesOpen(key, data); }
        catch (CryptographicException ex) { throw new VaultKeyringException(ex); }
    }

    /// <summary>Un trousseau fermé peut s'ouvrir ensuite : l'échec n'est retenu qu'un instant, pour ne pas réessayer à chaque secret.</summary>
    private byte[]? Key()
    {
        lock (_gate)
        {
            if (_key is not null || DateTime.UtcNow < _retryAfterUtc) return _key;
            var found = _run(["lookup", .. Attributes], null);
            // Une erreur veut dire « pas de trousseau », une réponse vide « pas encore de clé » : on ne crée la clé que dans ce cas.
            _key = Decode(found) ?? (found is { ExitCode: not 0, Output: "", Errors: "" } ? Create() : null);
            if (_key is null) _retryAfterUtc = DateTime.UtcNow + RetryDelay;
            return _key;
        }
    }

    private byte[]? Create()
    {
        var key = RandomNumberGenerator.GetBytes(KeySize);
        try
        {
            if (_run(["store", "--label=TwinPanel", .. Attributes], Convert.ToBase64String(key)).ExitCode != 0) return null;
            // On relit avant de s'y fier : une clé perdue rendrait le coffre illisible.
            return Decode(_run(["lookup", .. Attributes], null)) is { } stored && CryptographicOperations.FixedTimeEquals(stored, key) ? stored : null;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[]? Decode(KeyringToolResult result)
    {
        if (result.ExitCode != 0) return null;
        try
        {
            var key = Convert.FromBase64String(result.Output.Trim());
            return key.Length == KeySize ? key : null;
        }
        catch (FormatException) { return null; }
    }

    private static KeyringToolResult RunTool(string[] arguments, string? input)
    {
        var start = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return Unavailable;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (input is not null) process.StandardInput.Write(input);
            process.StandardInput.Close();
            // Le bureau peut demander d'ouvrir le trousseau : on laisse le temps de répondre, sans attendre indéfiniment.
            if (!process.WaitForExit(ToolTimeout))
            {
                process.Kill();
                return Unavailable;
            }
            return new KeyringToolResult(process.ExitCode, output.GetAwaiter().GetResult(), errors.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return Unavailable;
        }
    }

    private static KeyringToolResult Unavailable => new(-1, string.Empty, "secret-tool");
}
