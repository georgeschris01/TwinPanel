// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Settings;

/// <summary>Serveur d'un onglet : la machine d'arrivée retrouve sa connexion même si son identifiant y est différent.</summary>
public sealed record TabServer(Guid Id, string Name, string Host, int Port, RemoteProtocol Protocol);

/// <summary>Onglets ouverts des deux panneaux, tels qu'ils voyagent dans une sauvegarde ou une synchronisation.</summary>
public sealed class TransferredTabs
{
    public PanelState Left { get; set; } = new();
    public PanelState Right { get; set; } = new();
    public List<TabServer> Servers { get; set; } = [];

    public int Count => Left.Tabs.Count + Right.Tabs.Count;
}

/// <summary>
/// Onglets d'une machine vers une autre. Un onglet de serveur est rouvert si sa connexion existe à l'arrivée.
/// Un onglet du PC n'a de sens que sur le même système : « D:\musique » n'existe pas sous Linux, ni « /home » sous Windows.
/// </summary>
public static class TabTransfer
{
    /// <summary>Onglets à emporter, avec les serveurs qu'ils désignent.</summary>
    public static TransferredTabs Capture(PanelState left, PanelState right, Func<Guid, ConnectionProfile?> findProfile)
    {
        var result = new TransferredTabs { Left = Copy(left), Right = Copy(right) };
        foreach (var tab in left.Tabs.Concat(right.Tabs))
        {
            if (!SavedRemoteTab.TryDecode(SavedTab.Decode(tab).Path, out var id, out _) || result.Servers.Any(s => s.Id == id)) continue;
            if (findProfile(id) is { } p) result.Servers.Add(new TabServer(p.Id, p.Name, p.Host, p.Port, p.Protocol));
        }
        return result;
    }

    /// <summary>
    /// Onglets possibles sur cette machine. Un onglet de serveur prend l'identifiant de la connexion d'ici ;
    /// le reste (dossier d'un autre système, connexion absente) est compté dans Skipped.
    /// Que le dossier existe encore est vérifié à l'ouverture.
    /// </summary>
    public static (TabSet Set, int Skipped) ForThisMachine(TransferredTabs tabs, IReadOnlyList<ConnectionProfile> profiles, bool onWindows)
    {
        int skipped = 0;
        PanelState Side(PanelState state)
        {
            var kept = new List<string>();
            int active = 0;
            for (int i = 0; i < state.Tabs.Count; i++)
            {
                var saved = SavedTab.Decode(state.Tabs[i]);
                string? path = saved.Path;
                if (SavedRemoteTab.TryDecode(saved.Path, out var id, out var remotePath))
                {
                    var server = tabs.Servers.FirstOrDefault(s => s.Id == id) ?? new TabServer(id, string.Empty, string.Empty, 0, default);
                    path = Match(server, profiles) is { } here ? SavedRemoteTab.Encode(here.Id, remotePath) : null;
                }
                else if (!FitsSystem(saved.Path, onWindows)) path = null;

                if (path is null) { skipped++; continue; }
                if (i == state.ActiveTab) active = kept.Count;
                kept.Add((saved with { Path = path }).Encode());
            }
            return new PanelState { Tabs = kept, ActiveTab = active, SortColumn = state.SortColumn, SortDescending = state.SortDescending };
        }
        var set = new TabSet { Left = Side(tabs.Left), Right = Side(tabs.Right) };
        return (set, skipped);
    }

    /// <summary>La connexion d'ici : même identifiant, sinon même serveur et même nom, sinon le seul qui a ce serveur.</summary>
    public static ConnectionProfile? Match(TabServer server, IReadOnlyList<ConnectionProfile> profiles)
    {
        if (profiles.FirstOrDefault(p => p.Id == server.Id) is { } same) return same;
        if (server.Host.Length == 0) return null;
        var sameServer = profiles.Where(p => p.Protocol == server.Protocol && p.Port == server.Port
                                             && string.Equals(p.Host, server.Host, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameServer.FirstOrDefault(p => string.Equals(p.Name, server.Name, StringComparison.CurrentCultureIgnoreCase))
               ?? (sameServer.Count == 1 ? sameServer[0] : null);
    }

    /// <summary>Chemin écrit pour ce système : « C:\… » ou « \\serveur\… » sous Windows, « /… » sous Linux.</summary>
    public static bool FitsSystem(string path, bool onWindows)
    {
        if (onWindows)
            return path.StartsWith(@"\\", StringComparison.Ordinal)
                   || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/');
        return path.StartsWith('/');
    }

    private static PanelState Copy(PanelState s) =>
        new() { Tabs = [.. s.Tabs], ActiveTab = s.ActiveTab, SortColumn = s.SortColumn, SortDescending = s.SortDescending };
}
