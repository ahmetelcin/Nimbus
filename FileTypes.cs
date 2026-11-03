namespace Nimbus;

// SaveExtension: Google eski biçime dışa aktaramaz; bu uzantıdaki dosyalar yeni biçimde kaydedilir.
internal sealed record FileType(string SourceMimeType, string GoogleMimeType, string? SaveExtension = null);

internal static class FileTypes
{
    private const string Slides = "application/vnd.google-apps.presentation";
    private const string Docs = "application/vnd.google-apps.document";
    private const string Sheets = "application/vnd.google-apps.spreadsheet";

    private static readonly Dictionary<string, FileType> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ppt"] = new("application/vnd.ms-powerpoint", Slides, ".pptx"),
        [".pptx"] = new("application/vnd.openxmlformats-officedocument.presentationml.presentation", Slides),
        [".odp"] = new("application/vnd.oasis.opendocument.presentation", Slides),

        [".doc"] = new("application/msword", Docs, ".docx"),
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", Docs),
        [".odt"] = new("application/vnd.oasis.opendocument.text", Docs),
        [".rtf"] = new("application/rtf", Docs),

        [".xls"] = new("application/vnd.ms-excel", Sheets, ".xlsx"),
        [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Sheets),
        [".ods"] = new("application/vnd.oasis.opendocument.spreadsheet", Sheets),
        [".csv"] = new("text/csv", Sheets),
    };

    public static IEnumerable<string> Extensions => ByExtension.Keys;

    public static FileType? Find(string path) => ByExtension.GetValueOrDefault(Path.GetExtension(path));

    public static string SavePath(string path) =>
        Find(path)?.SaveExtension is { } extension ? Path.ChangeExtension(path, extension) : path;

    public static string ExportMimeType(string path) => Find(SavePath(path))!.SourceMimeType;
}
