using Microsoft.Extensions.Logging;

namespace Logship.Core;

/// <summary>
/// One log line captured as plain, immutable data. It is created on the thread that logged,
/// waits in a sink's queue, and is read later by that sink's worker thread, so nothing
/// about it may change after creation.
/// </summary>
/// <param name="Timestamp">When the entry was logged (UTC).</param>
/// <param name="Level">Severity.</param>
/// <param name="Category">The logger category, usually the full type name of the caller.</param>
/// <param name="Message">The fully formatted message text.</param>
/// <param name="Exception">The exception rendered with <c>ToString()</c>, or null when there was none.
/// It is captured as text so the entry holds no references to live objects.</param>
internal sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception);
