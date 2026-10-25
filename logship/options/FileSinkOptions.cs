using Microsoft.Extensions.Logging;

namespace Logship.Options;

/// <summary>Everything needed to write logs to rolling files.</summary>
public sealed class FileSinkOptions
{
    /// <summary>Folder for the log files. It is created if it does not exist. Default: <c>logs</c>.</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>File name, containing <c>{date}</c>. Default: <c>application-{date}.log</c>.</summary>
    public string FileNamePattern { get; set; } = "application-{date}.log";

    /// <summary>
    /// .NET date format that replaces <c>{date}</c> (the date is in UTC). A new file starts when it changes.
    /// Default: <c>yyyy-MM-dd</c>.
    /// </summary>
    public string DatePattern { get; set; } = "yyyy-MM-dd";

    /// <summary>A file is closed and a new one started (<c>name.1.log</c>, <c>name.2.log</c>…) before it passes this size. Default: 20 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>Line format. Default: <see cref="LogFileFormat.Json"/>.</summary>
    public LogFileFormat Format { get; set; } = LogFileFormat.Json;

    /// <summary>Lowest level written to the file. Default: Information.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Information;

    /// <summary>How long to wait for more entries before writing what is queued. Default: 1 second.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Entries held in memory while waiting to be written. New entries are dropped past this. Default: 5000.</summary>
    public int BufferCap { get; set; } = 5000;

    internal FileSinkOptions Clone() => new()
    {
        Directory = Directory,
        FileNamePattern = FileNamePattern,
        DatePattern = DatePattern,
        MaxFileSizeBytes = MaxFileSizeBytes,
        Format = Format,
        MinLevel = MinLevel,
        FlushInterval = FlushInterval,
        BufferCap = BufferCap
    };
}
