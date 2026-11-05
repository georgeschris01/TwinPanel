// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using TwinPanel.App.ViewModels;

namespace TwinPanel.App.Views;

public partial class ViewerControl : UserControl
{
    private ViewerViewModel? _vm;

    public ViewerControl()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ViewerViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        };
    }

    public void FocusFind()
    {
        FindBox.Focus();
        FindBox.SelectAll();
    }

    public void FocusContent() => LinesList.Focus();

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerViewModel.SelectedLine) && _vm is { SelectedLine: >= 0 } vm && vm.SelectedLine < vm.Lines.Count)
            Dispatcher.UIThread.Post(() => LinesList.ScrollIntoView(vm.SelectedLine));
    }
}
