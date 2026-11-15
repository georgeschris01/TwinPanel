// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;

namespace TwinPanel.Core.Trash;

/// <summary>Un élément de la corbeille : son nom et sa place d'origine, et les deux fichiers qui le gardent.</summary>
/// <param name="DataPath">Le fichier ou dossier lui-même, sous un nom interne (« $R… »).</param>
/// <param name="InfoPath">La fiche (« $I… ») : chemin d'origine, taille, date de suppression.</param>
public sealed record RecycleItem(string Name, string OriginalPath, DateTime Deleted, long Size, bool IsDirectory, string DataPath, string InfoPath)
{
    public string OriginalFolder => Path.GetDirectoryName(OriginalPath) ?? string.Empty;
}

/// <summary>
/// Corbeille de Windows, lue directement : chaque disque a un dossier caché « $Recycle.Bin », avec un sous-dossier
/// par compte. Pour chaque élément supprimé, Windows y range l'élément (« $R… ») et sa fiche (« $I… »).
/// Linux (règle commune des bureaux, freedesktop.org) : la corbeille du dossier personnel, l'élément dans « files »
/// et sa fiche « nom.trashinfo » dans « info ». Seule la corbeille du compte qui utilise TwinPanel est lue.
/// </summary>
public static class RecycleBin
{
    /// <summary>Chemin interne du panneau qui montre la corbeille (jamais traduit, jamais montré tel quel).</summary>
    public const string PanelPath = "trash:";

    public static bool IsPanelPath(string? path) =>
        path is not null && string.Equals(path.TrimEnd('/', '\\'), PanelPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>La corbeille est disponible sur ce système.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>
    /// Lit une fiche « $I » : version 1 (Windows Vista à 8 : chemin de 260 caractères) ou 2 (Windows 10 et 11 : longueur, puis chemin).
    /// null si la fiche est abîmée ou inconnue.
    /// </summary>
    public static (string OriginalPath, long Size, DateTime DeletedUtc)? ParseInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24) return null;
        long version = BinaryPrimitives.ReadInt64LittleEndian(data);
        long size = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(data[16..]);
        string path;
        if (version == 1)
        {
            path = Utf16UntilNull(data[24..Math.Min(data.Length, 24 + 520)]);
        }
        else if (version == 2)
        {
            if (data.Length < 28) return null;
            int chars = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
            if (chars <= 0 || 28L + chars * 2L > data.Length) return null;
            path = Utf16UntilNull(data.Slice(28, chars * 2));
        }
        else return null;
        if (path.Length == 0) return null;

