// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core;
using TwinPanel.Core.Archives;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.ViewModels;

/// <summary>Modifier une archive affichée dans un panneau : F2 / Maj+F6, F5, F6, F7, F8.</summary>
public partial class MainWindowViewModel
{
    /// <summary>Archive du panneau actif (ou de ce dossier), si elle est modifiable ; sinon le refus est dit.</summary>
    private async Task<(string Archive, string Inner)?> EditableArchiveAsync(string? folder = null)
    {
        ArchivePath.TrySplit(folder ?? ActivePanel.Current.CurrentPath, out var archive, out var inner);
        if (await EditRefusalAsync(archive) is not { } refusal) return (archive, inner);
        await _dialogs.ShowErrorAsync("Archive", refusal);
        return null;
    }

    /// <summary>Pourquoi cette archive ne peut pas être modifiée ; null si elle le peut.</summary>
    private static async Task<string?> EditRefusalAsync(string archive)
    {
        if (!ArchiveEditor.CanEdit(archive))
            return L.T("Ce format reste en lecture seule (rar, gz, bz2, xz). Formats modifiables : zip, 7z, tar, tar.gz.\nTu peux voir (F3), extraire (F5) et tester.");
        try { return await Task.Run(() => ArchiveEditor.EncryptedRefusal(archive)); }
        catch (IOException ex) { return ex.Message; }
    }

    /// <summary>Demande le mot de passe de l'archive s'il en faut un et qu'il n'est pas déjà connu. Faux si annulé.</summary>
    private async Task<bool> UnlockArchiveAsync(string archive)
    {
        if (!await Task.Run(() => ArchivePasswords.NeedsPassword(archive))) return true;
        for (bool wrong = false; ; wrong = true)
        {
            if (await _dialogs.AskArchivePasswordAsync(Path.GetFileName(archive), wrong) is not { } password) return false;
            if (await Task.Run(() => ArchivePasswords.Check(archive, password)))
            {
                ArchivePasswords.Remember(archive, password);
                return true;
            }
        }
    }

    /// <summary>L'archive est réécrite en entier : deux modifications de la même archive ne tournent jamais ensemble.</summary>
    private void StartArchiveEdit(string archive, string title, ArchiveEdits edits)
    {
        Operations.Start(new OperationJobViewModel(title, transfer: true, async (progress, ct, pause) =>
        {
            var result = await ArchiveEditor.ApplyAsync(archive, edits, progress, ct, pause);
            if (result.SourcesLeft > 0) throw new FileOperationException(L.F("Archive à jour, mais {0} élément(s) n'ont pas pu être retirés du PC.", result.SourcesLeft));
        }) { ExclusiveKey = archive });
    }

    private async Task<bool> ArchiveRenameAsync()
    {
        if (!ActivePanel.Current.IsInsideArchive) return false;
        if (ActivePanel.Current.CurrentItem is not { IsParentLink: false } item || await EditableArchiveAsync() is not { } target) return true;
        var newName = (await AskNewNameAsync(item.Name, item.Name.Length))?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == item.Name) return true;
        if (ArchiveEditor.CheckName(newName) is { } problem) { await _dialogs.ShowErrorAsync(L.T("Renommer"), problem); return true; }

