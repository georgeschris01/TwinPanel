// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;

namespace TwinPanel.App.ViewModels;

public enum JobState { Waiting, Running, Paused, Done, Failed, Cancelled }

/// <summary>Une opération affichée dans le panneau de progression (copie, déplacement, suppression).</summary>
public partial class OperationJobViewModel : ObservableObject
{
    private readonly Func<IProgress<TransferProgress>, CancellationToken, PauseController, Task> _work;
    private readonly CancellationTokenSource _cts = new();
    private readonly PauseController _pause = new();
    private readonly Stopwatch _clock = new();
    private readonly TwinPanel.Core.Remote.TransferResume? _resume;

    /// <param name="transfer">Compte parmi les transferts limités (Options → Comportement).</param>
    /// <param name="resume">Donné aux transferts qui savent reprendre : le bouton « Reprendre » apparaît après un échec.</param>
    public OperationJobViewModel(string title, bool transfer, Func<IProgress<TransferProgress>, CancellationToken, PauseController, Task> work,
        TwinPanel.Core.Remote.TransferResume? resume = null)
    {
        _resume = resume;
        _title = title;
        IsTransfer = transfer;
        _work = work;
        _pause.PauseRequested += OnPauseRequested;
        _detail = L.T("Préparation…");
    }

    public bool IsTransfer { get; }

    /// <summary>Deux opérations de même clé (la même archive) ne tournent jamais ensemble.</summary>
    public string? ExclusiveKey { get; init; }

    /// <summary>L'utilisateur doit agir (plus de place sur le disque).</summary>
    public event EventHandler<string>? AttentionNeeded;

    private void OnPauseRequested(string reason) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (State != JobState.Running) return;
        State = JobState.Paused;
        _clock.Stop();
        Detail = reason.Split('\n')[0];
        AttentionNeeded?.Invoke(this, reason);
    });

    /// <summary>Sa fiche dans la liste des transferts gardés ; null s'il ne survit pas à une fermeture.</summary>
    public TwinPanel.Core.Remote.SavedTransfer? Saved { get; init; }

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _detail;
    [ObservableProperty] private double _percent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsFinished), nameof(CanPause), nameof(PauseLabel), nameof(CanResume))]
    private JobState _state = JobState.Waiting;
    [ObservableProperty] private string? _error;

    public bool IsActive => State is JobState.Waiting or JobState.Running or JobState.Paused;
    public bool IsFinished => !IsActive;
    public bool CanPause => State is JobState.Running or JobState.Paused;
    public string PauseLabel => State == JobState.Paused ? L.T("Reprendre") : "Pause";
    public bool CanResume => State == JobState.Failed && _resume is not null;

    /// <summary>Ordre d'affichage : ce qui demande de l'attention, ce qui tourne, ce qui attend, ce qui est fini.</summary>
    public int DisplayRank => State switch
    {
        JobState.Failed or JobState.Paused => 0,
        JobState.Running => 1,
        JobState.Waiting => 2,
        _ => 3,
    };

    /// <summary>Tous les transferts permis tournent : celui-ci attend.</summary>
    public void ShowWaiting() => Detail = L.T("En attente de son tour…");

    /// <summary>Remet un transfert échoué en route : ce qui est fini n'est pas refait.</summary>
    public void PrepareResume()
    {
        Error = null;
        Detail = L.T("Reprise…");
        State = JobState.Waiting;
        _clock.Reset();
    }

    /// <summary>L'utilisateur renonce à ce transfert : les morceaux gardés sont retirés.</summary>
    public Task DiscardAsync() => _resume?.DiscardAsync() ?? Task.CompletedTask;

    /// <summary>Exécute l'opération ; ne lève jamais d'exception (l'erreur est dans <see cref="Error"/>).</summary>
    public async Task RunAsync()
    {
        if (_cts.IsCancellationRequested) { State = JobState.Cancelled; return; }
        State = JobState.Running;
        _clock.Start();
        var progress = new Progress<TransferProgress>(OnProgress);
        try
        {
            await _work(progress, _cts.Token, _pause);
            Percent = 100;
            State = JobState.Done;
            Detail = L.F("Terminé en {0:mm\\:ss}", _clock.Elapsed);
        }
        catch (OperationCanceledException)
        {
            State = JobState.Cancelled;
            Detail = L.T("Annulé");
        }
        catch (Exception ex) when (ex is FileOperationException or IOException or UnauthorizedAccessException)
        {
            State = JobState.Failed;
            Error = ex.Message;
            Detail = L.T("Échec : ") + ex.Message;
            if (DiskSpace.IsFull(ex)) AttentionNeeded?.Invoke(this, ex.Message);
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (State == JobState.Running) { _pause.Pause(); State = JobState.Paused; _clock.Stop(); }
        else if (State == JobState.Paused) { _pause.Resume(); State = JobState.Running; _clock.Start(); }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        _pause.Resume(); // une opération en pause doit pouvoir voir l'annulation
        if (State == JobState.Waiting) { Detail = L.T("Annulé"); State = JobState.Cancelled; }
    }

    private void OnProgress(TransferProgress p)
    {
        if (State == JobState.Paused) return; // la raison de la pause reste affichée
        Percent = Math.Round(p.Fraction * 100, 1);
        if (p.Notice is { } notice) { Detail = notice; return; }
        var speed = _clock.Elapsed.TotalSeconds > 0.5 && p.BytesDone > 0
            ? $" — {FileEntry.FormatSize((long)(p.BytesDone / _clock.Elapsed.TotalSeconds))}/s"
            : string.Empty;
        var name = Path.GetFileName(p.CurrentItem);
        Detail = p.BytesTotal > 0
            ? L.F("{0}/{1} fichier(s), {2} / {3}{4}  {5}", p.FilesDone, p.FilesTotal, FileEntry.FormatSize(p.BytesDone), FileEntry.FormatSize(p.BytesTotal), speed, name)
            : $"{p.FilesDone}/{p.FilesTotal}  {name}";
    }
}