        DateTime deleted;
        try { deleted = DateTime.FromFileTimeUtc(fileTime); }
        catch (ArgumentOutOfRangeException) { deleted = DateTime.MinValue; }
        return (path, Math.Max(0, size), deleted);
    }

    private static string Utf16UntilNull(ReadOnlySpan<byte> bytes)
    {
        var text = Encoding.Unicode.GetString(bytes[..(bytes.Length & ~1)]);
        int end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }

    /// <summary>Éléments de ces dossiers de corbeille. Une fiche sans son élément (ou l'inverse) est ignorée.</summary>
    public static IReadOnlyList<RecycleItem> ListFolders(IEnumerable<string> binFolders, CancellationToken ct = default)
    {
        var list = new List<RecycleItem>();
        foreach (var bin in binFolders)
        {
            List<string> infos;
            try { infos = Directory.EnumerateFiles(bin, "$I*").ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var info in infos)
            {
                ct.ThrowIfCancellationRequested();
                if (ItemOf(info) is { } item) list.Add(item);
            }
        }
        return list;
    }

    /// <summary>L'élément décrit par cette fiche « $I », ou null (fiche illisible, élément absent).</summary>
    private static RecycleItem? ItemOf(string infoPath)
    {
        if (infoPath.EndsWith(TrashInfoExtension, StringComparison.Ordinal)) return DesktopItemOf(infoPath, null);
        var data = DataPathOf(infoPath);
        bool isDirectory = Directory.Exists(data);
        if (!isDirectory && !File.Exists(data)) return null;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(infoPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (ParseInfo(bytes) is not { } p) return null;
        var name = Path.GetFileName(p.OriginalPath.TrimEnd('\\', '/'));
        if (name.Length == 0) name = p.OriginalPath;
        var deleted = p.DeletedUtc == DateTime.MinValue ? DateTime.MinValue : p.DeletedUtc.ToLocalTime();
        return new RecycleItem(name, p.OriginalPath, deleted, p.Size, isDirectory, data, infoPath);
    }

    /// <summary>« …\$IABC123.txt » → « …\$RABC123.txt » (et l'inverse).</summary>
    public static string DataPathOf(string infoPath) => SwapPrefix(infoPath, "$R");
    public static string InfoPathOf(string dataPath) => SwapPrefix(dataPath, "$I");

    private static string SwapPrefix(string path, string prefix)
    {
        var name = Path.GetFileName(path);
        return Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, prefix + (name.Length > 2 ? name[2..] : string.Empty));
    }

    /// <summary>Retrouve l'élément d'après son fichier « $R » (celui que le panneau montre). null s'il n'est plus là.</summary>
    /// (Sans la taille des dossiers sous Linux : elle ne sert ni à restaurer, ni à supprimer, ni au nom.)
    public static RecycleItem? Find(string dataPath) =>
        DesktopInfoOf(dataPath) is { } info ? DesktopItemOf(info, NoSizes) : ItemOf(InfoPathOf(dataPath));

    private static readonly IReadOnlyDictionary<string, (long Size, long Mtime)> NoSizes = new Dictionary<string, (long, long)>();

    /// <summary>
    /// Dossier supprimé (tel que la corbeille le garde : « $R… » ou « files/nom ») qui contient ce chemin, ou ce chemin lui-même ;
    /// null si le chemin n'est pas à l'intérieur d'un élément de la corbeille.
    /// </summary>
    public static string? ItemRootOf(string? path) =>
        ItemRootOf(path, OperatingSystem.IsLinux() ? DesktopTrash.HomeTrash() : null);

    internal static string? ItemRootOf(string? path, string? desktopTrash)
    {
        if (string.IsNullOrEmpty(path) || IsPanelPath(path)) return null;
        var full = path.TrimEnd('/', '\\');
        if (desktopTrash is not null)
        {
            var files = Path.Combine(desktopTrash, "files") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(files, StringComparison.Ordinal))
            {
                // Corbeille d'un autre disque : « …/.Trash-1000/files/… » ou « …/.Trash/1000/files/… ».
                var filesPart = Path.DirectorySeparatorChar + "files" + Path.DirectorySeparatorChar;
                int pos = full.IndexOf(filesPart, StringComparison.Ordinal);
                while (pos > 0 && !IsVolumeTrash(full[..pos])) pos = full.IndexOf(filesPart, pos + 1, StringComparison.Ordinal);
                if (pos <= 0) return null;
                files = full[..(pos + filesPart.Length)];
            }
            var first = full[files.Length..].Split(Path.DirectorySeparatorChar)[0];
            return first.Length == 0 ? null : files + first;
        }
        const string marker = "\\$Recycle.Bin\\";
        int at = full.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var parts = full[(at + marker.Length)..].Split('\\');
        if (parts.Length < 2 || !parts[1].StartsWith("$R", StringComparison.OrdinalIgnoreCase)) return null;
        return full[..(at + marker.Length)] + parts[0] + "\\" + parts[1];
    }

    /// <summary>Vrai nom d'un élément de la corbeille d'après le chemin qui le garde (son nom interne si sa fiche est illisible).</summary>
    /// (Gardé en mémoire : le nom interne d'un élément ne change jamais, et le titre ou le chemin le demandent souvent.)
    public static string RealNameOf(string itemRoot)
    {
        lock (s_names)
            if (s_names.TryGetValue(itemRoot, out var known)) return known;
        if (Find(itemRoot)?.Name is not { } name) return Path.GetFileName(itemRoot);
        lock (s_names)
        {
            if (s_names.Count > 256) s_names.Clear();
            s_names[itemRoot] = name;
        }
        return name;
    }

    private static readonly Dictionary<string, string> s_names = new(StringComparer.Ordinal);

    /// <summary>« …/.Trash-1000 » ou « …/.Trash/1000 » : corbeille d'un disque autre que celui du dossier personnel.</summary>
    internal static bool IsVolumeTrash(string folder)
    {
        var name = Path.GetFileName(folder);
        if (name.StartsWith(".Trash-", StringComparison.Ordinal)) return name.Length > 7 && name[7..].All(char.IsAsciiDigit);
        return name.Length > 0 && name.All(char.IsAsciiDigit)
               && Path.GetFileName(Path.GetDirectoryName(folder) ?? string.Empty) == ".Trash";
    }

    /// <summary>Disque d'une corbeille d'un autre disque (les chemins y sont gardés par rapport à lui) ; null pour celle du dossier personnel.</summary>
    internal static string? TopDirectoryOf(string trash)
    {
        if (!IsVolumeTrash(trash)) return null;
        var parent = Path.GetDirectoryName(trash);
        return Path.GetFileName(trash).StartsWith(".Trash-", StringComparison.Ordinal) ? parent : Path.GetDirectoryName(parent ?? string.Empty);
    }

    // ---- Linux : corbeille des bureaux (freedesktop.org) ----------------------------------------

    private const string TrashInfoExtension = ".trashinfo";

    /// <summary>
    /// Lit une fiche « .trashinfo » : chemin d'origine (encodé comme une adresse) et date de suppression (heure locale).
    /// Un chemin relatif (corbeille d'un autre disque) est rattaché à topDirectory. null si la fiche est abîmée.
    /// </summary>
    public static (string OriginalPath, DateTime Deleted)? ParseTrashInfo(string text, string? topDirectory = null)
    {
        string? path = null;
        DateTime deleted = DateTime.MinValue;
        bool inSection = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith('[')) { inSection = line == "[Trash Info]"; continue; }
            if (!inSection) continue;
            if (line.StartsWith("Path=", StringComparison.Ordinal)) path = Uri.UnescapeDataString(line[5..]);
            else if (line.StartsWith("DeletionDate=", StringComparison.Ordinal)
                     && DateTime.TryParseExact(line[13..], "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
                deleted = d;
        }
        if (string.IsNullOrEmpty(path)) return null;
        if (!path.StartsWith('/') && topDirectory is not null) path = Path.Combine(topDirectory, path);
        return (path, deleted);
    }

    /// <summary>Éléments de cette corbeille (dossiers « files » et « info »). Une fiche sans son élément est ignorée.</summary>
    public static IReadOnlyList<RecycleItem> ListDesktop(string trash, CancellationToken ct = default)
    {
        var list = new List<RecycleItem>();
        List<string> infos;
        try { infos = Directory.EnumerateFiles(Path.Combine(trash, "info"), "*" + TrashInfoExtension).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        var folderSizes = DirectorySizes(trash);
        foreach (var info in infos)
        {
            ct.ThrowIfCancellationRequested();
            if (DesktopItemOf(info, folderSizes) is { } item) list.Add(item);
        }
        return list;
    }

    /// <summary>Vrai si cette corbeille contient au moins un élément (lecture rapide, pour l'icône).</summary>
    public static bool AnyInDesktop(string trash)
    {
        try
        {
            return Directory.EnumerateFiles(Path.Combine(trash, "info"), "*" + TrashInfoExtension)
                .Select(DesktopDataOf).Any(d => File.Exists(d) || Directory.Exists(d));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>« …/info/a.txt.trashinfo » → « …/files/a.txt ».</summary>
    private static string DesktopDataOf(string infoPath)
    {
        var trash = Path.GetDirectoryName(Path.GetDirectoryName(infoPath)) ?? string.Empty;
        var name = Path.GetFileName(infoPath);
        return Path.Combine(trash, "files", name[..^TrashInfoExtension.Length]);
    }

    /// <summary>« …/files/a.txt » → « …/info/a.txt.trashinfo » si cette fiche existe ; sinon null (pas une corbeille de bureau).</summary>
    private static string? DesktopInfoOf(string dataPath)
    {
        var trimmed = dataPath.TrimEnd('/', '\\');
        var folder = Path.GetDirectoryName(trimmed);
        if (folder is null || Path.GetFileName(folder) != "files") return null;
        var info = Path.Combine(Path.GetDirectoryName(folder) ?? string.Empty, "info", Path.GetFileName(trimmed) + TrashInfoExtension);
        return File.Exists(info) ? info : null;
    }

    private static RecycleItem? DesktopItemOf(string infoPath, IReadOnlyDictionary<string, (long Size, long Mtime)>? folderSizes)
    {
        var data = DesktopDataOf(infoPath);
        var trash = Path.GetDirectoryName(Path.GetDirectoryName(infoPath)) ?? string.Empty;
        bool isDirectory = Directory.Exists(data);
        if (!isDirectory && !File.Exists(data)) return null;
        string text;
        try { text = File.ReadAllText(infoPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (ParseTrashInfo(text, TopDirectoryOf(trash)) is not { } p) return null;
        var name = Path.GetFileName(p.OriginalPath.TrimEnd('/'));
        if (name.Length == 0) name = p.OriginalPath;
        long size = 0;
        try
        {
            if (!isDirectory) size = new FileInfo(data).Length;
            else if ((folderSizes ?? DirectorySizes(trash)).TryGetValue(Path.GetFileName(data), out var s)
                     && SameTime(s.Mtime, File.GetLastWriteTimeUtc(infoPath))) size = s.Size;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new RecycleItem(name, p.OriginalPath, p.Deleted, size, isDirectory, data, infoPath);
    }

    /// <summary>
    /// Tailles des dossiers notées par le bureau (« directorysizes » : taille, date de la fiche, nom encodé). Vide si absent.
    /// Une ligne n'est juste que si sa date est celle de la fiche : sinon elle parle d'un ancien dossier du même nom.
    /// </summary>
    internal static IReadOnlyDictionary<string, (long Size, long Mtime)> DirectorySizes(string trash)
    {
        var sizes = new Dictionary<string, (long, long)>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(trash, "directorysizes")))
            {
                var parts = line.Split(' ', 3);
                if (parts.Length == 3 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var mtime))
                    sizes[Uri.UnescapeDataString(parts[2])] = (size, mtime);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return sizes;
    }

    /// <summary>La date notée (en secondes, ou en millisecondes selon le bureau) est celle de la fiche.</summary>
    internal static bool SameTime(long noted, DateTime infoWrittenUtc)
    {
        long seconds = new DateTimeOffset(DateTime.SpecifyKind(infoWrittenUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return noted == seconds || noted / 1000 == seconds;
    }

    /// <summary>Dossiers de corbeille du compte actuel, sur les disques du PC et les clés USB (pas les lecteurs réseau, lents).</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<string> UserBinFolders()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) return [];
        var list = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady) continue;
                var bin = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                if (Directory.Exists(bin)) list.Add(bin);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Vrai si l'un de ces dossiers de corbeille contient au moins un élément (lecture rapide, pour l'icône).</summary>
    public static bool AnyIn(IEnumerable<string> binFolders)
    {
        foreach (var bin in binFolders)
        {
            try
            {
                if (Directory.EnumerateFiles(bin, "$I*").Any(i => File.Exists(DataPathOf(i)) || Directory.Exists(DataPathOf(i)))) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return false;
    }

    /// <summary>La corbeille du compte contient quelque chose.</summary>
    public static bool HasItems() =>
        OperatingSystem.IsWindows() ? AnyIn(UserBinFolders())
        : OperatingSystem.IsLinux() && DesktopTrash.UserTrashes().Any(AnyInDesktop);

    /// <summary>
    /// Tout le contenu de la corbeille du compte (vide si le système n'est pas pris en charge).
    /// Linux : celle du dossier personnel et celles des autres disques (clés USB…) qui appartiennent au compte.
    /// </summary>
    public static IReadOnlyList<RecycleItem> List(CancellationToken ct = default) =>
        OperatingSystem.IsWindows() ? ListFolders(UserBinFolders(), ct)
        : OperatingSystem.IsLinux() ? DesktopTrash.UserTrashes().SelectMany(t => ListDesktop(t, ct)).ToList()
        : [];

    /// <summary>
    /// Remet l'élément à sa place d'origine (son dossier est recréé au besoin).
    /// Faux, sans rien toucher, si un élément du même nom s'y trouve déjà.
    /// </summary>
    public static bool Restore(RecycleItem item)
    {
        var target = item.OriginalPath;
        if (File.Exists(target) || Directory.Exists(target)) return false;
        try
        {
            if (Path.GetDirectoryName(target) is { Length: > 0 } parent) Directory.CreateDirectory(parent);
            if (item.IsDirectory) Directory.Move(item.DataPath, target);
            else File.Move(item.DataPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileOperationException(L.F("« {0} » n'a pas pu être restauré : {1}", item.Name, ex.Message), ex);
        }
        TryDelete(item.InfoPath);
        return true;
    }

    /// <summary>Supprime définitivement l'élément et sa fiche.</summary>
    public static void DeletePermanently(RecycleItem item)
    {
        try
        {
            // L'élément est lui-même un lien (jonction, lien symbolique) : seul le lien est effacé,
            // sans rien toucher (ni attributs ni fichiers) dans le dossier qu'il vise.
            var info = new FileInfo(item.DataPath);
            if (info.Exists || Directory.Exists(item.DataPath))
            {
                FileSystemInfo self = info.Exists ? info : new DirectoryInfo(item.DataPath);
                if (self.LinkTarget is not null)
                {
                    if (self is DirectoryInfo d) d.Delete(recursive: false); else self.Delete();
                    TryDelete(item.InfoPath);
                    return;
                }
            }
            if (item.IsDirectory && Directory.Exists(item.DataPath))
            {
                // Fichiers en lecture seule : Windows refuserait de les effacer.
                // (les liens vers d'autres dossiers ne sont pas suivis : seul le lien est effacé)
                var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
                foreach (var f in Directory.EnumerateFiles(item.DataPath, "*", options))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(item.DataPath, recursive: true);
            }
            else if (File.Exists(item.DataPath))
            {
                File.SetAttributes(item.DataPath, FileAttributes.Normal);
                File.Delete(item.DataPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileOperationException(L.F("« {0} » n'a pas pu être supprimé : {1}", item.Name, ex.Message), ex);
        }
        TryDelete(item.InfoPath);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Le panneau montre l'élément sous son vrai nom, avec son dossier d'origine ; son chemin reste celui du fichier « $R ».</summary>
    public static FileEntry ToEntry(RecycleItem item)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(item.DataPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { attributes = item.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal; }
        return new FileEntry(item.Name, item.DataPath, item.IsDirectory, item.Size, item.Deleted, attributes) { OriginalFolder = item.OriginalFolder };
    }
}
