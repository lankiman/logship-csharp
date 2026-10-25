using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Logship.Core;
using Logship.Options;

namespace Logship.Formatting;

/// <summary>Builds the JSON body of a request to Loki's push API from a batch of entries.</summary>
internal static class LokiPayloadBuilder
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Entries with the same level and category share one stream, because Loki identifies a
    /// stream by its labels. Within a stream, values are ordered by time.
    /// Labels: <c>job</c>, <c>app_name</c>, <c>environment</c> (when set), <c>level</c>, <c>context</c> (the category).
    /// </summary>
    public static byte[] Build(IReadOnlyList<LogEntry> entries, LokiSinkOptions options)
    {
        var streams = new Dictionary<(string Level, string Category), List<LogEntry>>();
        var order = new List<(string Level, string Category)>();

        foreach (var entry in entries)
        {
            var key = (LineFormatter.LevelName(entry.Level), entry.Category);
            if (!streams.TryGetValue(key, out var list))
            {
                list = [];
                streams[key] = list;
                order.Add(key);
            }

            list.Add(entry);
        }

        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("streams");

            foreach (var key in order)
            {
                writer.WriteStartObject();

                writer.WriteStartObject("stream");
                writer.WriteString("job", TextSafety.Clean(options.JobLabel));
                writer.WriteString("app_name", TextSafety.Clean(options.AppLabel));
                if (!string.IsNullOrEmpty(options.Environment))
                {
                    writer.WriteString("environment", TextSafety.Clean(options.Environment));
                }

                writer.WriteString("level", key.Level);
                if (!string.IsNullOrEmpty(key.Category))
                {
                    writer.WriteString("context", TextSafety.Clean(key.Category));
                }

                writer.WriteEndObject();

                writer.WriteStartArray("values");
                foreach (var entry in streams[key].OrderBy(e => e.Timestamp))
                {
                    writer.WriteStartArray();
                    writer.WriteStringValue(ToUnixNanoseconds(entry.Timestamp));
                    writer.WriteStringValue(LineFormatter.Format(entry, LogFileFormat.Json));
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Loki wants nanoseconds since 1970 as a string. Ticks are 100 ns, and ticks * 100 would overflow
    /// a long, so whole seconds and the leftover ticks are converted separately.
    /// </summary>
    public static string ToUnixNanoseconds(DateTimeOffset timestamp)
    {
        var ticks = timestamp.UtcTicks - DateTime.UnixEpoch.Ticks;
        var seconds = ticks / TimeSpan.TicksPerSecond;
        var leftoverTicks = ticks % TimeSpan.TicksPerSecond;
        var nanoseconds = seconds * 1_000_000_000L + leftoverTicks * 100L;
        return nanoseconds.ToString(CultureInfo.InvariantCulture);
    }
}
