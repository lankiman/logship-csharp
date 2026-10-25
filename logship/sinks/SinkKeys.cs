using Logship.Options;

namespace Logship.Sinks;

/// <summary>
/// Identifies a Loki sink by the settings that make two sinks "the same". Records compare by value, so
/// two options objects with equal settings produce equal keys and share one sink (and one queue,
/// one worker and one HttpClient). <c>MinLevel</c> is left out on purpose: it only decides which entries
/// a logger offers to the sink, not how the sink works.
/// </summary>
internal sealed record LokiSinkKey(
    string Url,
    string AppLabel,
    string JobLabel,
    string? Environment,
    string? OrgId,
    LokiAuthType AuthType,
    string? Username,
    string? Password,
    string? Token,
    TimeSpan FlushInterval,
    int BatchSize,
    int BufferCap,
    TimeSpan Timeout,
    int MaxRetries,
    TimeSpan RetryBackoff)
{
    public static LokiSinkKey From(LokiSinkOptions o) => new(
        o.Url.TrimEnd('/'), o.AppLabel, o.JobLabel, o.Environment, o.OrgId, o.AuthType,
        o.Username, o.Password, o.Token, o.FlushInterval, o.BatchSize, o.BufferCap,
        o.Timeout, o.MaxRetries, o.RetryBackoff);

    // The generated ToString would print every field, including the password and token.
    public override string ToString() => nameof(LokiSinkKey);
}

/// <summary>
/// Identifies a file sink. Two sinks writing the same file would fight over it, so equal settings
/// must always map to one sink.
/// </summary>
internal sealed record FileSinkKey(
    string Directory,
    string FileNamePattern,
    string DatePattern,
    long MaxFileSizeBytes,
    LogFileFormat Format,
    TimeSpan FlushInterval,
    int BufferCap)
{
    public static FileSinkKey From(FileSinkOptions o)
    {
        // "/logs" and "/logs/" are the same folder, so they must make the same key.
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(o.Directory));
        if (OperatingSystem.IsWindows())
        {
            directory = directory.ToUpperInvariant();
        }

        return new FileSinkKey(directory, o.FileNamePattern, o.DatePattern, o.MaxFileSizeBytes, o.Format, o.FlushInterval, o.BufferCap);
    }

    public override string ToString() => nameof(FileSinkKey);
}
