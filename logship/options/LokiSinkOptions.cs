using Microsoft.Extensions.Logging;

namespace Logship.Options;

/// <summary>Everything needed to ship logs to one Grafana Loki server.</summary>
public sealed class LokiSinkOptions
{
    /// <summary>
    /// The Loki address, for example <c>http://loki:3100</c>. The push path
    /// <c>/loki/api/v1/push</c> is added when it is missing.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>Value of the <c>app_name</c> label on every stream.</summary>
    public required string AppLabel { get; set; }

    /// <summary>Value of the <c>job</c> label on every stream.</summary>
    public required string JobLabel { get; set; }

    /// <summary>Value of the optional <c>environment</c> label, such as <c>production</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Tenant id sent as the <c>X-Scope-OrgID</c> header, for multi-tenant Loki setups.</summary>
    public string? OrgId { get; set; }

    /// <summary>Which authentication to use. Default: <see cref="LokiAuthType.None"/>.</summary>
    public LokiAuthType AuthType { get; set; } = LokiAuthType.None;

    /// <summary>Basic-auth user name. Used only with <see cref="LokiAuthType.Basic"/>.</summary>
    public string? Username { get; set; }

    /// <summary>Basic-auth password. Used only with <see cref="LokiAuthType.Basic"/>.</summary>
    public string? Password { get; set; }

    /// <summary>Bearer token. Used only with <see cref="LokiAuthType.Token"/>.</summary>
    public string? Token { get; set; }

    /// <summary>Lowest level sent to Loki. Default: Information.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Information;

    /// <summary>How long to wait for a batch to fill before sending it anyway. Default: 1 second.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Entries that trigger an immediate send, and the maximum per request. Default: 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Entries held in memory while waiting to be sent. New entries are dropped past this. Default: 5000.</summary>
    public int BufferCap { get; set; } = 5000;

    /// <summary>Time allowed for each HTTP request. Default: 5 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Extra attempts after a failed send. 0 turns retries off. Default: 2.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Delay before the first retry, doubled for each later retry. Default: 500 ms.</summary>
    public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(500);

    internal LokiSinkOptions Clone() => new()
    {
        Url = Url,
        AppLabel = AppLabel,
        JobLabel = JobLabel,
        Environment = Environment,
        OrgId = OrgId,
        AuthType = AuthType,
        Username = Username,
        Password = Password,
        Token = Token,
        MinLevel = MinLevel,
        FlushInterval = FlushInterval,
        BatchSize = BatchSize,
        BufferCap = BufferCap,
        Timeout = Timeout,
        MaxRetries = MaxRetries,
        RetryBackoff = RetryBackoff
    };
}
