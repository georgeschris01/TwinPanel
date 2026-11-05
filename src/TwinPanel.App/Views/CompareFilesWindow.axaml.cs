// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using Avalonia.Input;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

public partial class CompareFilesWindow : Window
{
    public CompareFilesWindow()
    {
        InitializeComponent();
        Opened += async (_, _) => { if (DataContext is CompareFilesViewModel vm) await vm.LoadAsync(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
