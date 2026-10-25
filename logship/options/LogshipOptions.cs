namespace Logship.Options;

/// <summary>The settings passed to <c>AddLogship</c>.</summary>
public sealed class LogshipOptions
{
    /// <summary>Default Loki settings. Null means Loki is off.</summary>
    public LokiSinkOptions? Loki { get; set; }

    /// <summary>Default file settings. Null means file logging is off.</summary>
    public FileSinkOptions? File { get; set; }

    /// <summary>
    /// Named targets. The key is a logger category (a full type name or any leading part of one,
    /// such as <c>MyApp.Payments</c>); the longest matching key wins. Matching ignores case and
    /// only matches whole dot-separated segments.
    /// </summary>
    public Dictionary<string, LogshipTargetOptions> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
