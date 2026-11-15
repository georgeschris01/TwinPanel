// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Settings;

/// <summary>
/// Dossier des réglages, des connexions, du coffre et des journaux : un seul endroit pour tout TwinPanel.
/// Version installée : %APPDATA%\TwinPanel (Linux : ~/.config/TwinPanel).
/// Version portable (fichier « portable.txt » à côté du programme) : dossier « Data » à côté du programme,
/// rien n'est écrit sur le PC. Si ce dossier est en lecture seule (CD, clé verrouillée), une copie de travail
/// est faite dans le dossier temporaire et effacée à la fermeture : rien n'est gardé.
/// </summary>
public static class AppPaths
{
    /// <summary>Fichier repère de la version portable, à côté de TwinPanel.exe.</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>Fichiers repris de la version installée au premier démarrage en portable (jamais le coffre : il est lié au PC).</summary>
    public static readonly string[] ImportableFiles = ["settings.json", "history.json", "sync-profiles.json", "connections.json", "known_hosts.json"];

    private static Layout? s_current;

    /// <summary>Choix fait au démarrage (calculé à la première demande).</summary>
    public static Layout Current => s_current ??= Resolve(AppContext.BaseDirectory, InstalledDirectory);

    public static bool IsPortable => Current.Portable;
    public static string DataDirectory => Current.Data;

    /// <summary>Dossier de la version installée (%APPDATA%\TwinPanel), même en portable : pour proposer de reprendre ses réglages.</summary>
    public static string InstalledDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TwinPanel");

    /// <param name="Portable">Fichier repère trouvé à côté du programme.</param>
    /// <param name="Data">Dossier utilisé pour tout ce que TwinPanel enregistre.</param>
    /// <param name="ReadOnlyCopy">Portable sur un dossier en lecture seule : Data est une copie temporaire, effacée à la fermeture.</param>
    /// <param name="PortableData">Dossier « Data » à côté du programme (portable seulement).</param>
    public sealed record Layout(bool Portable, string Data, bool ReadOnlyCopy, string? PortableData);

    /// <summary>Choisit le dossier des réglages. Testable : le dossier du programme et celui de la version installée sont donnés.</summary>
    public static Layout Resolve(string programDirectory, string installedDirectory, string? tempRoot = null)
    {
        if (!File.Exists(Path.Combine(programDirectory, PortableMarker)))
            return new Layout(false, installedDirectory, false, null);
        var data = Path.Combine(programDirectory, "Data");
        if (CanWrite(data)) return new Layout(true, data, false, data);

        // Lecture seule : copie de travail, pour que tout fonctionne quand même (rien n'est gardé).
        var copy = Path.Combine(tempRoot ?? Path.GetTempPath(), "TwinPanel-portable-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(copy);
            if (Directory.Exists(data))
                foreach (var file in Directory.EnumerateFiles(data))
                    File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new Layout(true, copy, true, data);
    }

    /// <summary>Vrai si on peut écrire dans ce dossier (créé au besoin).</summary>
    internal static bool CanWrite(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".twinpanel-write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }

    /// <summary>
    /// Premier démarrage en portable : vrai si le dossier Data est encore vide et qu'une version installée a des réglages à reprendre.
    /// </summary>
    public static bool CanImportFromInstalled(Layout layout, string installedDirectory) =>
        layout.Portable && !layout.ReadOnlyCopy
        && !File.Exists(Path.Combine(layout.Data, "settings.json"))
        && File.Exists(Path.Combine(installedDirectory, "settings.json"));

    /// <summary>Copie les réglages de la version installée (sans le coffre des mots de passe). Retourne le nombre de fichiers copiés.</summary>
    public static int ImportFromInstalled(string installedDirectory, string data)
    {
        Directory.CreateDirectory(data);
        int n = 0;
        foreach (var name in ImportableFiles)
        {
            var source = Path.Combine(installedDirectory, name);
            if (!File.Exists(source)) continue;
            File.Copy(source, Path.Combine(data, name), overwrite: true);
            n++;
        }
        return n;
    }

    /// <summary>Fermeture : la copie de travail d'un portable en lecture seule est effacée.</summary>
    public static void CleanUp()
    {
        if (s_current is not { ReadOnlyCopy: true } layout) return;
        try { Directory.Delete(layout.Data, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Version portable : pas de protection liée au compte ni au PC, le coffre ne fonctionne qu'avec le mot de passe maître.</summary>
public sealed class PortableDataProtector : Remote.IUserDataProtector
{
    public bool IsAvailable => false;
    public byte[] Protect(byte[] data) => throw new PlatformNotSupportedException();
    public byte[] Unprotect(byte[] data) => throw new PlatformNotSupportedException();
}
