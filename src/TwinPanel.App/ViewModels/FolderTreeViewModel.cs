// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TwinPanel.App.ViewModels;

/// <summary>Un dossier de l'arborescence ; ses sous-dossiers sont lus seulement quand on l'ouvre.</summary>
public partial class FolderNodeViewModel : ObservableObject
{
    private bool _loaded;

    public FolderNodeViewModel(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
        Children.Add(Placeholder); // affiche la flèche d'ouverture sans lire le disque
    }

    private FolderNodeViewModel() { Name = "…"; FullPath = string.Empty; _loaded = true; }

    private static readonly FolderNodeViewModel Placeholder = new();

    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    [ObservableProperty] private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) EnsureLoaded();
    }

    public void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        Children.Clear();
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var dir in new DirectoryInfo(FullPath).EnumerateDirectories("*", options)
                                                            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                Children.Add(new FolderNodeViewModel(dir.Name, dir.FullName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* dossier illisible : pas d'enfants */ }
    }
}

/// <summary>Arborescence des lecteurs et dossiers (Ctrl+F8).</summary>
public partial class FolderTreeViewModel : ObservableObject
{
    public ObservableCollection<FolderNodeViewModel> Roots { get; } = [];

    [ObservableProperty] private FolderNodeViewModel? _selectedNode;

    /// <summary>Vrai pendant <see cref="Reveal"/> : la sélection vient du programme, pas d'un clic.</summary>
    public bool IsRevealing { get; private set; }

    public FolderTreeViewModel() => ReloadDrives();

    public void ReloadDrives()
    {
        Roots.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            bool ready;
            try { ready = d.IsReady; } catch (IOException) { ready = false; }
            if (!ready) continue;
            string label;
            try { label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : $"{d.Name.TrimEnd('\\')} {d.VolumeLabel}"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { label = d.Name; }
            Roots.Add(new FolderNodeViewModel(label, d.RootDirectory.FullName));
        }
    }

    /// <summary>Ouvre l'arborescence jusqu'au dossier et le sélectionne.</summary>
    public void Reveal(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        IsRevealing = true;
        try { RevealCore(path); }
        finally { IsRevealing = false; }
    }

    private void RevealCore(string path)
    {
        var root = Path.GetPathRoot(path);
        var node = Roots.FirstOrDefault(r => string.Equals(r.FullPath, root, StringComparison.OrdinalIgnoreCase));
        if (node is null) return;

        var relative = Path.GetRelativePath(node.FullPath, path);
        if (relative != ".")
        {
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                node.IsExpanded = true;
                var child = node.Children.FirstOrDefault(c => string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
                if (child is null) break; // dossier caché ou illisible : on s'arrête au parent
                node = child;
            }
        }
        SelectedNode = node;
    }
}
