using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Logship.Options;
using Logship.Sinks;

namespace Logship.Tests;

internal sealed record CapturedRequest(Uri Uri, string? Authorization, string? OrgId, string? ContentType, string Body);

/// <summary>A stand-in for the network. It records each request and answers with whatever the test says.</summary>
internal sealed class FakeLokiHandler : HttpMessageHandler
{
    private readonly Func<int, Task<HttpResponseMessage>> _respond;
    private readonly ConcurrentQueue<CapturedRequest> _requests = new();
    private int _calls;

    public FakeLokiHandler(Func<int, Task<HttpResponseMessage>> respond) => _respond = respond;

    public static FakeLokiHandler Always(HttpStatusCode status)
        => new(_ => Task.FromResult(new HttpResponseMessage(status)));

    public int Calls => Volatile.Read(ref _calls);

    public CapturedRequest[] Requests => [.. _requests];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls);
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);

        _requests.Enqueue(new CapturedRequest(
            request.RequestUri!,
            request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
            request.Headers.TryGetValues("X-Scope-OrgID", out var org) ? org.Single() : null,
            request.Content.Headers.ContentType?.MediaType,
            body));

        return await _respond(call);
    }
}

public class LokiSinkTests
{
    private static LokiSinkOptions Options() => new()
    {
        Url = "http://loki:3100",
        AppLabel = "app",
        JobLabel = "job",
        FlushInterval = TimeSpan.FromMilliseconds(20),
        MaxRetries = 2
    };

    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static async Task SendOneAsync(LokiSinkOptions options, FakeLokiHandler handler, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var sink = new LokiSink(options, handler, delay ?? NoDelay);
        sink.TryEnqueue(TestData.Entry("hello"));
        await Wait.UntilAsync(() => handler.Calls >= 1);
        await sink.DisposeAsync();
    }

    [Fact]
    public async Task Posts_json_to_the_push_endpoint()
    {
        var handler = FakeLokiHandler.Always(HttpStatusCode.NoContent);

        await SendOneAsync(Options(), handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/loki/api/v1/push", request.Uri.AbsolutePath);
        Assert.Equal("application/json", request.ContentType);

        using var json = JsonDocument.Parse(request.Body);
        var line = json.RootElement.GetProperty("streams")[0].GetProperty("values")[0][1].GetString();
        Assert.Contains("hello", line);
    }

    [Theory]
    [InlineData("http://loki:3100", "/loki/api/v1/push")]
    [InlineData("http://loki:3100/", "/loki/api/v1/push")]
    [InlineData("http://loki:3100/loki/api/v1/push", "/loki/api/v1/push")]
    [InlineData("http://host/proxy", "/proxy/loki/api/v1/push")]
    public void The_push_path_is_added_only_when_missing(string url, string expectedPath)
        => Assert.Equal(expectedPath, LokiSink.BuildEndpoint(url).AbsolutePath);

    [Fact]
    public async Task Sends_no_credentials_by_default()
    {
        var handler = FakeLokiHandler.Always(HttpStatusCode.NoContent);

        await SendOneAsync(Options(), handler);

        Assert.Null(handler.Requests[0].Authorization);
        Assert.Null(handler.Requests[0].OrgId);
    }

    [Fact]
    public async Task Sends_basic_auth()
    {
        var options = Options();
        options.AuthType = LokiAuthType.Basic;
        options.Username = "u";
        options.Password = "p";
        var handler = FakeLokiHandler.Always(HttpStatusCode.NoContent);

        await SendOneAsync(options, handler);

        Assert.Equal("Basic dTpw", handler.Requests[0].Authorization); // base64("u:p")
    }

    [Fact]
    public async Task Sends_a_bearer_token()
    {
        var options = Options();
        options.AuthType = LokiAuthType.Token;
        options.Token = "abc";
        var handler = FakeLokiHandler.Always(HttpStatusCode.NoContent);

        await SendOneAsync(options, handler);

        Assert.Equal("Bearer abc", handler.Requests[0].Authorization);
    }

    [Fact]
    public async Task Sends_the_org_id()
    {
        var options = Options();
        options.OrgId = "tenant-1";
        var handler = FakeLokiHandler.Always(HttpStatusCode.NoContent);

        await SendOneAsync(options, handler);

        Assert.Equal("tenant-1", handler.Requests[0].OrgId);
    }

    [Fact]
    public async Task Retries_server_errors_until_one_succeeds()
    {
        var handler = new FakeLokiHandler(call => Task.FromResult(new HttpResponseMessage(call < 3 ? HttpStatusCode.InternalServerError : HttpStatusCode.NoContent)));
        var sink = new LokiSink(Options(), handler, NoDelay);

        sink.TryEnqueue(TestData.Entry());
        await Wait.UntilAsync(() => handler.Calls >= 3);
        await sink.DisposeAsync();

        Assert.Equal(3, handler.Calls);
        Assert.Equal(0, sink.LostEntryCount);
    }

    [Fact]
    public async Task Gives_up_after_the_last_retry_and_counts_the_lost_entries()
    {
        var options = Options();
        options.MaxRetries = 1;
        var handler = FakeLokiHandler.Always(HttpStatusCode.ServiceUnavailable);
        var sink = new LokiSink(options, handler, NoDelay);

        sink.TryEnqueue(TestData.Entry());
        await Wait.UntilAsync(() => sink.LostEntryCount == 1);
        await sink.DisposeAsync();

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Does_not_retry_a_rejected_batch()
    {
        var handler = FakeLokiHandler.Always(HttpStatusCode.BadRequest);
        var sink = new LokiSink(Options(), handler, NoDelay);

        sink.TryEnqueue(TestData.Entry());
        await Wait.UntilAsync(() => sink.LostEntryCount == 1);
        await sink.DisposeAsync();

        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Retries_rate_limits_and_request_timeouts(HttpStatusCode status)
    {
        var handler = new FakeLokiHandler(call => Task.FromResult(new HttpResponseMessage(call == 1 ? status : HttpStatusCode.NoContent)));
        var sink = new LokiSink(Options(), handler, NoDelay);

        sink.TryEnqueue(TestData.Entry());
        await Wait.UntilAsync(() => handler.Calls >= 2);
        await sink.DisposeAsync();

        Assert.Equal(2, handler.Calls);
        Assert.Equal(0, sink.LostEntryCount);
    }

    [Fact]
    public async Task Retries_network_errors_and_timeouts()
    {
        var handler = new FakeLokiHandler(call => call switch
        {
            1 => throw new HttpRequestException("connection refused"),
            2 => throw new TaskCanceledException("timed out"),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent))
        });
        var sink = new LokiSink(Options(), handler, NoDelay);

        sink.TryEnqueue(TestData.Entry());
        await Wait.UntilAsync(() => handler.Calls >= 3);
        await sink.DisposeAsync();

        Assert.Equal(3, handler.Calls);
        Assert.Equal(0, sink.LostEntryCount);
    }

    [Fact]
    public async Task Waits_longer_before_each_retry()
    {
        var options = Options();
        options.MaxRetries = 3;
        options.RetryBackoff = TimeSpan.FromMilliseconds(500);
        var delays = new ConcurrentQueue<TimeSpan>();
        var handler = FakeLokiHandler.Always(HttpStatusCode.InternalServerError);
        var sink = new LokiSink(options, handler, (delay, _) =>
        {
            delays.Enqueue(delay);
            return Task.CompletedTask;
        });

        sink.TryEnqueue(TestData.Entry());
        await Wait.UntilAsync(() => sink.LostEntryCount == 1);
        await sink.DisposeAsync();

        Assert.Equal(new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, delays.ToArray());
    }
}
