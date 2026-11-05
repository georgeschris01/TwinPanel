// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.App.Services;
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Terminal;

namespace TwinPanel.App.ViewModels;

/// <summary>Une commande de la liste.</summary>
public sealed class TerminalCommandRow(SavedTerminalCommand command)
{
    public SavedTerminalCommand Command { get; } = command;
    public string Name => Command.Name;
    /// <summary>Une commande secrète n'est jamais affichée.</summary>
    public string Text => Command.Secret ? "••••••••" : Command.Command;
    public bool IsSecret => Command.Secret;
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
    private readonly Func<SecretVault?> _vault;
    private readonly Func<SecretVault, Task<bool>> _unlock;
    private SavedTerminalCommand? _editing;

    public TerminalCommandsViewModel(TerminalPanelViewModel panel, IDialogService dialogs, Func<TerminalSettings> settings, Action save, Action<string> status,
                                     Func<SecretVault?> vault, Func<SecretVault, Task<bool>> unlock)
    {
        _panel = panel;
        _dialogs = dialogs;
        _settings = settings;
        _save = save;
        _status = status;
        _vault = vault;
        _unlock = unlock;
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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditPasswordChar))] private bool _editSecret;
    [ObservableProperty] private bool _editReveal;

    /// <summary>Texte d'une commande secrète caché pendant qu'on le tape (sauf « Afficher »).</summary>
    public char EditPasswordChar => EditSecret ? '•' : '\0';

    partial void OnEditSecretChanged(bool value)
    {
        if (!value) EditReveal = false;
    }

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
        EditSecret = false;
        EditReveal = false;
        OpenForm();
    }

    [RelayCommand]
    private async Task StartEdit(TerminalCommandRow? row)
    {
        if (row is null) return;
        // Une commande secrète : son texte est lu dans le coffre (déverrouillé si besoin). Perdu : on le retape.
        string text;
        if (!row.Command.Secret) text = row.Command.Command;
        else if (await OpenVaultAsync() is null) return;
        else text = await ReadTextAsync(row.Command) ?? string.Empty;
        _editing = row.Command;
        EditName = row.Command.Name;
        EditCommand = text;
        EditAskFirst = row.Command.AskFirst;
        EditServerOnly = row.Command.ServerId is not null;
        EditSecret = row.Command.Secret;
        EditReveal = false;
        OpenForm();
    }

    /// <summary>Le coffre ouvert ; null s'il n'est pas disponible ou que l'utilisateur renonce.</summary>
    private async Task<SecretVault?> OpenVaultAsync()
    {
        if (_vault() is not { } vault)
        {
            _status(L.T("Le coffre des mots de passe n'est pas disponible."));
            return null;
        }
        return vault.IsUnlocked || await _unlock(vault) ? vault : null;
    }

    /// <summary>Texte de la commande ; null (avec un message) s'il n'a pas pu être lu.</summary>
    private async Task<string?> ReadTextAsync(SavedTerminalCommand command)
    {
        if (!command.Secret) return command.Command;
        if (await OpenVaultAsync() is not { } vault) return null;
        try
        {
            if (TerminalCommandSecrets.Text(command, vault) is { } text) return text;
            _status(L.F("Le texte de « {0} » n'est plus dans le coffre : modifie la commande pour le retaper.", command.Name));
            return null;
        }
        catch (Exception ex) when (ex is VaultLockedException or VaultKeyringException or InvalidOperationException
                                       or System.Security.Cryptography.CryptographicException or FormatException)
        {
            _status(ex.Message);
            return null;
        }
    }

    private void OpenForm()
    {
        IsEditing = true;
        OnPropertyChanged(nameof(CanLimitToServer));
        OnPropertyChanged(nameof(ServerOnlyLabel));
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditCommand = string.Empty;
        EditReveal = false;
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        var text = EditCommand.TrimEnd();
        if (text.Trim().Length == 0)
        {
            _status(L.T("Écris d'abord la commande."));
            return;
        }
        if (EditSecret && EditName.Trim().Length == 0)
        {
            // Sans nom, la commande elle-même servirait de nom : un mot de passe s'afficherait.
            _status(L.T("Donne un nom à une commande secrète."));
            return;
        }
        var command = _editing ?? new SavedTerminalCommand();
        SecretVault? vault = null;
        if ((EditSecret || command.Secret) && (vault = await OpenVaultAsync()) is null) return;
        try { TerminalCommandSecrets.Store(command, text, EditSecret, vault); }
        catch (Exception ex) when (ex is VaultLockedException or VaultKeyringException or InvalidOperationException
                                       or System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
        {
            _status(ex.Message);
            return;
        }
        if (_editing is null) _settings().SavedCommands.Add(command);
        // Sans nom, la commande elle-même sert de nom.
        command.Name = EditName.Trim().Length > 0 ? EditName.Trim() : text.Split('\n')[0].Trim();
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
        EditCommand = string.Empty; // le secret ne reste pas dans le formulaire
        EditReveal = false;
        _save();
        Refresh();
    }

    [RelayCommand]
    private async Task Delete(TerminalCommandRow? row)
    {
        if (row is null || !await _dialogs.ConfirmAsync(L.T("Supprimer la commande"), L.F("Supprimer « {0} » ?", row.Name))) return;
        _settings().SavedCommands.Remove(row.Command);
        TerminalCommandSecrets.Forget(row.Command, _vault());
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
        if (row.Command.AskFirst && !await _dialogs.ConfirmAsync(L.T("Lancer la commande"),
                L.F("Lancer cette commande ?\n\n{0}", row.IsSecret ? row.Name : row.Text))) return;
        if (await ReadTextAsync(row.Command) is not { } text) return;
        bool bracketed;
        lock (session.Gate) bracketed = session.Screen.BracketedPaste;
        session.Send(TerminalCommands.Input(text, bracketed));
        _panel.RequestFocus();
    }

    [RelayCommand]
    private async Task CopyText(TerminalCommandRow? row)
    {
        if (row is not null && await ReadTextAsync(row.Command) is { } text) _panel.RequestCopy(text);
    }
}
