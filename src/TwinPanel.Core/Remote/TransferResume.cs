// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json.Serialization;

namespace TwinPanel.Core.Remote;

/// <summary>Un fichier d'un transfert : d'où, vers où, et où il en est.</summary>
public sealed class TransferPlanFile
{
    public string Source { get; set; } = string.Empty;
    /// <summary>Chemin final, définitif une fois <see cref="Resolved"/> vrai (conflit de nom réglé).</summary>
    public string Target { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    /// <summary>Numéro de l'élément choisi par l'utilisateur dont il fait partie.</summary>
    public int Top { get; set; }
    public bool Resolved { get; set; }
    public bool Done { get; set; }
    /// <summary>Le fichier « .twinpanel-part » est le nôtre : seul celui-là peut être repris.</summary>
    public bool Started { get; set; }
    /// <summary>État de la source au moment où l'écriture a commencé (téléchargement).</summary>
    public long StampSize { get; set; }
    public DateTime StampModified { get; set; }
}

public sealed class TransferPlanDir
{
    public string Path { get; set; } = string.Empty;
    /// <summary>Déjà présent sur le serveur à la préparation (envoi).</summary>
    public bool Exists { get; set; }
    public bool Done { get; set; }
}

/// <summary>
/// Ce qu'un transfert doit faire, établi une seule fois : une reprise repart de là au lieu de relire les dossiers,
/// sans renvoyer ce qui est fini et sans changer les noms déjà choisis.
/// </summary>
public sealed class TransferPlan
{
    public bool IsUpload { get; set; }
    public List<TransferPlanFile> Files { get; set; } = [];
    public List<TransferPlanDir> Dirs { get; set; } = [];
    /// <summary>Éléments choisis dont une partie a été laissée de côté : leur source n'est pas supprimée (F6).</summary>
    public HashSet<int> Broken { get; set; } = [];
    /// <summary>Éléments choisis dont la source a déjà été supprimée (F6).</summary>
    public HashSet<int> Deleted { get; set; } = [];
    public List<string> Skipped { get; set; } = [];

    [JsonIgnore] public long BytesTotal => Files.Sum(f => f.Size);
    [JsonIgnore] public long BytesDone => Files.Where(f => f.Done).Sum(f => f.Size);
    [JsonIgnore] public int FilesDone => Files.Count(f => f.Done);
}

/// <summary>
/// Reprise d'un transfert : tentatives automatiques après une coupure, et bouton « Reprendre » ensuite.
/// Seule une coupure est retentée ; un refus (identifiant, droits, disque plein) s'arrête aussitôt.
/// </summary>
public sealed class TransferResume
{
    /// <summary>Sous cette taille, recommencer le fichier coûte moins cher que de vérifier s'il peut être repris.</summary>
    public const long DefaultMinBytes = 1024 * 1024;
    /// <summary>Fin de la partie déjà transférée, relue et comparée avant de reprendre.</summary>
    public const int TailBytes = 64 * 1024;

    private Func<CancellationToken, Task<IRemoteFileSystem>>? _connect;

    public int MaxRetries { get; init; } = 3;
    public Func<int, TimeSpan> RetryDelay { get; init; } = DefaultDelay;
    public long MinBytes { get; init; } = DefaultMinBytes;
    public TransferPlan? Plan { get; set; }

    /// <summary>5 s, 15 s, puis 30 s entre deux tentatives.</summary>
    public static TimeSpan DefaultDelay(int attempt) => TimeSpan.FromSeconds(attempt switch { 1 => 5, 2 => 15, _ => 30 });

