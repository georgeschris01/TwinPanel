// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core.Terminal;

namespace TwinPanel.App.ViewModels;

/// <summary>Un onglet du panneau : un terminal, sur ce PC ou sur un serveur.</summary>
public sealed partial class TerminalTabViewModel(string name) : ObservableObject
{
    private const int MaxTitle = 32;
    private const int HistoryLines = 10_000;

    [ObservableProperty] private string _title = name;
    [ObservableProperty] private TerminalSession? _session;

    /// <summary>Rouvre le même programme ou le même serveur (Dupliquer).</summary>
    public Func<Task>? Reopen { get; set; }

    /// <summary>Serveur de ce terminal ; nul : terminal du PC.</summary>
    public Guid? ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Annule une connexion en cours si l'onglet est fermé avant la fin.</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>La connexion est finie sans problème (« exit ») : l'onglet n'a plus de raison d'être.</summary>
    public event Action? Ended;

    public void Start(ITerminalConnection connection, int columns, int rows)
    {
        var session = new TerminalSession(connection, columns, rows, HistoryLines);
        session.TitleChanged += () => Dispatcher.UIThread.Post(() => UpdateTitle(session));
        session.Closed += reason => Dispatcher.UIThread.Post(() =>
        {
            if (reason is null) Ended?.Invoke();
            else session.ShowMessage(L.F("Connexion terminée : {0}", reason));
        });
        Session = session;
        try { session.Start(); }
        catch
        {
            Session = null;
            session.Dispose();
            throw;
        }
    }

    private void UpdateTitle(TerminalSession session)
    {
        string title;
        lock (session.Gate) title = session.Screen.Title;
        // Windows met le chemin du programme comme titre : le nom du dossier dit bien plus.
        if (title.Length == 0 || title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        Title = title.Length > MaxTitle ? "…" + title[^(MaxTitle - 1)..] : title;
    }

    public void Close()
    {
        Cancellation.Cancel();
        var session = Session;
        Session = null;
        session?.Dispose();
        Cancellation.Dispose();
    }
}

/// <summary>Le panneau du bas : ses onglets, et s'il est affiché.</summary>
public sealed partial class TerminalPanelViewModel : ObservableObject
{
    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    [ObservableProperty] private TerminalTabViewModel? _selected;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private double _fontSize = 13;

    /// <summary>Le clavier est dans le terminal.</summary>
    public bool HasFocus { get; set; }

    /// <summary>Taille à donner au prochain terminal : celle de l'écran actuel.</summary>
    public int Columns { get; set; } = 120;
    public int Rows { get; set; } = 24;

    public event Action? FocusTerminalRequested;
    public event Action? FocusFilesRequested;
    public event Action<string>? CopyRequested;

    public void RequestCopy(string text) => CopyRequested?.Invoke(text);

    public TerminalTabViewModel Add(string name)
    {
        var tab = new TerminalTabViewModel(name);
        tab.Ended += () => CloseTab(tab);
        Tabs.Add(tab);
        Selected = tab;
        Show();
        return tab;
    }

    public void Show()
    {
        IsOpen = true;
        FocusTerminalRequested?.Invoke();
    }

    public void RequestFocus() => FocusTerminalRequested?.Invoke();

    [RelayCommand]
    public void Hide()
    {
        IsOpen = false;
        HasFocus = false;
        FocusFilesRequested?.Invoke();
    }

    [RelayCommand]
    public void CloseTab(TerminalTabViewModel? tab)
    {
        if (tab is null) return;
        int index = Tabs.IndexOf(tab);
        if (index < 0) return;
        // La liste vide sa sélection dès la suppression : on regarde avant.
        bool wasSelected = Selected == tab;
        Tabs.RemoveAt(index);
        tab.Close();
        if (Tabs.Count == 0)
        {
            Selected = null;
            Hide();
            return;
        }
        if (wasSelected) Selected = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    partial void OnSelectedChanged(TerminalTabViewModel? value)
    {
        if (value is not null && IsOpen) FocusTerminalRequested?.Invoke();
    }

    /// <summary>En quittant, ou quand le terminal intégré est désactivé.</summary>
    public void CloseAll()
    {
        foreach (var tab in Tabs.ToList()) tab.Close();
        Tabs.Clear();
        Selected = null;
        IsOpen = false;
        HasFocus = false;
    }
}
