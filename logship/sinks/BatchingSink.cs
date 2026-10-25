using System.Threading.Channels;
using Logship.Core;
using Logship.Internal;

namespace Logship.Sinks;

/// <summary>
/// The shared engine behind the file and Loki sinks.
/// <para>
/// Callers drop entries into a bounded queue (fast, never blocks). One background worker takes entries
/// out in batches and hands each batch to <see cref="EmitBatchAsync"/>, which a child class implements
/// (write to a file, POST to Loki). A slow or broken destination can therefore never slow down the app.
/// </para>
/// <para>
/// A batch is sent as soon as it holds <c>batchSize</c> entries, or once <c>flushInterval</c> has passed
/// since its first entry arrived. When the queue is full, new entries are dropped and counted.
/// </para>
/// </summary>
internal abstract class BatchingSink : ILogSink
{
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);
    private const long ReportEveryMilliseconds = 10_000;

    private readonly Channel<LogEntry> _channel;
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly TimeSpan _shutdownTimeout;
    private readonly CancellationTokenSource _stop = new();

    private Task? _worker;
    private int _started;
    private int _disposed;
    private long _dropped;
    private long _failedBatches;
    private long _nextReportAt;

    protected BatchingSink(int bufferCap, int batchSize, TimeSpan flushInterval, TimeSpan? shutdownTimeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferCap, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(flushInterval, TimeSpan.Zero);

        _batchSize = batchSize;
        _flushInterval = flushInterval;
        _shutdownTimeout = shutdownTimeout ?? DefaultShutdownTimeout;

        // Wait mode makes TryWrite return false when the queue is full instead of silently dropping.
        _channel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(bufferCap)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Entries refused because the queue was full (or the sink was shutting down).</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>Batches that <see cref="EmitBatchAsync"/> threw on.</summary>
    public long FailedBatchCount => Interlocked.Read(ref _failedBatches);

    public bool TryEnqueue(LogEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
        {
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <summary>
    /// Starts the worker. A child class calls this at the end of its own constructor, so the worker
    /// can never call <see cref="EmitBatchAsync"/> before the child has finished setting itself up.
    /// </summary>
    protected void Start()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _worker = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>
    /// Delivers one batch. The list is reused afterwards, so do not keep a reference to it.
    /// Throwing is allowed: the engine reports it and carries on with the next batch.
    /// </summary>
    protected abstract Task EmitBatchAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct);

    /// <summary>Releases the child's resources. Runs after the worker has stopped.</summary>
    protected virtual ValueTask DisposeResourcesAsync() => ValueTask.CompletedTask;

    private async Task RunAsync(CancellationToken stopToken)
    {
        var reader = _channel.Reader;
        var batch = new List<LogEntry>(_batchSize);

        try
        {
            // False only once the queue is completed and empty.
            while (await reader.WaitToReadAsync(stopToken).ConfigureAwait(false))
            {
                batch.Clear();
                Drain(reader, batch);

                if (batch.Count < _batchSize)
                {
                    await LingerAsync(reader, batch, stopToken).ConfigureAwait(false);
                }

                if (batch.Count > 0)
                {
                    await EmitSafeAsync(batch, stopToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            // Forced stop after the shutdown timeout.
        }
        catch (Exception ex)
        {
            SelfLog.Error($"{GetType().Name} worker stopped unexpectedly.", ex);
        }
    }

    private void Drain(ChannelReader<LogEntry> reader, List<LogEntry> batch)
    {
        while (batch.Count < _batchSize && reader.TryRead(out var entry))
        {
            batch.Add(entry);
        }
    }

    // Wait up to the flush interval for the batch to fill. Completing the queue ends the wait early.
    private async Task LingerAsync(ChannelReader<LogEntry> reader, List<LogEntry> batch, CancellationToken stopToken)
    {
        using var linger = CancellationTokenSource.CreateLinkedTokenSource(stopToken);
        linger.CancelAfter(_flushInterval);

        try
        {
            while (batch.Count < _batchSize && await reader.WaitToReadAsync(linger.Token).ConfigureAwait(false))
            {
                Drain(reader, batch);
            }
        }
        catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
        {
            // The flush interval elapsed. Send what we have.
        }
    }

    private async Task EmitSafeAsync(IReadOnlyList<LogEntry> batch, CancellationToken stopToken)
    {
        try
        {
            await EmitBatchAsync(batch, stopToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedBatches);
            ReportFailure(ex);
        }
    }

    // A dead disk or server would fail every batch, so report at most once every few seconds.
    private void ReportFailure(Exception ex)
    {
        var now = Environment.TickCount64;
        if (now < Interlocked.Read(ref _nextReportAt))
        {
            return;
        }

        Interlocked.Exchange(ref _nextReportAt, now + ReportEveryMilliseconds);
        SelfLog.Error($"{GetType().Name} failed to deliver a batch ({FailedBatchCount} failed so far).", ex);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // No more entries accepted; the worker finishes what is queued, then ends.
        _channel.Writer.TryComplete();

        var worker = _worker;
        if (worker is not null)
        {
            try
            {
                await worker.WaitAsync(_shutdownTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                SelfLog.Warn($"{GetType().Name} did not finish within {_shutdownTimeout.TotalSeconds:0.##}s of shutdown; remaining entries were lost.");
                _stop.Cancel();
                try
                {
                    await worker.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
                catch
                {
                    // Give up on the worker; it ends with the process.
                }
            }
            catch
            {
                // The worker reports its own errors.
            }
        }

        try
        {
            await DisposeResourcesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SelfLog.Error($"{GetType().Name} failed to release its resources.", ex);
        }

        if (worker is null || worker.IsCompleted)
        {
            _stop.Dispose();
        }
    }
}