        var key = ArchivePath.TrySplit(item.FullPath, out _, out var inner) ? inner : string.Empty;
        var parent = ArchiveIndex.ParentOf(key);
        var renamed = (parent.Length > 0 ? parent + "/" : string.Empty) + newName;
        StartArchiveEdit(target.Archive, L.F("Renommage de « {0} » dans {1}", item.Name, Path.GetFileName(target.Archive)),
            new ArchiveEdits { Rename = new Dictionary<string, string> { [key] = renamed } });
        return true;
    }

    private async Task<bool> ArchiveMakeDirectoryAsync()
    {
        if (!ActivePanel.Current.IsInsideArchive) return false;
        if (await EditableArchiveAsync() is not { } target) return true;
        var name = (await _dialogs.PromptAsync(L.T("Nouveau dossier"), L.T("Nom du dossier :")))?.Trim();
        if (string.IsNullOrEmpty(name)) return true;
        if (ArchiveEditor.CheckName(name) is { } problem) { await _dialogs.ShowErrorAsync(L.T("Nouveau dossier"), problem); return true; }

        var key = (target.Inner.Length > 0 ? target.Inner + "/" : string.Empty) + name;
        StartArchiveEdit(target.Archive, L.F("Nouveau dossier « {0} » dans {1}", name, Path.GetFileName(target.Archive)), new ArchiveEdits { NewFolders = [key] });
        return true;
    }

    private async Task<bool> ArchiveDeleteAsync()
    {
        if (!ActivePanel.Current.IsInsideArchive) return false;
        var items = ActivePanel.Current.TargetEntries;
        if (items.Count == 0 || await EditableArchiveAsync() is not { } target) return true;
        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        // Pas de corbeille dans une archive : la confirmation reste, quelles que soient les Options.
        if (!await _dialogs.ConfirmAsync(L.T("Supprimer"), L.F("Supprimer DÉFINITIVEMENT {0} de {1} ?", what, Path.GetFileName(target.Archive)))) return true;

        ActivePanel.Current.MarkAll(false);
        var keys = items.Select(i => ArchivePath.TrySplit(i.FullPath, out _, out var inner) ? inner : string.Empty).Where(k => k.Length > 0).ToList();
        StartArchiveEdit(target.Archive, L.F("Suppression de {0} dans {1}", what, Path.GetFileName(target.Archive)), new ArchiveEdits { Delete = keys });
        return true;
    }

    /// <summary>
    /// Entrée sur une archive d'un serveur : elle est téléchargée une fois, avec sa progression, puis le panneau l'ouvre comme une archive du PC.
    /// Si le panneau a changé de dossier entre-temps, il n'est pas déplacé.
    /// </summary>
    private Task OpenRemoteArchiveAsync(FileEntry entry)
    {
        if (_remote?.FindForPath(entry.FullPath) is not { } session) return Task.CompletedTask;
        var tab = ActivePanel.Current;
        var folder = tab.CurrentPath;
        var onServer = RemotePart(entry.FullPath);
        Operations.Start(new OperationJobViewModel(L.F("Ouverture de {0} depuis {1}", entry.Name, session.Profile.Name), transfer: true, async (progress, ct, pause) =>
        {
            var copy = await Task.Run(() => ServerArchives.OpenAsync(session.OpenWorkerAsync, onServer, folder, progress, ct, pause), ct);
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try { if (tab.CurrentPath == folder) await tab.NavigateAsync(copy.LocalPath); }
                finally { copy.IsOpening = false; }
            });
        }));
        return Task.CompletedTask;
    }

    /// <summary>Une copie sans onglet n'a plus de raison d'être. Pendant une opération, on attend : elle peut être en train de la lire.</summary>
    private void CloseUnusedServerArchives()
    {
        var copies = ServerArchives.All;
        if (copies.Count == 0 || Operations.Jobs.Any(j => j.IsActive)) return;
        var shown = Left.Tabs.Concat(Right.Tabs).Select(t => ServerArchives.Find(t.CurrentPath)).ToHashSet();
        foreach (var copy in copies.Where(c => !c.IsOpening && !shown.Contains(c))) ServerArchives.Close(copy);
    }

    /// <summary>F5 / F6 vers une archive.</summary>
    private async Task AddToArchiveAsync(string archive, string inner, IReadOnlyList<FileEntry> items, bool move, ConflictPolicy? chosenPolicy, RemoteSession? source = null)
    {
        if (await EditRefusalAsync(archive) is { } refusal)
        {
            await _dialogs.ShowErrorAsync("Archive", refusal);
            return;
        }
        if (!await UnlockArchiveAsync(archive)) return;
        var policy = chosenPolicy ?? ConflictPolicy.Overwrite;
        if (chosenPolicy is null)
        {
            List<string> conflicts;
            try
            {
                var names = ArchiveIndex.Load(archive).ChildrenOf(inner).Select(i => i.Key[(i.Key.LastIndexOf('/') + 1)..]).ToHashSet(StringComparer.Ordinal);
                conflicts = items.Where(i => names.Contains(i.Name)).Select(i => ArchivePath.Combine(archive, (inner.Length > 0 ? inner + "/" : string.Empty) + i.Name)).ToList();
            }
            catch (IOException ex)
            {
                await _dialogs.ShowErrorAsync("Archive", ex.Message);
                return;
            }
            if (conflicts.Count > 0)
            {
                if (await _dialogs.AskConflictAsync(conflicts) is not { } chosen) return;
                policy = chosen;
            }
        }
        ActivePanel.Current.MarkAll(false);
        var what = items.Count == 1 ? $"« {items[0].Name} »" : L.F("{0} éléments", items.Count);
        var title = L.F("{0} de {1} dans {2}", (move ? L.T("Déplacement") : L.T("Ajout")), what, Path.GetFileName(archive));
        if (source is not null)
        {
            var remoteItems = items.Select(ToRemoteItem).ToList();
            Operations.Start(new OperationJobViewModel(title, transfer: true, (progress, ct, pause) =>
                Task.Run(() => RemoteArchives.AddFromServerAsync(source, remoteItems, archive, inner, policy, move, progress, ct, pause), ct)) { ExclusiveKey = archive });
            return;
        }
        StartArchiveEdit(archive, title, new ArchiveEdits
        {
            Add = items.Select(i => new ArchiveAddition(i.FullPath, inner)).ToList(),
            Policy = policy,
            MoveSources = move,
        });
    }
}
