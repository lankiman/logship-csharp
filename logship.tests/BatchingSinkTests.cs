using System.Diagnostics;
using Logship.Core;
using Logship.Sinks;

namespace Logship.Tests;

/// <summary>A sink that records what it was asked to deliver, so the engine can be tested on its own.</summary>
internal sealed class RecordingSink : BatchingSink
{
    private readonly object _gate = new();
    private readonly List<int> _batchSizes = [];
    private readonly List<string> _messages = [];
    private int _emitCalls;

    public RecordingSink(int bufferCap, int batchSize, TimeSpan flushInterval, TimeSpan? shutdownTimeout = null, bool autoStart = true)
        : base(bufferCap, batchSize, flushInterval, shutdownTimeout)
    {
        if (autoStart)
        {
            Start();
        }
    }

    /// <summary>Runs inside each delivery. Set it before calling <see cref="StartNow"/>.</summary>
    public Func<int, CancellationToken, Task>? OnEmit { get; set; }

    public void StartNow() => Start();

    public int[] BatchSizes
    {
        get
        {
            lock (_gate)
            {
                return [.. _batchSizes];
            }
        }
    }

    public string[] Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    protected override async Task EmitBatchAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct)
    {
        var call = Interlocked.Increment(ref _emitCalls);
        if (OnEmit is not null)
        {
            await OnEmit(call, ct);
        }

        lock (_gate)
        {
            _batchSizes.Add(batch.Count);
            _messages.AddRange(batch.Select(e => e.Message));
        }
    }
}

public class BatchingSinkTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task A_full_queue_refuses_entries_and_counts_them()
    {
        // Not started, so nothing drains the queue and the result is deterministic.
        var sink = new RecordingSink(bufferCap: 2, batchSize: 10, Short, autoStart: false);

        Assert.True(sink.TryEnqueue(TestData.Entry("1")));
        Assert.True(sink.TryEnqueue(TestData.Entry("2")));
        Assert.False(sink.TryEnqueue(TestData.Entry("3")));
        Assert.Equal(1, sink.DroppedCount);

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task Entries_are_delivered_in_batches_of_at_most_the_batch_size()
    {
        var sink = new RecordingSink(bufferCap: 100, batchSize: 3, Short, autoStart: false);
        for (var i = 0; i < 7; i++)
        {
            sink.TryEnqueue(TestData.Entry($"m{i}"));
        }

        sink.StartNow();
        await Wait.UntilAsync(() => sink.Messages.Length == 7);

        Assert.Equal(new[] { 3, 3, 1 }, sink.BatchSizes);
        Assert.Equal(new[] { "m0", "m1", "m2", "m3", "m4", "m5", "m6" }, sink.Messages);

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task A_partial_batch_is_delivered_once_the_flush_interval_passes()
    {
        var sink = new RecordingSink(bufferCap: 100, batchSize: 100, Short);

        sink.TryEnqueue(TestData.Entry("only"));

        await Wait.UntilAsync(() => sink.Messages.Length == 1);
        Assert.Equal(new[] { "only" }, sink.Messages);

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_delivers_everything_still_queued_without_waiting_for_the_interval()
    {
        var sink = new RecordingSink(bufferCap: 100, batchSize: 100, TimeSpan.FromSeconds(30));
        for (var i = 0; i < 5; i++)
        {
            sink.TryEnqueue(TestData.Entry($"m{i}"));
        }

        var watch = Stopwatch.StartNew();
        await sink.DisposeAsync();

        Assert.Equal(5, sink.Messages.Length);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "Dispose waited for the flush interval.");
    }

    [Fact]
    public async Task Disposing_gives_up_on_a_delivery_that_never_finishes()
    {
        var started = false;
        var sink = new RecordingSink(bufferCap: 10, batchSize: 1, Short, shutdownTimeout: TimeSpan.FromMilliseconds(200), autoStart: false)
        {
            OnEmit = async (_, ct) =>
            {
                started = true;
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        sink.StartNow();
        sink.TryEnqueue(TestData.Entry("stuck"));
        await Wait.UntilAsync(() => started);

        var watch = Stopwatch.StartNew();
        await sink.DisposeAsync();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Dispose hung.");
    }

    [Fact]
    public async Task A_failing_delivery_does_not_stop_the_sink()
    {
        var sink = new RecordingSink(bufferCap: 10, batchSize: 1, TimeSpan.FromMilliseconds(20), autoStart: false)
        {
            OnEmit = (call, _) => call == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask
        };
        sink.StartNow();

        sink.TryEnqueue(TestData.Entry("first"));
        await Wait.UntilAsync(() => sink.FailedBatchCount == 1);

        sink.TryEnqueue(TestData.Entry("second"));
        await Wait.UntilAsync(() => sink.Messages.Contains("second"));

        Assert.DoesNotContain("first", sink.Messages);

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task Entries_are_refused_after_disposal()
    {
        var sink = new RecordingSink(bufferCap: 10, batchSize: 1, Short);
        await sink.DisposeAsync();

        Assert.False(sink.TryEnqueue(TestData.Entry()));
    }

    [Fact]
    public async Task Disposing_twice_is_harmless()
    {
        var sink = new RecordingSink(bufferCap: 10, batchSize: 1, Short);

        await sink.DisposeAsync();
        await sink.DisposeAsync();
    }

    [Fact]
    public void Bad_constructor_arguments_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingSink(0, 1, Short, autoStart: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingSink(1, 0, Short, autoStart: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingSink(1, 1, TimeSpan.Zero, autoStart: false));
    }
}
