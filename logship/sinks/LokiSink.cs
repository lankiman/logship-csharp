using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Logship.Core;
using Logship.Formatting;
using Logship.Internal;
using Logship.Options;

namespace Logship.Sinks;

/// <summary>
/// Sends batches to Grafana Loki's push API with one long-lived <see cref="HttpClient"/>.
/// Network errors, timeouts, 408, 429 and 5xx responses are retried with doubling delays;
/// other 4xx responses mean the batch itself is bad, so it is dropped without retrying.
/// </summary>
internal sealed class LokiSink : BatchingSink
{
    private const string PushPath = "/loki/api/v1/push";
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly LokiSinkOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string? _authorization;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private long _lostEntries;

    public LokiSink(
        LokiSinkOptions options,
        HttpMessageHandler? handler = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : base(options.BufferCap, options.BatchSize, options.FlushInterval)
    {
        _options = options.Clone();
        _endpoint = BuildEndpoint(_options.Url);
        _authorization = BuildAuthorization(_options);
        _delay = delay ?? Task.Delay;

        _http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: true)
        {
            Timeout = _options.Timeout
        };

        Start();
    }

    /// <summary>Entries that were given up on after a failed send (not counting queue overflow).</summary>
    public long LostEntryCount => Interlocked.Read(ref _lostEntries);

    /// <summary>Adds <c>/loki/api/v1/push</c> to the address unless it is already there.</summary>
    internal static Uri BuildEndpoint(string url)
    {
        var uri = new Uri(url, UriKind.Absolute);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith(PushPath, StringComparison.OrdinalIgnoreCase))
        {
            path += PushPath;
        }

        return new UriBuilder(uri) { Path = path }.Uri;
    }

    private static string? BuildAuthorization(LokiSinkOptions options) => options.AuthType switch
    {
        LokiAuthType.Basic => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}")),
        LokiAuthType.Token => "Bearer " + options.Token,
        _ => null
    };

    protected override async Task EmitBatchAsync(IReadOnlyList<LogEntry> batch, CancellationToken ct)
    {
        var payload = LokiPayloadBuilder.Build(batch, _options);
        var attempts = _options.MaxRetries + 1;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (attempt > 0)
            {
                await _delay(BackoffFor(attempt - 1), ct).ConfigureAwait(false);
            }

            bool retryable;
            string reason;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                {
                    Content = new ByteArrayContent(payload)
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                if (_authorization is not null)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", _authorization);
                }

                if (!string.IsNullOrEmpty(_options.OrgId))
                {
                    request.Headers.TryAddWithoutValidation("X-Scope-OrgID", _options.OrgId);
                }

                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                retryable = IsRetryable(response.StatusCode);
                reason = $"HTTP {(int)response.StatusCode} {await ReadSnippetAsync(response, ct).ConfigureAwait(false)}".TrimEnd();
            }
            catch (HttpRequestException ex)
            {
                retryable = true;
                reason = ex.Message;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                retryable = true;
                reason = "the request timed out";
            }

            if (!retryable)
            {
                GiveUp(batch.Count, $"Loki rejected a batch ({reason}); it will not be retried.");
                return;
            }

            if (attempt == attempts - 1)
            {
                GiveUp(batch.Count, $"Could not reach Loki after {attempts} attempt(s) ({reason}); the batch was dropped.");
            }
        }
    }

    protected override ValueTask DisposeResourcesAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool IsRetryable(HttpStatusCode status)
        => (int)status >= 500 || status == HttpStatusCode.RequestTimeout || status == HttpStatusCode.TooManyRequests;

    private TimeSpan BackoffFor(int retryIndex)
    {
        var ticks = _options.RetryBackoff.Ticks << Math.Min(retryIndex, 10);
        var delay = TimeSpan.FromTicks(Math.Max(0, ticks));
        return delay > MaxBackoff ? MaxBackoff : delay;
    }

    private void GiveUp(int entryCount, string message)
    {
        Interlocked.Add(ref _lostEntries, entryCount);
        SelfLog.Error($"{message} {entryCount} entries lost.");
    }

    private static async Task<string> ReadSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            body = body.Trim();
            return body.Length <= 200 ? body : body[..200] + "...";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
