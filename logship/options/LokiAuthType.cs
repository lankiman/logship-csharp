namespace Logship.Options;

/// <summary>How requests to Loki are authenticated.</summary>
public enum LokiAuthType
{
    /// <summary>No authentication header is sent.</summary>
    None,

    /// <summary>HTTP basic authentication using <see cref="LokiSinkOptions.Username"/> and <see cref="LokiSinkOptions.Password"/>.</summary>
    Basic,

    /// <summary>A bearer token taken from <see cref="LokiSinkOptions.Token"/>.</summary>
    Token
}
