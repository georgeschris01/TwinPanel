// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Archives;
using TwinPanel.Core.Operations;
using FileClipboard = TwinPanel.Core.Operations.FileClipboard;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.ViewModels;

/// <summary>Ce que le presse-papiers du système contient en fichiers (lu par la fenêtre).</summary>
/// <param name="Token">Marque de TwinPanel, si c'est lui qui les y a mis.</param>
/// <param name="LocalPaths">Fichiers du PC (mis par TwinPanel ou un autre programme, comme l'Explorateur).</param>
/// <param name="Move">Vrai si les fichiers ont été « coupés ».</param>
public sealed record ClipboardFiles(string? Token, IReadOnlyList<string> LocalPaths, bool Move);

/// <summary>Accès au presse-papiers du système, fourni par la fenêtre.</summary>
public interface IFileClipboardHost
{
    Task PutAsync(string token, IReadOnlyList<string> localPaths, bool move);
    Task<ClipboardFiles?> ReadAsync();
    Task ClearAsync();
}

/// <summary>
/// Copie sur le PC, faite au dernier moment, des éléments d'un serveur ou d'une archive glissés vers un autre programme.
/// Les fichiers vides créés au départ du glisser ne sont remplis que si l'autre programme les demande (au dépôt) ;
/// lâchés dans TwinPanel, ils ne servent pas et sont retirés.
/// </summary>
public sealed class DragStaging(string folder, IReadOnlyList<FileEntry> entries, IReadOnlyList<string> localPaths,
    Func<CancellationToken, Task> work, Action<string> status)
{
    /// <summary>Le programme qui reçoit attend pendant le téléchargement : au-delà, on abandonne.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);

    private bool? _ready;

    public string Folder { get; } = folder;
    public IReadOnlyList<string> LocalPaths { get; } = localPaths;
    public bool Used => _ready is not null;

    /// <summary>Fichiers et dossiers vides à leur place, pour pouvoir les annoncer à l'autre programme avant de les remplir.</summary>
    public void CreatePlaceholders()
    {
        for (int i = 0; i < LocalPaths.Count; i++)
        {
            var path = LocalPaths[i];
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (entries[i].IsDirectory) Directory.CreateDirectory(path);
            else File.Create(path).Dispose();
        }
    }

    /// <summary>
    /// Appelé par l'autre programme quand il demande les fichiers (fil de l'interface, qui attend) : télécharge une seule fois.
    /// Faux en cas d'échec : rien n'est donné, plutôt que des fichiers vides.
    /// </summary>
    public bool EnsureReady()
    {
        if (_ready is { } done) return done;
        status(L.T("Téléchargement pour l'autre programme…"));
        using var cts = new CancellationTokenSource();
        try
        {
            var task = Task.Run(() => work(cts.Token));
            _ready = task.Wait(MaxWait);
            if (_ready == false) cts.Cancel();
        }
        catch (AggregateException ex)
        {
            _ready = false;
            status(L.F("Glisser vers l'autre programme impossible : {0}", ex.InnerException?.Message ?? ex.Message));
            return false;
        }
        status(_ready == true ? L.T("Fichiers donnés à l'autre programme.") : L.T("Glisser vers l'autre programme abandonné : téléchargement trop long. Utilise Copier, puis Coller."));
        return _ready == true;
    }

    /// <summary>Fin du glisser : si personne n'a demandé les fichiers, les fichiers vides sont retirés.</summary>
    public void End()
    {
        if (_ready == true) return; // l'autre programme a reçu les copies
        try { Directory.Delete(Folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }
}

/// <summary>
/// Couper / Copier / Coller des fichiers (Ctrl+X / Ctrl+C / Ctrl+V, menu du clic droit).
/// Dans TwinPanel : partout (PC, archives, serveurs), avec la même fenêtre et les mêmes transferts que F5 / F6.
/// Avec les autres programmes (Explorateur…) : les fichiers du PC ; ceux d'un serveur ou d'une archive y sont d'abord
/// copiés dans un dossier temporaire (<see cref="ClipboardStaging"/>), et les autres programmes reçoivent ces copies.
/// </summary>
public partial class MainWindowViewModel
{
    private sealed record FileClip(string Token, IReadOnlyList<FileEntry> Entries, string Folder, bool Move);

    /// <summary>Copie vers le PC : dossier, chemins des copies (même ordre que les éléments), travail à faire.</summary>
    private sealed record Staging(string Folder, IReadOnlyList<FileEntry> Entries, IReadOnlyList<string> LocalPaths,
        Func<IProgress<TransferProgress>?, CancellationToken, PauseController?, Task> Work);

    private FileClip? _fileClip;

    public IFileClipboardHost? ClipboardHost { get; set; }

    [RelayCommand] private Task ClipboardCopy() => PutOnClipboardAsync(move: false);
    [RelayCommand] private Task ClipboardCut() => PutOnClipboardAsync(move: true);

    private async Task PutOnClipboardAsync(bool move)
    {
        var tab = ActivePanel.Current;
        var items = tab.TargetEntries;
        if (items.Count == 0 || ClipboardHost is not { } host) return;
        var token = Guid.NewGuid().ToString("N");
        bool local = !tab.IsRemote && !tab.IsInsideArchive;
        try
        {
            await host.PutAsync(token, local ? items.Select(i => i.FullPath).ToList() : [], move);
        }
        catch (Exception ex)
        {
            StatusMessage = L.F("Presse-papiers indisponible : {0}", ex.Message);
            return;
        }
        _fileClip = new FileClip(token, items, tab.CurrentPath, move);
        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        StatusMessage = move
            ? L.F("{0} coupé : Ctrl+V pour le coller dans un autre dossier.", what)
            : L.F("{0} copié : Ctrl+V pour le coller dans un autre dossier.", what);
        if (!local) await StageForOtherProgramsAsync(host, token, items, tab.CurrentPath, what);
    }

    /// <summary>
    /// Serveur ou archive : les éléments sont copiés sur le PC, puis ces copies sont mises dans le presse-papiers pour les
    /// autres programmes. Dans TwinPanel, Coller marche déjà sans attendre (directement depuis le serveur ou l'archive).
    /// Couper vers un autre programme est une copie : il ne peut rien effacer sur le serveur.
    /// </summary>
    private async Task StageForOtherProgramsAsync(IFileClipboardHost host, string token, IReadOnlyList<FileEntry> items, string sourceFolder, string what)
    {
        if (ClipboardStaging.ShouldAsk(items)
            && !await _dialogs.ConfirmAsync(L.T("Copier pour les autres programmes"),
                L.F("Préparer {0} ({1}) pour les autres programmes (Explorateur, messagerie…) ? Il faut d'abord les copier sur ce PC.{2}{2}Dans TwinPanel, Coller marche déjà, sans attendre.",
                    what, FileEntry.FormatSize(ClipboardStaging.KnownBytes(items)), Environment.NewLine)))
            return;

        ClipboardStaging.Default.CleanUp(); // copies du Copier précédent (et des glisser)
        if (PrepareStaging(items, sourceFolder) is not { } staging) return;
        var job = new OperationJobViewModel(L.F("Copie pour les autres programmes : {0}", what), transfer: true,
            (progress, ct, pause) => staging.Work(progress, ct, pause));
        Operations.Start(job);
        if (await FinishedAsync(job) != JobState.Done) return;

        // Entre-temps, autre chose a pu être copié : on ne le remplace pas.
        if (_fileClip?.Token != token) return;
        try
        {
            if ((await host.ReadAsync())?.Token != token) return;
            var ready = staging.LocalPaths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (ready.Count == 0) return;
            await host.PutAsync(token, ready, move: false);
            StatusMessage = L.F("{0} : prêt à coller aussi dans les autres programmes.", what);
        }
        catch (Exception ex)
        {
            StatusMessage = L.F("Presse-papiers indisponible : {0}", ex.Message);
        }
    }

    /// <summary>
    /// Préparation de la copie sur le PC d'éléments d'un serveur ou d'une archive. Null si ce n'est ni l'un ni l'autre,
    /// ou si la connexion n'est plus ouverte. Le dossier est créé ; rien n'est encore copié.
    /// </summary>
    /// <param name="mainConnection">Vrai : par la connexion déjà ouverte (glisser : aucune question possible pendant l'attente).</param>
    private Staging? PrepareStaging(IReadOnlyList<FileEntry> items, string sourceFolder, bool mainConnection = false)
    {
        Func<string, IReadOnlyList<FileEntry>, IProgress<TransferProgress>?, CancellationToken, PauseController?, Task> copy;
        if (PanelRemotePath.TryParse(sourceFolder, out var from))
        {
            if (_remote?.Find(from.Session) is not { } session) return null;
            copy = (dir, group, progress, ct, pause) =>
            {
                var remoteItems = group.Select(ToRemoteItem).ToList();
                return mainConnection
                    ? session.UseAsync(fs => RemoteTransfer.DownloadAsync(fs, remoteItems, dir, ConflictPolicy.Overwrite, progress, ct, pause), ct)
                    : RemoteTransfer.DownloadAsync(session, remoteItems, dir, ConflictPolicy.Overwrite, progress, ct, pause);
            };
        }
        else if (ArchivePath.TrySplit(sourceFolder, out var archive, out var innerBase))
        {
            copy = (dir, group, progress, ct, pause) =>
            {
                var keys = group.Select(i => ArchivePath.TrySplit(i.FullPath, out _, out var k) ? k : string.Empty).Where(k => k.Length > 0).ToList();
                return ArchiveService.ExtractAsync(archive, keys, innerBase, dir, ConflictPolicy.Overwrite, progress, ct, pause);
            };
        }
        else return null;

        var folder = ClipboardStaging.Default.NewFolder();
        var plan = ClipboardStaging.Plan(folder, items, e => e.Name);
        var ordered = plan.SelectMany(g => g.Items).ToList();
        return new Staging(folder, ordered, ClipboardStaging.LocalPaths(plan, e => e.Name), async (progress, ct, pause) =>
        {
            foreach (var (dir, group) in plan)
            {
                Directory.CreateDirectory(dir);
                await copy(dir, group, progress, ct, pause).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Glisser depuis un serveur ou une archive : copie sur le PC préparée, faite seulement si un autre programme la demande.
    /// Null : rien à préparer (PC), connexion fermée, ou trop gros pour faire attendre l'autre programme.
    /// </summary>
    public DragStaging? PrepareDragStaging(IReadOnlyList<FileEntry> items, string sourceFolder)
    {
        if (ClipboardStaging.ShouldAsk(items))
        {
            StatusMessage = L.T("Gros volume : pour le donner à un autre programme, utilise Copier (Ctrl+C), puis Coller dans ce programme.");
            return null;
        }
        if (PrepareStaging(items, sourceFolder, mainConnection: true) is not { } staging) return null;
        var drag = new DragStaging(staging.Folder, staging.Entries, staging.LocalPaths, ct => staging.Work(null, ct, null),
            text => StatusMessage = text);
        try { drag.CreatePlaceholders(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            drag.End();
            return null;
        }
        return drag;
    }

    /// <summary>Attend la fin d'une opération (terminée, en échec ou annulée).</summary>
    private static Task<JobState> FinishedAsync(OperationJobViewModel job)
    {
        var done = new TaskCompletionSource<JobState>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check()
        {
            if (job.State is JobState.Done or JobState.Failed or JobState.Cancelled) done.TrySetResult(job.State);
        }
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperationJobViewModel.State)) Check();
        };
        Check();
        return done.Task;
    }

    /// <summary>Au démarrage : copies pour les autres programmes de plus d'un jour.</summary>
    public static void CleanOldStaging() =>
        _ = Task.Run(() => ClipboardStaging.Default.CleanUp(ClipboardStaging.KeepFor));

    [RelayCommand]
    private async Task ClipboardPaste()
    {
        if (ClipboardHost is not { } host) return;
        ClipboardFiles? content;
        try { content = await host.ReadAsync(); }
        catch (Exception ex)
        {
            StatusMessage = L.F("Presse-papiers indisponible : {0}", ex.Message);
            return;
        }
        var dest = ActivePanel.Current.CurrentPath;

        // Mis par TwinPanel : la liste gardée vaut pour tout (serveurs et archives compris).
        if (content?.Token is { } token && _fileClip is { } clip && clip.Token == token)
        {
            if (FileClipboard.AnyAlreadyIn(clip.Entries.Select(e => e.FullPath), dest))
            {
                StatusMessage = L.T("Ces éléments sont déjà dans ce dossier : ouvre un autre dossier pour les coller.");
                return;
            }
            if (PanelRemotePath.TryParse(clip.Folder, out var from) && _remote?.Find(from.Session) is null)
            {
                await _dialogs.ShowErrorAsync(L.T("Coller"), L.F("La connexion « {0} » n'est plus ouverte : rouvre-la, puis colle à nouveau.", from.Session));
                return;
            }
            if (clip.Move) await ForgetCutAsync(host);
            await TransferAsync(clip.Move, clip.Entries, dest, fromPanel: true, sourceFolder: clip.Folder);
            return;
        }

        // Fichiers du PC mis par un autre programme (Explorateur…).
        var entries = (content?.LocalPaths ?? []).Select(FileClipboard.LocalEntry).OfType<FileEntry>().ToList();
        if (entries.Count == 0)
        {
            StatusMessage = L.T("Le presse-papiers ne contient pas de fichiers à coller.");
            return;
        }
        if (FileClipboard.AnyAlreadyIn(entries.Select(e => e.FullPath), dest))
        {
            StatusMessage = L.T("Ces éléments sont déjà dans ce dossier : ouvre un autre dossier pour les coller.");
            return;
        }
        bool move = content!.Move;
        if (move) await ForgetCutAsync(host);
        await TransferAsync(move, entries, dest, fromPanel: false);
    }

    /// <summary>Des éléments coupés ne se collent qu'une fois (comme dans l'Explorateur).</summary>
    private async Task ForgetCutAsync(IFileClipboardHost host)
    {
        _fileClip = null;
        try { await host.ClearAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }
}
