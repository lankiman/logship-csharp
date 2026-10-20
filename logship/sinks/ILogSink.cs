using logship.core;


namespace Logship.Sinks;

internal interface ILogSink : IAsyncDisposable
{
    bool TryEnqueue(LogEntry entry);
}