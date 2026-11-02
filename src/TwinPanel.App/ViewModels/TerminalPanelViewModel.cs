// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TwinPanel.Core.Terminal;

namespace TwinPanel.App.ViewModels;

/// <summary>Un terminal et ses copies : elles se numérotent dans leur onglet.</summary>
public sealed class TerminalFamily
{
    private int _count;
    public int Next() => ++_count;
}

/// <summary>Un onglet du panneau : un terminal, sur ce PC ou sur un serveur.</summary>
public sealed partial class TerminalTabViewModel(string name) : ObservableObject
{
    private const int MaxTitle = 32;
    private const int HistoryLines = 10_000;

    private string _name = name;
    private int _copyNumber = 1;

    [ObservableProperty] private string _title = name;
    [ObservableProperty] private TerminalSession? _session;

    /// <summary>Rang parmi les copies : à partir de 2, il s'affiche dans l'onglet, même si le programme change le titre.</summary>
    public int CopyNumber
    {
        get => _copyNumber;
        set
        {
            _copyNumber = value;
            ShowTitle();
        }
    }

    private void ShowTitle() => Title = _copyNumber > 1 ? $"{_name} ({_copyNumber})" : _name;

    /// <summary>Rouvre le même programme ou le même serveur (Dupliquer).</summary>
    public Func<Task>? Reopen { get; set; }

    /// <summary>Relance le même programme ou le même serveur dans cet onglet (Reconnecter).</summary>
    public Func<Task>? Restart { get; set; }
    public bool IsRestarting { get; set; }

    /// <summary>Serveur de ce terminal ; nul : terminal du PC.</summary>
    public Guid? ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Annule une connexion en cours si l'onglet est fermé avant la fin.</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>La connexion est finie sans problème (« exit ») : l'onglet n'a plus de raison d'être.</summary>
    public event Action? Ended;

    /// <summary>Branche une connexion sur l'onglet. Celle d'avant n'est fermée que si la nouvelle a démarré.</summary>
    public void Start(ITerminalConnection connection, int columns, int rows)
    {
        var previous = Session;
        var session = new TerminalSession(connection, columns, rows, HistoryLines);
        session.TitleChanged += () => Dispatcher.UIThread.Post(() =>
        {
            if (Session == session) UpdateTitle(session);
        });
        session.Closed += reason => Dispatcher.UIThread.Post(() =>
        {
            // La fin d'une session remplacée ne doit pas fermer l'onglet.
            if (Session != session) return;
            if (reason is null) Ended?.Invoke();
            else session.ShowMessage(L.F("Connexion terminée : {0}", reason));
        });
        Session = session;
        try { session.Start(); }
        catch
        {
            Session = previous;
            session.Dispose();
            throw;
        }
        // Une connexion coupée peut mettre du temps à se refermer : la fenêtre n'attend pas.
        if (previous is not null) _ = Task.Run(previous.Dispose);
    }

