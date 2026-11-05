// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.ViewModels;

/// <summary>Transferts avec un serveur : lancement, et reprise de ceux qu'une fermeture a interrompus.</summary>
public partial class MainWindowViewModel
{
    private const int PendingTitlesShown = 5;

    private enum PendingChoice { Resume, Later, Forget }

    private readonly TransferQueueStore _transfers;

    /// <summary>Un transfert en cours pourra reprendre après la fermeture.</summary>
    public bool HasKeptTransfers => Operations.Jobs.Any(j => j.IsActive && j.Saved is not null);

    /// <summary>
    /// Lance un transfert, nouveau ou rétabli. Il n'est gardé que si ses serveurs ont une fiche enregistrée :
    /// une connexion rapide n'a rien qui permette de la retrouver.
    /// </summary>
    /// <param name="follow">Appelé avec l'opération juste avant son départ (curseur à la fin du transfert).</param>
    private void StartTransfer(SavedTransfer transfer, RemoteSession? src, RemoteSession? dst, Action<OperationJobViewModel>? follow = null)
    {
        int retries = Math.Clamp(_settings.Behavior.TransferRetries, 0, 10);
        var (items, locals, dest, policy, move) = (transfer.Items, transfer.LocalPaths, transfer.Destination, transfer.Policy, transfer.Move);
        TransferResume? resume = null;
        Func<IProgress<TransferProgress>, CancellationToken, PauseController, Task> work;

        if (src is null && dst is not null)
        {
            resume = new TransferResume { MaxRetries = retries, Plan = transfer.Plan };
            work = (p, ct, pause) => Task.Run(() => RemoteTransfer.UploadAsync(dst, locals, dest, policy, p, ct, pause, deleteSource: move, resume: resume), ct);
        }
        else if (src is not null && dst is null)
        {
            resume = new TransferResume { MaxRetries = retries, Plan = transfer.Plan };
            work = (p, ct, pause) => Task.Run(() => RemoteTransfer.DownloadAsync(src, items, dest, policy, p, ct, pause, deleteSource: move, resume: resume), ct);
        }
        else
        {
            // Sur un même serveur, relancer retrouve les commandes en cours : le bouton « Reprendre » a un sens.
            if (src == dst) resume = new TransferResume { MaxRetries = retries };
            var commands = transfer.Commands ??= new ServerCommandState();
            work = src == dst && move
                ? (p, ct, pause) => Task.Run(() => RemoteTransfer.MoveOnServerAsync(src!, items, dest, policy, p, ct, pause, retries, commands), ct)
                : (p, ct, pause) => Task.Run(() => RemoteTransfer.CopyBetweenAsync(src!, items, dst!, dest, policy, p, ct, pause, deleteSource: move, retries: retries, state: commands), ct);
        }

        bool keep = HasProfile(src) && HasProfile(dst);
        transfer.Live = resume;
        transfer.HasJob = true;
        if (keep) _transfers.Add(transfer);
        var job = new OperationJobViewModel(transfer.Title, transfer: true, work, resume) { Saved = keep ? transfer : null };
        follow?.Invoke(job);
        Operations.Start(job);
    }

    private bool HasProfile(RemoteSession? session) => session is null || _connections?.FindProfile(session.Profile.Id) is not null;

    /// <summary>Au démarrage : rien ne repart sans l'accord de l'utilisateur.</summary>
    public async Task OfferPendingTransfersAsync()
    {
        if (_transfers.Pending is { Count: > 0 } pending) await AskPendingAsync(pending);
    }

    /// <summary>Transferts mis de côté avec « Plus tard ».</summary>
    [RelayCommand]
    private async Task ShowPendingTransfers()
    {
        if (!_transfers.IsOwner) StatusMessage = L.T("Une autre fenêtre de TwinPanel s'occupe des transferts non terminés.");
        else if (_transfers.Pending is { Count: > 0 } pending) await AskPendingAsync(pending);
        else StatusMessage = L.T("Aucun transfert non terminé.");
    }

    private async Task AskPendingAsync(IReadOnlyList<SavedTransfer> pending)
    {
        var lines = pending.Take(PendingTitlesShown).Select(t => "• " + t.Title).ToList();
        if (pending.Count > PendingTitlesShown) lines.Add(L.F("… et {0} autre(s)", pending.Count - PendingTitlesShown));
        var title = pending.Count == 1 ? L.T("1 transfert non terminé") : L.F("{0} transferts non terminés", pending.Count);
        var choice = await _dialogs.ChooseAsync<PendingChoice>(title, string.Join(Environment.NewLine, lines),
            (L.T("Reprendre"), PendingChoice.Resume), (L.T("Plus tard"), PendingChoice.Later), (L.T("Oublier"), PendingChoice.Forget));

        if (choice == PendingChoice.Resume) await ResumePendingAsync(pending);
        else if (choice == PendingChoice.Forget
                 && await _dialogs.ConfirmAsync(title, L.T("Oublier ces transferts ? Les morceaux déjà reçus sur le PC seront retirés, et ceux des serveurs connectés aussi.")))
            await ForgetPendingAsync(pending);
    }

    private async Task ResumePendingAsync(IReadOnlyList<SavedTransfer> pending)
    {
        // Un serveur refusé n'est pas redemandé pour chacun de ses transferts.
        var sessions = new Dictionary<Guid, RemoteSession?>();
        async Task<RemoteSession?> OpenAsync(Guid id)
        {
            if (sessions.TryGetValue(id, out var known)) return known;
            RemoteSession? session = _remote?.FindByProfile(id);
            if (session is null && _connections?.FindProfile(id) is { } profile)
                await WithSessionAsync(profile, opened => { session = opened; return Task.CompletedTask; });
            return sessions[id] = session;
        }

        int left = 0;
        foreach (var transfer in pending)
        {
            var src = transfer.SourceProfile is { } from ? await OpenAsync(from) : null;
            var dst = transfer.TargetProfile is { } to ? await OpenAsync(to) : null;
            if ((transfer.SourceProfile is not null && src is null) || (transfer.TargetProfile is not null && dst is null)) { left++; continue; }
            StartTransfer(transfer, src, dst);
        }
        if (left > 0)
            await _dialogs.ShowErrorAsync(L.T("Transferts non terminés"),
                L.F("{0} transfert(s) attendent : connexion impossible, ou fiche supprimée. Ils restent dans ≡ → Connexions → Transferts non terminés.", left));
    }

    private async Task ForgetPendingAsync(IReadOnlyList<SavedTransfer> pending)
    {
        int left = 0;
        foreach (var transfer in pending)
        {
            var server = transfer.TargetProfile is { } id ? _remote?.FindByProfile(id) : null;
            if (server is null)
                left += (transfer.Plan is { IsUpload: true } plan ? plan.Files.Count(f => f.Started && !f.Done) : 0)
                        + (transfer.Commands?.Started.Count(t => !transfer.Commands.Finished.ContainsKey(t)) ?? 0);
            await new TransferResume { Plan = transfer.Plan }.DiscardAsync(server is null ? null : server.OpenWorkerAsync);
            _transfers.Remove(transfer);
        }
        _transfers.Save();
        StatusMessage = left > 0
            ? L.F("Transferts oubliés. {0} morceau(x) reste(nt) sur le serveur, à supprimer à la main (extension {1} ou copie incomplète).", left, RemoteTransfer.PartSuffix)
            : L.T("Transferts oubliés.");
    }
}
