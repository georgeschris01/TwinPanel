// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Controls;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

/// <summary>Résultat : le chemin du fichier choisi (« Aller au fichier »), ou null.</summary>
public partial class SearchWindow : Window
{
    public SearchWindow()
    {
        InitializeComponent();
        GoToButton.Click += (_, _) => GoTo();
        ResultsGrid.DoubleTapped += (_, _) => GoTo();
        CloseButton.Click += (_, _) => Close(null);
        Opened += (_, _) => { MaskBox.Focus(); MaskBox.SelectAll(); };
        Closing += (_, _) => (DataContext as SearchViewModel)?.Cancel();
    }

    private void GoTo()
    {
        if (DataContext is SearchViewModel { SelectedResult: { } r }) Close(r.Entry.FullPath);
    }
}