    private void UpdateTitle(TerminalSession session)
    {
        string title;
        lock (session.Gate) title = session.Screen.Title;
        // Windows met le chemin du programme comme titre : le nom du dossier dit bien plus.
        if (title.Length == 0 || title.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        _name = title.Length > MaxTitle ? "…" + title[^(MaxTitle - 1)..] : title;
        ShowTitle();
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

/// <summary>Le panneau du bas : les onglets de chaque côté, et s'il est affiché.</summary>
public sealed partial class TerminalPanelViewModel : ObservableObject
{
    public ObservableCollection<TerminalTabViewModel> LeftTabs { get; } = [];

    /// <summary>Vide hors côte à côte.</summary>
    public ObservableCollection<TerminalTabViewModel> RightTabs { get; } = [];

    /// <summary>Tous les terminaux, des deux côtés.</summary>
    public IReadOnlyList<TerminalTabViewModel> Tabs => [.. LeftTabs, .. RightTabs];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected))]
    private TerminalTabViewModel? _leftTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected))]
    private TerminalTabViewModel? _rightTab;

    /// <summary>Le côté actif est celui de droite : il reçoit le clavier, les commandes et les nouveaux terminaux.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Selected))]
    private bool _activeIsRight;

    [ObservableProperty] private bool _isSplit;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private double _fontSize = 13;

    /// <summary>Le terminal du côté actif.</summary>
    public TerminalTabViewModel? Selected => ActiveIsRight ? RightTab : LeftTab;

    /// <summary>Le clavier est dans le terminal.</summary>
    public bool HasFocus { get; set; }

    /// <summary>Taille à donner au prochain terminal : celle de l'écran actuel.</summary>
    public int Columns { get; set; } = 120;
    public int Rows { get; set; } = 24;

    public event Action? FocusTerminalRequested;
    public event Action? FocusFilesRequested;
    public event Action<string>? CopyRequested;
    public event Action? SearchRequested;

    public void RequestCopy(string text) => CopyRequested?.Invoke(text);

    /// <summary>Nouvel onglet, du côté actif.</summary>
    public TerminalTabViewModel Add(string name)
    {
        var tab = new TerminalTabViewModel(name);
        tab.Ended += () => CloseTab(tab);
        if (IsSplit && ActiveIsRight)
        {
            RightTabs.Add(tab);
            RightTab = tab;
        }
        else
        {
            LeftTabs.Add(tab);
            LeftTab = tab;
        }
        Show();
        return tab;
    }

    public void Show()
    {
        IsOpen = true;
        FocusTerminalRequested?.Invoke();
    }

    public void RequestFocus() => FocusTerminalRequested?.Invoke();

    /// <summary>Affiche cet onglet, et rend son côté actif.</summary>
    public void SelectTab(TerminalTabViewModel tab)
    {
        if (RightTabs.Contains(tab)) RightTab = tab;
        else if (LeftTabs.Contains(tab)) LeftTab = tab;
        else return;
        ActiveIsRight = RightTabs.Contains(tab);
    }

    /// <summary>Ouvre la barre de recherche : c'est elle qui prend le clavier.</summary>
    public void OpenSearch()
    {
        IsOpen = true;
        IsSearchOpen = true;
        SearchRequested?.Invoke();
    }

    [RelayCommand]
    public void Hide()
    {
        IsOpen = false;
        IsSearchOpen = false;
        HasFocus = false;
        FocusFilesRequested?.Invoke();
    }

    /// <summary>Passe en côte à côte : un autre terminal va à droite. Faux s'il n'y en a pas : à l'appelant d'en ouvrir un.</summary>
    public bool Split()
    {
        IsSplit = true;
        ActiveIsRight = true;
        var other = LeftTabs.FirstOrDefault(t => t != LeftTab);
        if (other is null) return false;
        LeftTabs.Remove(other);
        RightTabs.Add(other);
        RightTab = other;
        return true;
    }

    /// <summary>Retour à un seul côté : tous les terminaux reviennent à gauche, aucun n'est fermé.</summary>
    public void Unsplit()
    {
        var active = Selected;
        var moved = RightTabs.ToList();
        RightTabs.Clear();
        RightTab = null;
        foreach (var tab in moved) LeftTabs.Add(tab);
        IsSplit = false;
        ActiveIsRight = false;
        LeftTab = active ?? LeftTab ?? LeftTabs.FirstOrDefault();
        RequestFocus();
    }

    /// <summary>Envoie un terminal de l'autre côté, sans le relancer. Un côté garde toujours au moins un onglet.</summary>
    public void MoveTab(TerminalTabViewModel tab)
    {
        bool right = RightTabs.Contains(tab);
        var from = right ? RightTabs : LeftTabs;
        if (!IsSplit || from.Count < 2 || !from.Contains(tab)) return;
        // La liste vide sa sélection dès la suppression : on regarde avant.
        bool wasShown = (right ? RightTab : LeftTab) == tab;
        int index = from.IndexOf(tab);
        from.RemoveAt(index);
        (right ? LeftTabs : RightTabs).Add(tab);
        if (wasShown)
        {
            var next = from[Math.Min(index, from.Count - 1)];
            if (right) RightTab = next;
            else LeftTab = next;
        }
        if (right) LeftTab = tab;
        else RightTab = tab;
    }

    /// <summary>Un clic dans un côté, ou sur son bouton +, le rend actif.</summary>
    public void ActivatePane(bool right) => ActiveIsRight = right;

    [RelayCommand]
    public void CloseTab(TerminalTabViewModel? tab)
    {
        if (tab is null) return;
        bool right = RightTabs.Contains(tab);
        var list = right ? RightTabs : LeftTabs;
        int index = list.IndexOf(tab);
        if (index < 0) return;
        // La liste vide sa sélection dès la suppression : on regarde avant.
        bool wasShown = (right ? RightTab : LeftTab) == tab;
        list.RemoveAt(index);
        tab.Close();

        if (LeftTabs.Count + RightTabs.Count == 0)
        {
            LeftTab = RightTab = null;
            IsSplit = false;
            ActiveIsRight = false;
            Hide();
            return;
        }
        if (IsSplit && list.Count == 0)
        {
            if (right) RightTab = null;
            else LeftTab = null;
            Unsplit();
            return;
        }
        if (!wasShown) return;
        var next = list[Math.Min(index, list.Count - 1)];
        if (right) RightTab = next;
        else LeftTab = next;
    }

    partial void OnLeftTabChanged(TerminalTabViewModel? value)
    {
        if (value is null) return;
        ActiveIsRight = false;
        if (IsOpen) FocusTerminalRequested?.Invoke();
    }

    partial void OnRightTabChanged(TerminalTabViewModel? value)
    {
        if (value is null) return;
        ActiveIsRight = true;
        if (IsOpen) FocusTerminalRequested?.Invoke();
    }

    /// <summary>En quittant, ou quand le terminal intégré est désactivé.</summary>
    public void CloseAll()
    {
        foreach (var tab in Tabs) tab.Close();
        LeftTabs.Clear();
        RightTabs.Clear();
        LeftTab = RightTab = null;
        IsSplit = false;
        ActiveIsRight = false;
        IsOpen = false;
        IsSearchOpen = false;
        HasFocus = false;
    }
}
