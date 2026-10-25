namespace Logship.Options;

/// <summary>
/// The destinations for one named logger. Loggers whose category matches the target's name
/// use these instead of the defaults, so one app can ship different parts of itself to different places.
/// </summary>
public sealed class LogshipTargetOptions
{
    /// <summary>Loki settings for this target. When set, they replace the default Loki settings as a whole.</summary>
    public LokiSinkOptions? Loki { get; set; }

    /// <summary>File settings for this target. When set, they replace the default file settings as a whole.</summary>
    public FileSinkOptions? File { get; set; }

    /// <summary>
    /// When true (the default), a sink this target does not set is taken from the defaults.
    /// When false, the target uses only what it sets itself.
    /// </summary>
    public bool InheritDefaults { get; set; } = true;
}
