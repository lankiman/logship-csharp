namespace Logship.Options;

/// <summary>The text format of lines written to log files.</summary>
public enum LogFileFormat
{
    /// <summary>One JSON object per line. Easy to search with tools and to ship elsewhere later.</summary>
    Json,

    /// <summary>Human-readable text. An exception is written on the lines after its entry.</summary>
    Text
}
