using Logship.Internal;
using Logship.Sinks;
using Microsoft.Extensions.Logging;

namespace Logship.Core;

/// <summary>A sink together with the lowest level it wants to receive.</summary>
internal readonly record struct SinkBinding(ILogSink Sink, LogLevel MinLevel);

/// <summary>
/// The <see cref="ILogger"/> for one category. Its only job is to turn a log call into a
/// <see cref="LogEntry"/> and offer it to each sink. It never blocks and never throws.
/// </summary>
internal sealed class LogshipLogger : ILogger
{
    private readonly string _category;
    private readonly SinkBinding[] _sinks;
    private readonly LogLevel _lowest;
    private readonly TimeProvider _time;

    public LogshipLogger(string category, IEnumerable<SinkBinding> sinks, TimeProvider? time = null)
    {
        _category = category;
        _sinks = sinks.ToArray();
        _lowest = _sinks.Length == 0 ? LogLevel.None : _sinks.Min(s => s.MinLevel);
        _time = time ?? TimeProvider.System;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _lowest;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        try
        {
            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
            {
                return;
            }

            var entry = new LogEntry(_time.GetUtcNow(), logLevel, _category, message ?? string.Empty, exception?.ToString());

            foreach (var binding in _sinks)
            {
                if (logLevel >= binding.MinLevel)
                {
                    binding.Sink.TryEnqueue(entry);
                }
            }
        }
        catch (Exception ex)
        {
            // A logger must never take the application down.
            SelfLog.Error("Failed to record a log entry.", ex);
        }
    }
}