    /// <summary>
    /// Fait le travail sur une connexion neuve, autant de fois qu'il le faut. Le compteur repart à zéro dès qu'un fichier
    /// a avancé : une ligne instable finit le travail, un serveur arrêté ne fait pas tourner en rond.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task<IRemoteFileSystem>> connect, Func<IRemoteFileSystem, Task> work,
        IProgress<TransferProgress>? progress, CancellationToken ct)
    {
        _connect = connect;
        int failures = 0;
        try
        {
            while (true)
            {
                int before = Plan?.FilesDone ?? 0;
                IRemoteFileSystem? fs = null;
                Exception failure;
                try
                {
                    fs = await connect(ct).ConfigureAwait(false);
                    await work(fs).ConfigureAwait(false);
                    await CloseAsync(fs).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ConnectionFailure.IsLost(ex, fs))
                {
                    failure = ex;
                }
                catch
                {
                    await CloseAsync(fs).ConfigureAwait(false);
                    throw;
                }
                await CloseAsync(fs).ConfigureAwait(false);

                if ((Plan?.FilesDone ?? 0) != before) failures = 0;
                if (failures >= MaxRetries) throw failure;
                failures++;
                var delay = RetryDelay(failures);
                progress?.Report(new TransferProgress(string.Empty, Plan?.BytesDone ?? 0, Plan?.BytesTotal ?? 0, Plan?.FilesDone ?? 0, Plan?.Files.Count ?? 0)
                {
                    Notice = L.F("Connexion perdue. Nouvelle tentative {0} sur {1} dans {2:0} s…", failures, MaxRetries, delay.TotalSeconds),
                });
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DiscardAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Abandon : retire les morceaux gardés pour la reprise. Serveur injoignable : ils restent, sans autre conséquence.</summary>
    /// <param name="connect">Pour un transfert rétabli qui n'a pas repris : sans connexion, les morceaux du serveur restent.</param>
    public async Task DiscardAsync(Func<CancellationToken, Task<IRemoteFileSystem>>? connect = null)
    {
        connect ??= _connect;
        var parts = Plan?.Files.Where(f => f.Started && !f.Done).Select(f => f.Target + RemoteTransfer.PartSuffix).ToList() ?? [];
        bool upload = Plan?.IsUpload == true;
        Plan = null;
        if (parts.Count == 0) return;
        if (!upload)
        {
            foreach (var part in parts)
            {
                try { File.Delete(part); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* il sera écrasé par le prochain transfert */ }
            }
            return;
        }
        if (connect is null) return;
        try
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await using var fs = await connect(limit.Token).ConfigureAwait(false);
            foreach (var part in parts) await fs.DeleteFileAsync(part, limit.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException) { /* serveur injoignable */ }
    }

    private static async Task CloseAsync(IRemoteFileSystem? fs)
    {
        if (fs is null) return;
        try { await fs.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidOperationException
                                       or Renci.SshNet.Common.SshException or FluentFTP.Exceptions.FtpException) { /* connexion déjà morte */ }
    }
}

/// <summary>
/// Commandes « cp » / « mv » d'un même travail, d'une connexion à l'autre. Après une coupure, le serveur continue le plus souvent
/// tout seul : il ne faut ni recommencer, ni écrire par-dessus.
/// </summary>
public sealed class ServerCommandState
{
    /// <summary>Cibles créées par nos commandes (absentes avant) : seules celles-là peuvent être retirées.</summary>
    public HashSet<string> Started { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Cibles terminées, avec leur taille.</summary>
    public Dictionary<string, long> Finished { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Sans aucun changement pendant ce temps, la commande est considérée comme arrêtée.</summary>
    [JsonIgnore] public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(20);
    [JsonIgnore] public TimeSpan CheckEvery { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>Distingue une coupure, qui vaut la peine de réessayer, d'un refus, qu'il ne faut surtout pas répéter.</summary>
public static class ConnectionFailure
{
    /// <summary>Coupure pendant une commande du serveur, qui ne doit pas passer pour un serveur sans commandes.</summary>
    public static RemoteException DuringCommand(string what, Exception cause) =>
        new(L.F("La connexion a été coupée pendant {0}. Le serveur la termine peut-être seul : vérifie la destination avant de recommencer.", what),
            new Renci.SshNet.Common.SshConnectionException(cause.Message));

    public static bool IsLost(Exception failure, IRemoteFileSystem? fs)
    {
        // Un compte ne doit pas se faire bloquer par des essais répétés ; droits ou chemin refusés le resteront.
        for (var e = failure; e is not null; e = e.InnerException)
            if (e is Renci.SshNet.Common.SshAuthenticationException or FluentFTP.Exceptions.FtpAuthenticationException or AuthenticationException
                or Renci.SshNet.Common.SftpPermissionDeniedException or Renci.SshNet.Common.SftpPathNotFoundException or UnauthorizedAccessException)
                return false;

        for (var e = failure; e is not null; e = e.InnerException)
        {
            if (e is Renci.SshNet.Common.SshConnectionException or Renci.SshNet.Common.SshOperationTimeoutException or TimeoutException
                or SocketException or EndOfStreamException or HttpIOException or HttpRequestException { StatusCode: null })
                return true;
            if (e is FluentFTP.Exceptions.FtpCommandException { CompletionCode: "421" or "425" or "426" }) return true;
        }

        try { return fs is { IsConnected: false }; }
        catch (ObjectDisposedException) { return true; }
    }
}

/// <summary>
/// Reçoit la suite d'un fichier repris : les premiers octets sont ceux que l'on a déjà. S'ils sont identiques, ils sont
/// écartés et le reste suit son chemin ; sinon l'écriture s'arrête, pour ne jamais raccorder deux fichiers différents.
/// Il ne se déplace pas : une bibliothèque ne peut pas le repositionner dans le fichier.
/// </summary>
public sealed class ResumeTailStream(Stream inner, byte[] expected) : Stream
{
    private int _checked;

    /// <summary>Le début reçu ne correspond pas à ce que l'on a.</summary>
    public bool Mismatch { get; private set; }
    public bool Verified => !Mismatch && _checked == expected.Length;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(Check(buffer.Length, buffer));
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int skip = Skip(buffer.Span);
        if (skip < buffer.Length) await inner.WriteAsync(buffer[skip..], cancellationToken).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    private ReadOnlySpan<byte> Check(int length, ReadOnlySpan<byte> buffer) => buffer[Skip(buffer)..length];

    /// <summary>Compare ce qui reste à vérifier ; rend le nombre d'octets à écarter.</summary>
    private int Skip(ReadOnlySpan<byte> buffer)
    {
        int count = Math.Min(expected.Length - _checked, buffer.Length);
        if (count == 0) return 0;
        if (!buffer[..count].SequenceEqual(expected.AsSpan(_checked, count)))
        {
            Mismatch = true;
            throw new IOException(L.T("Le fichier a changé : la reprise est abandonnée."));
        }
        _checked += count;
        return count;
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
