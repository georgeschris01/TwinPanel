// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinPanel.Core.Operations;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Un transfert avec un serveur, tel qu'il est gardé d'une session à l'autre. Les serveurs sont désignés par leur fiche :
/// aucun identifiant ni mot de passe ici, ils sont redemandés à la connexion comme d'habitude.
/// </summary>
public sealed class SavedTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    /// <summary>Fiche du serveur de départ ; null : les fichiers partent du PC.</summary>
    public Guid? SourceProfile { get; set; }
    /// <summary>Fiche du serveur d'arrivée ; null : les fichiers arrivent sur le PC.</summary>
    public Guid? TargetProfile { get; set; }
    public string Destination { get; set; } = string.Empty;
    public ConflictPolicy Policy { get; set; }
    public bool Move { get; set; }
    public List<string> LocalPaths { get; set; } = [];
    public List<RemoteItem> Items { get; set; } = [];
    /// <summary>Où en est un envoi ou un téléchargement ; null tant qu'il n'a pas commencé.</summary>
    public TransferPlan? Plan { get; set; }
    /// <summary>Où en sont les « cp » / « mv » faits par le serveur : ils continuent sans nous, il faut pouvoir les retrouver.</summary>
    public ServerCommandState? Commands { get; set; }

    /// <summary>Le transfert en cours, dont le plan avance : relu à chaque enregistrement.</summary>
    [JsonIgnore] public TransferResume? Live { get; set; }
    /// <summary>Déjà affiché dans le panneau de progression pendant cette session.</summary>
    [JsonIgnore] public bool HasJob { get; set; }
}

/// <summary>
/// Liste des transferts non terminés (transfers.json). Enregistrée régulièrement pour survivre à une panne, et sans jamais gêner
/// les transferts : le disque plein ou un fichier abîmé font perdre la reprise, pas le travail.
/// Si TwinPanel est ouvert deux fois, seule la première fenêtre s'en occupe : sinon la seconde reprendrait ce que la première fait.
/// </summary>
public sealed class TransferQueueStore(string filePath, TimeSpan? saveEvery = null) : IDisposable
{
    private const int SerializeAttempts = 5;
    /// <summary>Part du temps maximale passée à enregistrer : une liste énorme s'enregistre moins souvent.</summary>
    private const int SlowdownFactor = 50;

    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly object _lock = new();
    private readonly List<SavedTransfer> _items = [];
    private readonly TimeSpan _saveEvery = saveEvery ?? TimeSpan.FromSeconds(5);
    private FileStream? _ownership;
    private Timer? _timer;
    private string? _written;
    private bool _closed;

    /// <summary>Faux dans une deuxième fenêtre de TwinPanel : rien n'y est gardé.</summary>
    public bool IsOwner => _ownership is not null;

    public IReadOnlyList<SavedTransfer> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    /// <summary>Transferts gardés qui n'ont pas encore été relancés.</summary>
    public IReadOnlyList<SavedTransfer> Pending
    {
        get { lock (_lock) return _items.Where(t => !t.HasJob).ToList(); }
    }

    public void Load()
    {
        lock (_lock)
        {
            if (!TryTakeOwnership()) return;
            _items.Clear();
            if (Read() is { } saved) _items.AddRange(saved);
            _timer = new Timer(_ => Tick(), null, _saveEvery, Timeout.InfiniteTimeSpan);
        }
    }

    public void Add(SavedTransfer transfer)
    {
        lock (_lock)
        {
            if (IsOwner && !_items.Contains(transfer)) _items.Add(transfer);
        }
    }

    public void Remove(SavedTransfer? transfer)
    {
        if (transfer is null) return;
        lock (_lock) _items.Remove(transfer);
    }

    /// <summary>Écrit l'état présent, si quelque chose a changé.</summary>
    public void Save()
    {
        lock (_lock)
        {
            if (!IsOwner || _closed) return;
            foreach (var transfer in _items)
                if (transfer.Live?.Plan is { } plan) transfer.Plan = plan;
            if (Serialize() is not { } json || json == _written) return;
            try
            {
                if (_items.Count == 0) File.Delete(filePath);
                else
                {
                    var temp = filePath + ".tmp";
                    File.WriteAllText(temp, json);
                    File.Move(temp, filePath, overwrite: true);
                }
                _written = json;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* nouvel essai au prochain passage */ }
        }
    }

    public void Dispose()
    {
        Save();
        lock (_lock)
        {
            _closed = true;
            _timer?.Dispose();
            _timer = null;
            _ownership?.Dispose();
            _ownership = null;
        }
    }

    private bool TryTakeOwnership()
    {
        if (_ownership is not null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            // Fermé par Windows même après une panne : jamais de verrou oublié.
            _ownership = new FileStream(filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private List<SavedTransfer>? Read()
    {
        try { return File.Exists(filePath) ? JsonSerializer.Deserialize<List<SavedTransfer>>(File.ReadAllText(filePath), Options) : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>Les listes avancent pendant la lecture : on recommence, et au pire on attend le prochain passage.</summary>
    private string? Serialize()
    {
        for (int i = 0; i < SerializeAttempts; i++)
        {
            try { return JsonSerializer.Serialize(_items, Options); }
            catch (InvalidOperationException) { /* liste modifiée pendant la lecture */ }
        }
        return null;
    }

    private void Tick()
    {
        var clock = Stopwatch.StartNew();
        Save();
        var next = clock.Elapsed * SlowdownFactor;
        lock (_lock) _timer?.Change(next > _saveEvery ? next : _saveEvery, Timeout.InfiniteTimeSpan);
    }
}
