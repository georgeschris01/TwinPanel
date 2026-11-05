// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.Concurrent;

namespace TwinPanel.Core.Archives;

/// <summary>Il faut un mot de passe pour lire cette archive, ou celui qui a été donné ne convient pas.</summary>
public sealed class ArchivePasswordException(string archive, bool wrong, Exception? inner = null) : IOException(
    wrong
        ? L.F("Mot de passe incorrect pour « {0} » (ou archive endommagée).", Path.GetFileName(archive))
        : L.F("« {0} » est protégée par un mot de passe.", Path.GetFileName(archive)), inner)
{
    public string Archive { get; } = archive;
    public bool Wrong { get; } = wrong;
}

/// <summary>
/// Mots de passe des archives ouvertes : uniquement en mémoire, jusqu'à la fermeture de TwinPanel. Jamais écrits nulle part
/// (ni réglages, ni coffre, ni journaux). Les copies déchiffrées pour être affichées disparaissent avec eux.
/// </summary>
public static class ArchivePasswords
{
    private const int CheckedBytes = 1 << 20;
    private static readonly ConcurrentDictionary<string, string> Known = new(PathText.Comparer);
    private static readonly ConcurrentDictionary<string, byte> DecryptedCopies = new(PathText.Comparer);

    public static string? Get(string archivePath) => Known.TryGetValue(Path.GetFullPath(archivePath), out var password) ? password : null;

    public static void Remember(string archivePath, string password) => Known[Path.GetFullPath(archivePath)] = password;

    public static void Forget(string archivePath) => Known.TryRemove(Path.GetFullPath(archivePath), out _);

    /// <summary>Faut-il demander un mot de passe avant de lire cette archive ? Une archive illisible pour une autre raison : non, sa lecture le dira.</summary>
    public static bool NeedsPassword(string archivePath)
    {
        try { return ArchiveIndex.Load(archivePath).IsEncrypted && Get(archivePath) is null; }
        catch (ArchivePasswordException) { return true; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// Essaie un mot de passe sur le début du premier fichier chiffré : assez pour le juger, sans déchiffrer tout un gros fichier.
    /// </summary>
    public static bool Check(string archivePath, string password)
    {
        try
        {
            foreach (var entry in ArchiveReading.Read(archivePath, password))
            {
                if (entry.IsDirectory || !entry.Encrypted || entry.Size == 0) continue;
                using var data = entry.Open();
                _ = data.Read(new byte[CheckedBytes]);
                return true;
            }
            return true;
        }
        catch (Exception ex) when (ArchiveErrors.IsUnreadable(ex) || ArchiveErrors.IsPassword(ex)) { return false; }
    }

    /// <summary>Dossier temporaire qui contient une copie déchiffrée : à effacer en quittant.</summary>
    internal static void TrackDecryptedCopy(string folder) => DecryptedCopies[folder] = 0;

    /// <summary>À la fermeture de TwinPanel : oublie les mots de passe et efface les copies déchiffrées.</summary>
    public static void ForgetAll()
    {
        Known.Clear();
        foreach (var folder in DecryptedCopies.Keys)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* encore ouverte dans un programme */ }
            DecryptedCopies.TryRemove(folder, out _);
        }
    }
}
