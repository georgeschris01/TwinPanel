// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Net.Sockets;
using System.Security.Authentication;
using TwinPanel.Core.Operations;

namespace TwinPanel.Core.Remote;

/// <summary>Décision sur la clé d'un serveur SSH.</summary>
public enum HostKeyDecision { Reject, AcceptOnce, AcceptAndSave }

/// <summary>Décision sur un certificat TLS que Windows ne reconnaît pas.</summary>
public enum CertificateDecision { Reject, AcceptOnce, AlwaysTrust }

public sealed record HostKeyPrompt(string Host, int Port, string KeyType, string Fingerprint, HostKeyStatus Status);

public sealed record CertificatePrompt(string Host, int Port, string Subject, string Issuer, DateTime NotBefore, DateTime NotAfter,
                                       string Sha256, IReadOnlyList<string> Problems);

/// <summary>Demande d'un secret (mot de passe, phrase de passe…). CanRemember : proposer « Enregistrer dans le coffre ».</summary>
public sealed record SecretRequest(string Title, string Message, bool CanRemember);

public sealed record SecretAnswer(string Value, bool Remember);

/// <summary>Question d'un serveur en mode « clavier interactif » (code à usage unique…).</summary>
public sealed record InteractivePrompt(string Text, bool Echo);

/// <summary>
/// Ce que la connexion doit demander à l'utilisateur. L'interface l'implémente ; les appels peuvent venir
/// d'un fil de travail (jamais du fil de l'interface, qui ne doit pas être bloqué pendant une connexion).
/// </summary>
public interface IConnectionPrompts
{
    Task<HostKeyDecision> ConfirmHostKeyAsync(HostKeyPrompt prompt);
    Task<CertificateDecision> ConfirmCertificateAsync(CertificatePrompt prompt);
    /// <summary>null si l'utilisateur annule.</summary>
    Task<SecretAnswer?> AskSecretAsync(SecretRequest request);
    /// <summary>Demande le mot de passe maître et déverrouille le coffre ; false si l'utilisateur annule.</summary>
    Task<bool> UnlockVaultAsync(SecretVault vault);
    /// <summary>Réponses aux questions du serveur, ou null pour annuler.</summary>
    Task<IReadOnlyList<string>?> AnswerInteractiveAsync(string title, string instruction, IReadOnlyList<InteractivePrompt> prompts);
}

/// <summary>Traduit les erreurs réseau / protocole en messages clairs.</summary>
public static class RemoteErrors
{
    public static bool IsTranslatable(Exception ex) =>
        ex is IOException or SocketException or TimeoutException or AuthenticationException or UnauthorizedAccessException
           or InvalidOperationException or HttpRequestException or Renci.SshNet.Common.SshException or FluentFTP.Exceptions.FtpException
           or System.Security.Cryptography.CryptographicException or ProxyFailureException;

    public static RemoteException Translate(Exception ex, string? what = null)
    {
        if (ex is RemoteException r) return r;
        var prefix = what is null ? string.Empty : what + " : ";
        string msg = ex switch
        {
            Renci.SshNet.Common.SshAuthenticationException => L.T("identifiant, mot de passe ou clé refusés par le serveur."),
            Renci.SshNet.Common.SshOperationTimeoutException or TimeoutException => L.T("le serveur ne répond pas (délai dépassé)."),
            Renci.SshNet.Common.SshConnectionException => L.T("la connexion SSH a été coupée : ") + ex.Message,
            Renci.SshNet.Common.SftpPathNotFoundException => L.T("chemin introuvable sur le serveur."),
            Renci.SshNet.Common.SftpPermissionDeniedException => L.T("accès refusé par le serveur (droits insuffisants)."),
            Renci.SshNet.Common.ProxyException => L.T("le proxy a refusé la connexion : ") + ex.Message,
            FluentFTP.Exceptions.FtpAuthenticationException => L.T("identifiant ou mot de passe refusés par le serveur FTP."),
            FluentFTP.Exceptions.FtpCommandException c => L.F("le serveur FTP a refusé l'opération ({0}) : {1}", c.CompletionCode, c.Message),
            FluentFTP.Exceptions.FtpSecurityNotAvailableException => L.T("le serveur ne propose pas de chiffrement TLS (FTPS)."),
            AuthenticationException => L.T("échec de la négociation TLS (certificat refusé ou version TLS non prise en charge)."),
            SocketException s => s.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData => L.T("serveur introuvable (nom d'hôte inconnu)."),
                SocketError.ConnectionRefused => L.T("connexion refusée (port fermé, ou service arrêté)."),
                SocketError.TimedOut => L.T("le serveur ne répond pas (délai dépassé)."),
                SocketError.NetworkUnreachable or SocketError.HostUnreachable => L.T("réseau ou serveur injoignable."),
                _ => L.T("erreur réseau : ") + s.Message,
            },
            HttpRequestException h when h.InnerException is SocketException se => Translate(se).Message,
            HttpRequestException h when h.InnerException is AuthenticationException => L.T("échec de la négociation TLS (certificat refusé)."),
            _ => ex.Message,
        };
        if (ex is FileOperationException) return new RemoteException(ex.Message, ex);
        return new RemoteException(prefix + char.ToUpper(msg[0]) + msg[1..], ex);
    }
}

/// <summary>Échec côté proxy (WebDAV / FTP).</summary>
public sealed class ProxyFailureException(string message) : Exception(message);

/// <summary>
/// Flux enveloppe : compte les octets (progression), respecte la pause et l'annulation.
/// En levant OperationCanceledException au milieu d'un Read/Write, il interrompt aussi les bibliothèques sans jeton d'annulation.
/// </summary>
public sealed class TransferStream(Stream inner, Action<long>? onBytes, CancellationToken ct, PauseController? pause, bool leaveOpen = false) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    private void Gate()
    {
        pause?.WaitIfPaused(ct);
        ct.ThrowIfCancellationRequested();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        Gate();
        int n = inner.Read(buffer, offset, count);
        if (n > 0) onBytes?.Invoke(n);
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        Gate();
        int n = inner.Read(buffer);
        if (n > 0) onBytes?.Invoke(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Gate();
        int n = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (n > 0) onBytes?.Invoke(n);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Gate();
        inner.Write(buffer, offset, count);
        onBytes?.Invoke(count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Gate();
        inner.Write(buffer);
        onBytes?.Invoke(buffer.Length);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Gate();
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        onBytes?.Invoke(buffer.Length);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen) inner.Dispose();
        base.Dispose(disposing);
    }
}
