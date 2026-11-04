// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using TwinPanel.App.ViewModels;
using TwinPanel.Core.Operations;

namespace TwinPanel.App.Views;

/// <summary>
/// Presse-papiers du système pour Couper / Copier / Coller des fichiers : les fichiers du PC (lisibles par l'Explorateur),
/// « couper » dit à Windows par « Preferred DropEffect », et une marque propre à TwinPanel pour retrouver sa propre liste.
/// </summary>
public sealed class FileClipboardHost(TopLevel top) : IFileClipboardHost
{
    private static readonly DataFormat<string> TokenFormat = DataFormat.CreateStringApplicationFormat("TwinPanel.PressePapiers");
    private static readonly DataFormat<byte[]> DropEffectFormat = DataFormat.CreateBytesPlatformFormat("Preferred DropEffect");

    public async Task PutAsync(string token, IReadOnlyList<string> localPaths, bool move)
    {
        if (top.Clipboard is not { } clipboard) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TokenFormat, token));
        if (localPaths.Count > 0 && top.StorageProvider is { } storage)
        {
            foreach (var path in localPaths)
            {
                IStorageItem? item = Directory.Exists(path)
                    ? await storage.TryGetFolderFromPathAsync(path)
                    : await storage.TryGetFileFromPathAsync(path);
                if (item is not null) data.Add(DataTransferItem.CreateFile(item));
            }
            if (OperatingSystem.IsWindows()) data.Add(DataTransferItem.Create(DropEffectFormat, FileClipboard.DropEffectBytes(move)));
        }
        await clipboard.SetDataAsync(data);
        await clipboard.FlushAsync(); // reste lisible par l'Explorateur, même si TwinPanel se ferme
    }

    public async Task<ClipboardFiles?> ReadAsync()
    {
        if (top.Clipboard is not { } clipboard) return null;
        using var data = await clipboard.TryGetDataAsync();
        if (data is null) return null;
        var token = await data.TryGetValueAsync(TokenFormat);
        var files = await data.TryGetFilesAsync();
        var paths = files?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? [];
        bool move = OperatingSystem.IsWindows() && FileClipboard.IsMove(await data.TryGetValueAsync(DropEffectFormat));
        return new ClipboardFiles(token, paths, move);
    }

    public async Task ClearAsync()
    {
        if (top.Clipboard is { } clipboard) await clipboard.ClearAsync();
    }
}
