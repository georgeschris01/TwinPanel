// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core;
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
/// Couper / Copier / Coller des fichiers (Ctrl+X / Ctrl+C / Ctrl+V, menu du clic droit).
/// Dans TwinPanel : partout (PC, archives, serveurs), avec la même fenêtre et les mêmes transferts que F5 / F6.
/// Avec les autres programmes (Explorateur…) : les fichiers du PC seulement, dans les deux sens.
/// </summary>
public partial class MainWindowViewModel
{
    private sealed record FileClip(string Token, IReadOnlyList<FileEntry> Entries, string Folder, bool Move);

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
    }

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
