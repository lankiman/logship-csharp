namespace Logship.Internal;

/// <summary>
/// How Logship reports its own problems (a bad config, an unreachable Loki, a full disk).
/// It writes straight to the error stream and never through <c>ILogger</c>, because a failure
/// reported through the logger Logship provides could fail again and loop forever.
/// </summary>
internal static class SelfLog
{
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} ({ex.GetType().Name}: {ex.Message})");

    private static void Write(string level, string message)
    {
        try
        {
            Console.Error.WriteLine($"[logship] {level} {message}");
        }
        catch
        {
            // The package's own error path must never throw.
        }
    }
}
