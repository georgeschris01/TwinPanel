// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
using TwinPanel.Core.Trash;

namespace TwinPanel.App.ViewModels;

/// <summary>
/// Corbeille de Windows montrée dans un panneau : restaurer, copier ailleurs (F5), voir (F3),
/// supprimer définitivement (F8) et vider. Le reste (renommer, nouveau dossier, coller…) n'y a pas de sens et est refusé.
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>Le panneau actif montre la corbeille.</summary>
    private bool ActiveIsTrash => ActivePanel.Current.IsTrash;

    /// <summary>Ouvre la corbeille dans le panneau actif.</summary>
    [RelayCommand]
    private async Task OpenRecycleBin()
    {
        if (!RecycleBin.IsSupported)
        {
            await _dialogs.ShowErrorAsync(L.T("Corbeille"), L.T("La corbeille n'est pas encore disponible sur ce système."));
            return;
        }
        await ActivePanel.Current.NavigateAsync(RecycleBin.PanelPath);
    }

    /// <summary>Entrée sur un élément de la corbeille : un dossier s'ouvre (en lecture seule), un fichier est montré (F3).</summary>
    private async Task OpenTrashItemAsync(FileEntry entry)
    {
        if (entry.IsDirectory) await ActivePanel.Current.NavigateAsync(entry.FullPath);
        else await View();
    }

    /// <summary>Refuse une opération qui n'a pas de sens dans la corbeille (ou dans un dossier supprimé). Vrai si elle est refusée.</summary>
    private async Task<bool> RefuseInTrashAsync()
    {
        if (ActivePanel.Current.IsInTrashFolder)
        {
            await _dialogs.ShowErrorAsync(L.T("Corbeille"), L.T("Cette opération n'est pas possible dans la corbeille.\n") +
                                                           L.T("Dans un dossier supprimé, tu peux voir (F3) et copier ailleurs (F5). Pour le restaurer ou le supprimer, reviens à la corbeille (dossier parent)."));
            return true;
        }
        if (!ActiveIsTrash) return false;
        await _dialogs.ShowErrorAsync(L.T("Corbeille"), L.T("Cette opération n'est pas possible dans la corbeille.\n") +
                                                       L.T("Tu peux restaurer, copier ailleurs (F5), voir (F3) et supprimer définitivement (F8)."));
        return true;
    }

    /// <summary>Ce chemin est la corbeille, ou l'intérieur d'un dossier supprimé.</summary>
    private static bool InTrash(string? path) => RecycleBin.IsPanelPath(path) || RecycleBin.ItemRootOf(path) is not null;

    /// <summary>Éléments de la corbeille visés dans le panneau actif (ceux qui ont disparu entre-temps sont oubliés).</summary>
    private static IReadOnlyList<RecycleItem> TrashItems(IReadOnlyList<FileEntry> entries) =>
        entries.Select(e => RecycleBin.Find(e.FullPath)).OfType<RecycleItem>().ToList();

    private static string Describe(IReadOnlyList<FileEntry> items) =>
        items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);

    /// <summary>Restaurer : chaque élément retourne à sa place d'origine. Un élément du même nom déjà là-bas n'est pas écrasé.</summary>
    [RelayCommand]
    private async Task RestoreFromRecycleBin()
    {
        if (!ActiveIsTrash) return;
        var entries = ActivePanel.Current.TargetEntries;
        if (entries.Count == 0) return;
        var skipped = new List<string>();
        var errors = new List<string>();
        ActivePanel.Current.MarkAll(false);
        var job = new OperationJobViewModel(L.F("Restauration de {0}", Describe(entries)), transfer: false, (progress, ct, _) => Task.Run(() =>
        {
            var items = TrashItems(entries); // lecture des fiches hors de la fenêtre : elle ne se fige pas
            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new TransferProgress(items[i].Name, 0, 0, i, items.Count));
                try
                {
                    if (!RecycleBin.Restore(items[i])) lock (skipped) skipped.Add(items[i].OriginalPath);
                }
                catch (FileOperationException ex) { lock (errors) errors.Add(ex.Message); }
            }
            progress.Report(new TransferProgress(string.Empty, 0, 0, items.Count, items.Count));
        }, ct));
        AfterTrashJob(job, async () =>
        {
            if (skipped.Count > 0)
                await _dialogs.ShowListAsync(L.T("Restaurer"), L.T("Ces éléments n'ont pas été restaurés : un élément du même nom est déjà à leur place d'origine. Copie-les ailleurs avec F5."), skipped);
            if (errors.Count > 0)
                await _dialogs.ShowListAsync(L.T("Restaurer"), L.T("Erreurs :"), errors);
        });
        Operations.Start(job);
    }

    /// <summary>F8 dans la corbeille : suppression définitive, toujours confirmée.</summary>
    private async Task<bool> TrashDeleteAsync()
    {
        if (ActivePanel.Current.IsInTrashFolder) return await RefuseInTrashAsync();
        if (!ActiveIsTrash) return false;
        var entries = ActivePanel.Current.TargetEntries;
        if (entries.Count == 0) return true;
        if (!await _dialogs.ConfirmAsync(L.T("Supprimer"), L.F("Supprimer DÉFINITIVEMENT {0} de la corbeille ?", Describe(entries)))) return true;
        ActivePanel.Current.MarkAll(false);
        StartTrashDelete(L.F("Suppression de {0}", Describe(entries)), () => TrashItems(entries));
        return true;
    }

    /// <summary>Vider la corbeille : tout est supprimé définitivement, après confirmation.</summary>
    [RelayCommand]
    private async Task EmptyRecycleBin()
    {
        if (!RecycleBin.IsSupported)
        {
            await _dialogs.ShowErrorAsync(L.T("Corbeille"), L.T("La corbeille n'est pas encore disponible sur ce système."));
            return;
        }
        var items = await Task.Run(() => RecycleBin.List());
        if (items.Count == 0)
        {
            StatusMessage = L.T("La corbeille est vide.");
            return;
        }
        if (!await _dialogs.ConfirmAsync(L.T("Vider la corbeille"), L.F("Supprimer DÉFINITIVEMENT les {0} éléments de la corbeille ?", items.Count))) return;
        StartTrashDelete(L.T("Vider la corbeille"), () => items);
    }

    private void StartTrashDelete(string title, Func<IReadOnlyList<RecycleItem>> list)
    {
        var errors = new List<string>();
        var job = new OperationJobViewModel(title, transfer: false, (progress, ct, _) => Task.Run(() =>
        {
            var items = list();
            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new TransferProgress(items[i].Name, 0, 0, i, items.Count));
                try { RecycleBin.DeletePermanently(items[i]); }
                catch (FileOperationException ex) { lock (errors) errors.Add(ex.Message); }
            }
            progress.Report(new TransferProgress(string.Empty, 0, 0, items.Count, items.Count));
        }, ct));
        AfterTrashJob(job, async () =>
        {
            if (errors.Count > 0) await _dialogs.ShowListAsync(L.T("Supprimer"), L.T("Erreurs :"), errors);
        });
        Operations.Start(job);
    }

    /// <summary>
    /// F5 / F6 depuis la corbeille. Copier : les éléments arrivent sous leur vrai nom. Déplacer : refusé (Restaurer ou copier).
    /// Vrai si la demande a été prise en charge ici.
    /// </summary>
    /// <param name="forced">Choix déjà fait pour les noms en double (« Tout garder ») : il s'applique aussi ici.</param>
    private async Task<bool> TrashTransferAsync(bool move, IReadOnlyList<FileEntry> items, string? destination, string sourceDir, ConflictPolicy? forced)
    {
        bool fromTrash = RecycleBin.IsPanelPath(sourceDir);
        bool fromTrashFolder = RecycleBin.ItemRootOf(sourceDir) is not null;
        bool intoTrash = InTrash(destination ?? OtherPanel.Current.CurrentPath);
        if (!fromTrash && !fromTrashFolder && !intoTrash) return false;
        if (intoTrash)
        {
            await _dialogs.ShowErrorAsync(L.T("Corbeille"), L.T("On ne copie rien dans la corbeille : pour y envoyer des fichiers, supprime-les (F8)."));
            return true;
        }
        if (move)
        {
            await _dialogs.ShowErrorAsync(L.T("Corbeille"), L.T("Pour sortir un élément de la corbeille : « Restaurer » le remet à sa place d'origine, F5 en fait une copie ailleurs."));
            return true;
        }
        // Dans un dossier supprimé, les éléments ont déjà leur vrai nom : copie ordinaire.
        if (fromTrashFolder) return false;

        var what = Describe(items);
        var request = await _dialogs.AskTransferAsync(L.T("Copier"), what, destination ?? OtherPanel.Current.CurrentPath);
        if (request is null || request.Destination.Length == 0) return true;
        var dest = request.Destination;
        if (InTrash(dest) || TwinPanel.Core.Archives.ArchivePath.TrySplit(dest, out _, out _)
            || TwinPanel.Core.Remote.PanelRemotePath.TryParse(dest, out _) || !Path.IsPathRooted(dest))
        {
            await _dialogs.ShowErrorAsync(L.T("Corbeille"), L.T("Depuis la corbeille, on copie vers un dossier du PC (pas une archive ni un serveur)."));
            return true;
        }
        if (!Directory.Exists(dest))
        {
            if (!await _dialogs.ConfirmAsync(L.T("Dossier inexistant"), L.F("Le dossier{0}{1}{2}n'existe pas. Le créer ?", Environment.NewLine, dest, Environment.NewLine))) return true;
            try { Directory.CreateDirectory(dest); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                await _dialogs.ShowErrorAsync(L.T("Erreur"), ex.Message);
                return true;
            }
        }

        var named = items.Select(i => (i.FullPath, i.Name)).ToList();
        var policy = forced ?? request.Policy ?? ConflictPolicy.Overwrite;
        if (forced is null && request.Policy is null)
        {
            var conflicts = items.Select(i => Path.Combine(dest, i.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
            if (conflicts.Count > 0)
            {
                if (await _dialogs.AskConflictAsync(conflicts) is not { } chosen) return true;
                policy = chosen;
            }
        }
        ActivePanel.Current.MarkAll(false);
        var job = new OperationJobViewModel(L.F("{0} de {1} vers {2}", L.T("Copie"), what, dest), transfer: true,
            (progress, ct, pause) => FileOperations.CopyNamedAsync(named, dest, policy, progress, ct, pause));
        FollowTransfer(job, items, sourceDir, dest);
        Operations.Start(job);
        return true;
    }

    /// <summary>À la fin d'un travail sur la corbeille : panneaux qui la montrent relus, icône de la corbeille mise à jour, puis message éventuel.</summary>
    private void AfterTrashJob(OperationJobViewModel job, Func<Task> report)
    {
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OperationJobViewModel.State) || job.State is not (JobState.Done or JobState.Failed or JobState.Cancelled)) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await RefreshTrashViewsAsync();
                await report();
            });
        };
    }

    /// <summary>Relit les panneaux qui montrent la corbeille et l'icône « pleine / vide » des barres de disques.</summary>
    private async Task RefreshTrashViewsAsync()
    {
        Left.RefreshTrashState();
        Right.RefreshTrashState();
        foreach (var panel in new[] { Left, Right })
            if (panel.Current.IsTrash) await panel.Current.RefreshAsync();
    }
}
