namespace BotFactory.Api.Logging;

/// <summary>
/// Where the file log lives and how big it may grow. Shared between the Serilog
/// sink registration (Program.cs) and the /api/logs endpoints that read it back.
/// </summary>
public static class FileLog
{
    // One ~25MB window: when the file fills up, Serilog rolls to a numbered
    // sibling and deletes the old one (retainedFileCountLimit: 1), so disk
    // usage stays bounded without a cleanup job.
    public const long MaxSizeBytes = 25 * 1024 * 1024;

    public const string FileNamePrefix = "bf-api";

    public static string Directory(IConfiguration config) =>
        config["FileLogging:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "logs");

    public static string SinkPath(IConfiguration config) =>
        Path.Combine(Directory(config), FileNamePrefix + ".clef");

    /// <summary>
    /// The file currently being written. Rolling renames the active file to
    /// bf-api_001.clef etc., so pick the most recently written match.
    /// </summary>
    public static FileInfo? CurrentFile(IConfiguration config)
    {
        var dir = new DirectoryInfo(Directory(config));
        if (!dir.Exists) return null;
        return dir.GetFiles(FileNamePrefix + "*.clef")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }
}
