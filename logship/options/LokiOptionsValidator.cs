namespace Logship.Options;

internal static class LokiOptionsValidator
{
    public static List<string> Validate(LokiSinkOptions o)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(o.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors.Add("Url must be an absolute http or https URL.");

        if (string.IsNullOrWhiteSpace(o.AppLabel)) errors.Add("AppLabel is required.");
        if (string.IsNullOrWhiteSpace(o.JobLabel)) errors.Add("JobLabel is required.");

        switch (o.AuthType)
        {
            case LokiAuthType.None:
                if (o.Username is not null || o.Password is not null || o.Token is not null)
                    errors.Add("Credentials were given but AuthType is None.");
                break;
            case LokiAuthType.Basic:
                if (string.IsNullOrEmpty(o.Username) || string.IsNullOrEmpty(o.Password))
                    errors.Add("Basic auth needs both Username and Password.");
                if (o.Token is not null)
                    errors.Add("Basic auth cannot be combined with a Token.");
                break;
            case LokiAuthType.Token:
                if (string.IsNullOrEmpty(o.Token))
                    errors.Add("Token auth needs a Token.");
                if (o.Username is not null || o.Password is not null)
                    errors.Add("Token auth cannot be combined with Username or Password.");
                break;
        }

        if (o.FlushInterval <= TimeSpan.Zero) errors.Add("FlushInterval must be greater than zero.");
        if (o.BatchSize < 1) errors.Add("BatchSize must be at least 1.");
        if (o.BufferCap < o.BatchSize) errors.Add("BufferCap must be at least BatchSize.");
        if (o.Timeout <= TimeSpan.Zero) errors.Add("Timeout must be greater than zero.");
        if (o.MaxRetries < 0) errors.Add("MaxRetries cannot be negative.");
        if (o.RetryBackoff < TimeSpan.Zero) errors.Add("RetryBackoff cannot be negative.");

        return errors;
    }
}