using Logship.Core;

namespace Logship.Sinks;

/// <summary>A place log entries go: a file, Loki, and so on.</summary>
internal interface ILogSink : IAsyncDisposable
{
    /// <summary>
    /// Offers an entry to the sink. Must be fast and must never block or throw.
    /// Returns false when the entry was refused (queue full, or the sink is shutting down).
    /// </summary>
    bool TryEnqueue(LogEntry entry);
}
