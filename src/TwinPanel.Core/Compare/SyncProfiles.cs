// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinPanel.Core.Compare;

/// <summary>
/// Réglages d'une synchronisation à refaire. Un dossier de serveur est gardé sous la forme « remote:{fiche}|{chemin} »
/// (voir <see cref="Settings.SavedRemoteTab"/>), sans aucun identifiant ni mot de passe.
/// </summary>
public sealed record SyncProfile
{
    public string Name { get; init; } = string.Empty;
    public string Left { get; init; } = string.Empty;
    public string Right { get; init; } = string.Empty;
    public string NameMask { get; init; } = "*";
    public bool Recursive { get; init; } = true;
    public bool CompareContent { get; init; }
    public bool IgnoreDate { get; init; }
    public SyncDirection Direction { get; init; } = SyncDirection.Both;

    public override string ToString() => Name;
}

/// <summary>Profils enregistrés (sync-profiles.json), triés par nom ; un nom ne désigne qu'un seul profil.</summary>
public sealed class SyncProfileStore(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly List<SyncProfile> _items = [];

    public IReadOnlyList<SyncProfile> Items => _items;

    public SyncProfile? Find(string name) => _items.Find(p => IsSameName(p.Name, name));

    /// <summary>Lit la liste ; fichier absent ou abîmé : liste vide.</summary>
    public void Load()
    {
        _items.Clear();
        try
        {
            if (!File.Exists(filePath)) return;
            _items.AddRange(JsonSerializer.Deserialize<List<SyncProfile>>(File.ReadAllText(filePath), Options) ?? []);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { _items.Clear(); }
        Sort();
    }

    /// <summary>Ajoute le profil, ou remplace celui qui porte ce nom, et enregistre.</summary>
    public void Put(SyncProfile profile)
    {
        _items.RemoveAll(p => IsSameName(p.Name, profile.Name));
        _items.Add(profile);
        Sort();
        Save();
    }

    public void Remove(string name)
    {
        _items.RemoveAll(p => IsSameName(p.Name, name));
        Save();
    }

    private void Sort() => _items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

    private static bool IsSameName(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Écriture atomique ; le fichier disparaît avec le dernier profil.</summary>
    private void Save()
    {
        if (_items.Count == 0) { File.Delete(filePath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_items, Options));
        File.Move(temp, filePath, overwrite: true);
    }
}
