using System.Collections.Concurrent;
using Logship.Options;

namespace Logship.Sinks;

/// <summary>
/// The sink manager. Asking for a sink with settings that equal an existing sink's returns that same
/// sink, so loggers with the same destination share one queue and one worker. It owns the sinks and
/// disposes them once, at shutdown. Loggers never dispose a sink.
/// </summary>
internal sealed class SinkRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<LokiSinkKey, Lazy<LokiSink>> _loki = new();
    private readonly ConcurrentDictionary<FileSinkKey, Lazy<FileSink>> _file = new();
    private readonly Func<HttpMessageHandler>? _lokiHandlerFactory;
    private readonly Func<TimeSpan, CancellationToken, Task>? _lokiDelay;
    private readonly TimeProvider _time;
    private int _disposed;

    public SinkRegistry(
        TimeProvider? time = null,
        Func<HttpMessageHandler>? lokiHandlerFactory = null,
        Func<TimeSpan, CancellationToken, Task>? lokiDelay = null)
    {
        _time = time ?? TimeProvider.System;
        _lokiHandlerFactory = lokiHandlerFactory;
        _lokiDelay = lokiDelay;
    }

    public ILogSink GetLoki(LokiSinkOptions options)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // Lazy makes sure only one sink is ever built per key, even when threads race.
        return _loki.GetOrAdd(
            LokiSinkKey.From(options),
            _ => new Lazy<LokiSink>(() => new LokiSink(options, _lokiHandlerFactory?.Invoke(), _lokiDelay))).Value;
    }

    public ILogSink GetFile(FileSinkOptions options)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        return _file.GetOrAdd(
            FileSinkKey.From(options),
            _ => new Lazy<FileSink>(() => new FileSink(options, _time))).Value;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var disposals = new List<Task>();
        foreach (var lazy in _loki.Values.Where(l => l.IsValueCreated))
        {
            disposals.Add(lazy.Value.DisposeAsync().AsTask());
        }

        foreach (var lazy in _file.Values.Where(l => l.IsValueCreated))
        {
            disposals.Add(lazy.Value.DisposeAsync().AsTask());
        }

        await Task.WhenAll(disposals).ConfigureAwait(false);
    }
}
