using System.Collections.Concurrent;
using Logship.Internal;
using Logship.Options;
using Logship.Sinks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Logship.Core;

/// <summary>
/// Plugs Logship into .NET logging. It is a singleton for the life of the app: it creates one logger per
/// category (the C# counterpart of a "named logger"), asks the <see cref="SinkRegistry"/> for the sinks each
/// category needs, and disposes everything at shutdown.
/// </summary>
[ProviderAlias("Logship")]
internal sealed class LogshipLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(15);

    private readonly LogshipOptions _options;
    private readonly SinkRegistry _registry;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<object, bool> _validated = new(ReferenceEqualityComparer.Instance);
    private int _disposed;

    public LogshipLoggerProvider(LogshipOptions options, SinkRegistry? registry = null, TimeProvider? time = null)
    {
        _options = options;
        _time = time ?? TimeProvider.System;
        _registry = registry ?? new SinkRegistry(_time);
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName ?? string.Empty, Build);

    private ILogger Build(string category)
    {
        var (loki, file) = TargetResolver.Resolve(category, _options);
        var bindings = new List<SinkBinding>(2);

        if (loki is not null && IsValid(loki, "Loki", LokiOptionsValidator.Validate(loki)))
        {
            bindings.Add(new SinkBinding(_registry.GetLoki(loki), loki.MinLevel));
        }

        if (file is not null && IsValid(file, "File", FileOptionsValidator.Validate(file)))
        {
            bindings.Add(new SinkBinding(_registry.GetFile(file), file.MinLevel));
        }

        return bindings.Count == 0
            ? NullLogger.Instance
            : new LogshipLogger(category, bindings, _time);
    }

    // A bad config disables that one sink with a warning. It never throws into the app.
    private bool IsValid(object options, string name, List<string> errors)
    {
        return _validated.GetOrAdd(options, _ =>
        {
            if (errors.Count > 0)
            {
                SelfLog.Warn($"{name} sink disabled because its options are invalid: {string.Join(" ", errors)}");
            }

            return errors.Count == 0;
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _registry.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            // Run on the thread pool so a caller with a synchronization context cannot deadlock.
            Task.Run(() => DisposeAsync().AsTask()).Wait(DisposeWait);
        }
        catch (Exception ex)
        {
            SelfLog.Error("Failed to dispose the Logship provider.", ex);
        }
    }
}
