// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core;

public enum FileCategory { Parent, Folder, File, Image, Video, Audio, Archive, Document, Code, Executable }

public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Map = Build();

    public static FileCategory FromExtension(string extension) =>
        Map.TryGetValue(extension.TrimStart('.'), out var c) ? c : FileCategory.File;

    private static Dictionary<string, FileCategory> Build()
    {
        var d = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory c, params string[] exts) { foreach (var e in exts) d[e] = c; }

        Add(FileCategory.Image, "jpg", "jpeg", "png", "gif", "bmp", "webp", "tif", "tiff", "svg", "ico", "heic", "avif", "raw", "psd");
        Add(FileCategory.Video, "mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "ts", "m2ts", "flv", "mpg", "mpeg");
        Add(FileCategory.Audio, "mp3", "flac", "wav", "ogg", "m4a", "aac", "wma", "opus", "alac");
        Add(FileCategory.Archive, "zip", "7z", "rar", "tar", "gz", "bz2", "xz", "zst", "cab", "iso", "tgz");
        Add(FileCategory.Document, "pdf", "doc", "docx", "odt", "xls", "xlsx", "ods", "ppt", "pptx", "odp", "txt", "md", "rtf", "epub", "srt", "ass", "sub");
        Add(FileCategory.Code, "cs", "axaml", "xaml", "xml", "json", "yml", "yaml", "js", "ts", "py", "html", "css", "sh", "ps1", "c", "cpp", "h", "java", "go", "rs", "sql", "csproj", "sln", "ini", "toml", "props");
        Add(FileCategory.Executable, "exe", "msi", "bat", "cmd", "com", "lnk", "appx", "msix");
        return d;
    }
}
