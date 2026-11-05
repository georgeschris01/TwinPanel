// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.ViewModels;
using TwinPanel.Core;

namespace TwinPanel.App.Services;

/// <summary>
/// Lance les opérations sur les fichiers. Les transferts tournent au nombre choisi dans les Options, les suivants attendent ;
/// les autres opérations (suppressions, tests, sommes de contrôle) partent tout de suite.
/// </summary>
public sealed partial class OperationManager : ObservableObject
{
    private readonly TransferGate<OperationJobViewModel> _gate = new();

    public OperationManager()
    {
        Jobs.CollectionChanged += (_, _) => UpdateShown();
    }

    /// <summary>Toutes les opérations (panneau « tous les transferts »).</summary>
    public ObservableCollection<OperationJobViewModel> Jobs { get; } = [];

    /// <summary>Les lignes affichées en bas de la fenêtre.</summary>
    public ObservableCollection<OperationJobViewModel> Shown { get; } = [];

    [ObservableProperty] private bool _showAll;

    public int OthersCount => Jobs.Count - Shown.Count;
    public bool HasOthers => OthersCount > 0;
    public string OthersLabel => OthersCount == 1 ? L.T("+ 1 autre transfert") : L.F("+ {0} autres transferts", OthersCount);
    public string AllTitle => L.F("Tous les transferts ({0})", Jobs.Count);

    /// <summary>Levé (sur le fil de l'interface) quand une opération se termine, quelle qu'en soit l'issue.</summary>
    public event EventHandler<OperationJobViewModel>? JobFinished;

    /// <summary>Levé quand l'utilisateur renonce à une opération échouée.</summary>
    public event EventHandler<OperationJobViewModel>? JobDismissed;

    /// <summary>Une opération a besoin de l'utilisateur (plus de place sur le disque).</summary>
    public event EventHandler<string>? AttentionNeeded;

    /// <summary>Transferts en même temps : un changement s'applique tout de suite, sans rien interrompre.</summary>
    public int Limit
    {
        get => _gate.Limit;
        set { foreach (var next in _gate.SetLimit(value)) _ = RunAsync(next); }
    }

    public void Start(OperationJobViewModel job)
    {
        job.AttentionNeeded += (_, message) => AttentionNeeded?.Invoke(this, message);
        job.PropertyChanged += OnJobChanged;
        Jobs.Add(job);
        Launch(job);
    }

    /// <summary>« Reprendre » sur un transfert échoué : il repasse par la limite.</summary>
    public void Resume(OperationJobViewModel job)
    {
        if (!job.CanResume) return;
        job.PrepareResume();
        Launch(job);
    }

    public void Dismiss(OperationJobViewModel job)
    {
        if (!job.IsFinished) return;
        if (job.CanResume) _ = job.DiscardAsync();
        Jobs.Remove(job);
        JobDismissed?.Invoke(this, job);
    }

    [RelayCommand]
    private void ToggleAll() => ShowAll = !ShowAll;

    private void Launch(OperationJobViewModel job)
    {
        if (!job.IsTransfer || _gate.Enter(job, job.ExclusiveKey)) _ = RunAsync(job);
        else job.ShowWaiting();
    }

    private async Task RunAsync(OperationJobViewModel job)
    {
        try { await job.RunAsync(); }
        finally
        {
            if (job.IsTransfer)
                foreach (var next in _gate.Leave(job)) _ = RunAsync(next);
        }
        await FinishAsync(job);
    }

    private async Task FinishAsync(OperationJobViewModel job)
    {
        JobFinished?.Invoke(this, job);

        // Une opération réussie disparaît après quelques secondes ; une erreur reste affichée.
        if (job.State is JobState.Done or JobState.Cancelled)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            Dispatcher.UIThread.Post(() => Jobs.Remove(job));
        }
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OperationJobViewModel.State)) return;
        var job = (OperationJobViewModel)sender!;
        UpdateShown();
        // Annulé pendant son attente : il ne passera jamais par RunAsync.
        if (job.State == JobState.Cancelled && _gate.Forget(job)) _ = FinishAsync(job);
    }

    /// <summary>Déplace les lignes sans les recréer : une barre ne clignote pas quand une autre change d'état.</summary>
    private void UpdateShown()
    {
        var wanted = ProgressLines.Pick(Jobs, j => j.DisplayRank);
        for (int i = Shown.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Shown[i])) Shown.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = Shown.IndexOf(wanted[i]);
            if (at < 0) Shown.Insert(i, wanted[i]);
            else if (at != i) Shown.Move(at, i);
        }
        OnPropertyChanged(nameof(OthersCount));
        OnPropertyChanged(nameof(HasOthers));
        OnPropertyChanged(nameof(OthersLabel));
        OnPropertyChanged(nameof(AllTitle));
        if (!HasOthers) ShowAll = false; // tout est visible en bas : le panneau ne sert plus à rien
    }
}
