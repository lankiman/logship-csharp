using logship.options;
using Microsoft.Extensions.Logging;

namespace Logship.Options;

public sealed class FileSinkOptions
{
    public string Directory { get; set; } = "logs";
    public string FileNamePattern { get; set; } = "application-{date}.log";
    public string DatePattern { get; set; } = "yyyy-MM-dd";
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;
    public LogFileFormat Format { get; set; } = LogFileFormat.Json;

    public LogLevel MinLevel { get; set; } = LogLevel.Information;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);
    public int BufferCap { get; set; } = 5000;
}