using Microsoft.Extensions.Logging;

namespace logship.options;

public sealed class LokiSinkOptions
{
        public required string Url { get; set; }
        public required string AppLabel { get; set; }
        public required string JobLabel { get; set; }
        public string? Environment { get; set; }
        public string? OrgId { get; set; }
        
        public LokiAuthType AuthType { get; set; } = LokiAuthType.None;
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Token { get; set; }

        public LogLevel MinLevel { get; set; } = LogLevel.Information;
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);
        public int BatchSize { get; set; } = 500;
        public int BufferCap { get; set; } = 5000;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
        public int MaxRetries { get; set; } = 2;
        public TimeSpan RetryBackoff { get; set; } = TimeSpan.FromMilliseconds(500);
}