// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.Services;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Terminal;

namespace TwinPanel.App.ViewModels;

/// <summary>Une commande de la liste.</summary>
public sealed class TerminalCommandRow(SavedTerminalCommand command)
{
    public SavedTerminalCommand Command { get; } = command;
    public string Name => Command.Name;
    public string Text => Command.Command;
    public string Scope => Command.ServerId is null ? string.Empty : L.F("Seulement sur {0}", Command.ServerName);
    public bool HasScope => Command.ServerId is not null;
}

/// <summary>Panneau des commandes enregistrées, à droite du terminal.</summary>
public sealed partial class TerminalCommandsViewModel : ObservableObject
{
    private readonly TerminalPanelViewModel _panel;
    private readonly IDialogService _dialogs;
    private readonly Func<TerminalSettings> _settings;
    private readonly Action _save;
    private readonly Action<string> _status;
    private SavedTerminalCommand? _editing;

    public TerminalCommandsViewModel(TerminalPanelViewModel panel, IDialogService dialogs, Func<TerminalSettings> settings, Action save, Action<string> status)
    {
        _panel = panel;
        _dialogs = dialogs;
        _settings = settings;
        _save = save;
        _status = status;
        panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TerminalPanelViewModel.Selected)) Refresh();
        };
    }

    public ObservableCollection<TerminalCommandRow> Rows { get; } = [];

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editCommand = string.Empty;
    [ObservableProperty] private bool _editAskFirst;
    [ObservableProperty] private bool _editServerOnly;

    public bool IsEmpty => Rows.Count == 0;

    public bool IsOpen
    {
        get => _settings().CommandsPanelOpen;
        set
        {
            if (_settings().CommandsPanelOpen == value) return;
            _settings().CommandsPanelOpen = value;
            OnPropertyChanged();
            _save();
        }
    }

    /// <summary>Le terminal affiché est sur un serveur : la commande peut lui être réservée.</summary>
    public bool CanLimitToServer => _editing?.ServerId is not null || _panel.Selected?.ServerId is not null;

    public string ServerOnlyLabel => L.F("Seulement sur {0}", _editing?.ServerId is not null ? _editing.ServerName : _panel.Selected?.ServerName ?? string.Empty);

    /// <summary>Réaffiche la liste : le terminal affiché a changé, ou les réglages.</summary>
    public void Refresh()
    {
        Rows.Clear();
        foreach (var c in TerminalCommands.Visible(_settings().SavedCommands, _panel.Selected?.ServerId)) Rows.Add(new TerminalCommandRow(c));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsOpen));
        OnPropertyChanged(nameof(CanLimitToServer));
        OnPropertyChanged(nameof(ServerOnlyLabel));
    }

    [RelayCommand]
    private void StartAdd()
    {
        _editing = null;
        EditName = string.Empty;
        EditCommand = string.Empty;
        EditAskFirst = false;
        EditServerOnly = false;
        OpenForm();
    }

    [RelayCommand]
    private void StartEdit(TerminalCommandRow? row)
    {
        if (row is null) return;
        _editing = row.Command;
        EditName = row.Command.Name;
        EditCommand = row.Command.Command;
        EditAskFirst = row.Command.AskFirst;
        EditServerOnly = row.Command.ServerId is not null;
        OpenForm();
    }

    private void OpenForm()
    {
        IsEditing = true;
        OnPropertyChanged(nameof(CanLimitToServer));
        OnPropertyChanged(nameof(ServerOnlyLabel));
    }

    [RelayCommand]
    private void CancelEdit() => IsEditing = false;

    [RelayCommand]
    private void SaveEdit()
    {
        var text = EditCommand.TrimEnd();
        if (text.Trim().Length == 0)
        {
            _status(L.T("Écris d'abord la commande."));
            return;
        }
        var command = _editing;
        if (command is null)
        {
            command = new SavedTerminalCommand();
            _settings().SavedCommands.Add(command);
        }
        // Sans nom, la commande elle-même sert de nom.
        command.Name = EditName.Trim().Length > 0 ? EditName.Trim() : text.Split('\n')[0].Trim();
        command.Command = text;
        command.AskFirst = EditAskFirst;
        if (!EditServerOnly)
        {
            command.ServerId = null;
            command.ServerName = string.Empty;
        }
        else if (command.ServerId is null && _panel.Selected is { ServerId: { } id } tab)
        {
            command.ServerId = id;
            command.ServerName = tab.ServerName;
        }
        IsEditing = false;
        _save();
        Refresh();
    }

    [RelayCommand]
    private async Task Delete(TerminalCommandRow? row)
    {
        if (row is null || !await _dialogs.ConfirmAsync(L.T("Supprimer la commande"), L.F("Supprimer « {0} » ?", row.Name))) return;
        _settings().SavedCommands.Remove(row.Command);
        _save();
        Refresh();
    }

    [RelayCommand]
    private async Task Run(TerminalCommandRow? row)
    {
        if (row is null) return;
        if (_panel.Selected?.Session is not { IsClosed: false } session)
        {
            _status(L.T("Aucun terminal en marche pour lancer cette commande."));
            return;
        }
        if (row.Command.AskFirst && !await _dialogs.ConfirmAsync(L.T("Lancer la commande"), L.F("Lancer cette commande ?\n\n{0}", row.Text))) return;
        bool bracketed;
        lock (session.Gate) bracketed = session.Screen.BracketedPaste;
        session.Send(TerminalCommands.Input(row.Text, bracketed));
        _panel.RequestFocus();
    }

    [RelayCommand]
    private void CopyText(TerminalCommandRow? row)
    {
        if (row is not null) _panel.RequestCopy(row.Text);
    }
}
