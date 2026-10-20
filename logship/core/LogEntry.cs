using Microsoft.Extensions.Logging;

namespace logship.core;

internal sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, String Category, String Message, String? Exception );