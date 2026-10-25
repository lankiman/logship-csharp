using System.Diagnostics;
using Logship.Core;
using Microsoft.Extensions.Logging;

namespace Logship.Tests;

/// <summary>A folder that exists for the length of one test and is deleted afterwards.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "logship-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string Combine(params string[] parts) => Path.Combine([FullPath, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }
}

/// <summary>A clock the test moves by hand, so day changes can be tested without waiting for midnight.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }
}

internal static class Wait
{
    /// <summary>Polls until the condition is true. Worker threads finish "soon", not instantly.</summary>
    public static async Task UntilAsync(Func<bool> condition, int timeoutMilliseconds = 10_000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > timeoutMilliseconds)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }
}

internal static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 3, 5, 10, 0, 0, TimeSpan.Zero);

    public static LogEntry Entry(
        string message = "message",
        LogLevel level = LogLevel.Information,
        string category = "Test.Category",
        DateTimeOffset? at = null,
        string? exception = null)
        => new(at ?? Start, level, category, message, exception);

    /// <summary>Reads a file that another thread may still have open for writing.</summary>
    public static string[] ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
