using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Logship.Core;
using Logship.Options;
using Microsoft.Extensions.Logging;

namespace Logship.Formatting;

/// <summary>Turns a <see cref="LogEntry"/> into one line of text. It does no I/O, which keeps it easy to test.</summary>
internal static class LineFormatter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Format(LogEntry entry, LogFileFormat format)
        => format == LogFileFormat.Json ? ToJson(entry) : ToText(entry);

    /// <summary>Level names as Grafana recognises them, which keeps colouring in Explore working.</summary>
    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "unknown"
    };

    public static string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string ToJson(LogEntry entry)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", FormatTimestamp(entry.Timestamp));
            writer.WriteString("level", LevelName(entry.Level));
            writer.WriteString("category", TextSafety.Clean(entry.Category));
            writer.WriteString("message", TextSafety.Clean(entry.Message));
            if (entry.Exception is not null)
            {
                writer.WriteString("exception", TextSafety.Clean(entry.Exception));
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string ToText(LogEntry entry)
    {
        var line = $"{FormatTimestamp(entry.Timestamp)} [{LevelName(entry.Level)}] {entry.Category}: {entry.Message}";
        return entry.Exception is null ? line : line + "\n" + entry.Exception;
    }
}
