namespace Logship.Internal;

internal static class SelfLog
{
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} ({ex.GetType().Name}: {ex.Message})");

    private static void Write(string level, string message)
    {
        try { Console.Error.WriteLine($"[logship] {level} {message}"); }
        catch { /* the logger's own error path must never throw */ }
    }
}