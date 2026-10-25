using System.Net;
using Logship.Options;
using Logship.Sinks;
using Microsoft.Extensions.Logging;

namespace Logship.Tests;

public class SinkRegistryTests
{
    private static LokiSinkOptions Loki(string url = "http://loki:3100") => new()
    {
        Url = url,
        AppLabel = "app",
        JobLabel = "job"
    };

    private static SinkRegistry Registry() => new(lokiHandlerFactory: () => FakeLokiHandler.Always(HttpStatusCode.NoContent));

    [Fact]
    public async Task Equal_settings_share_one_sink()
    {
        await using var registry = Registry();

        var first = registry.GetLoki(Loki());
        var second = registry.GetLoki(Loki());

        Assert.Same(first, second);
    }

    [Fact]
    public async Task A_trailing_slash_on_the_url_does_not_make_a_new_sink()
    {
        await using var registry = Registry();

        Assert.Same(registry.GetLoki(Loki("http://loki:3100")), registry.GetLoki(Loki("http://loki:3100/")));
    }

    [Fact]
    public async Task Different_settings_get_different_sinks()
    {
        await using var registry = Registry();

        var other = Loki("http://other-loki:3100");

        Assert.NotSame(registry.GetLoki(Loki()), registry.GetLoki(other));

        var differentLabel = Loki();
        differentLabel.AppLabel = "another-app";
        Assert.NotSame(registry.GetLoki(Loki()), registry.GetLoki(differentLabel));
    }

    [Fact]
    public async Task The_minimum_level_does_not_create_a_new_sink()
    {
        await using var registry = Registry();
        var warnings = Loki();
        warnings.MinLevel = LogLevel.Warning;

        Assert.Same(registry.GetLoki(Loki()), registry.GetLoki(warnings));
    }

    [Fact]
    public void Keys_never_print_secrets()
    {
        var options = Loki();
        options.AuthType = LokiAuthType.Basic;
        options.Username = "user";
        options.Password = "s3cret-password";
        options.Token = "s3cret-token";

        var key = LokiSinkKey.From(options);

        Assert.DoesNotContain("s3cret", key.ToString());
        Assert.DoesNotContain("s3cret", $"{key}");
    }

    [Fact]
    public async Task File_settings_that_point_at_the_same_file_share_one_sink()
    {
        using var dir = new TempDirectory();
        await using var registry = Registry();

        var first = registry.GetFile(new FileSinkOptions { Directory = dir.FullPath });
        var second = registry.GetFile(new FileSinkOptions { Directory = dir.FullPath + Path.DirectorySeparatorChar });

        Assert.Same(first, second);
    }

    [Fact]
    public async Task Different_directories_get_different_file_sinks()
    {
        using var dir = new TempDirectory();
        await using var registry = Registry();

        var first = registry.GetFile(new FileSinkOptions { Directory = dir.Combine("a") });
        var second = registry.GetFile(new FileSinkOptions { Directory = dir.Combine("b") });

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Threads_racing_for_the_same_sink_create_exactly_one()
    {
        var handlersCreated = 0;
        await using var registry = new SinkRegistry(lokiHandlerFactory: () =>
        {
            Interlocked.Increment(ref handlersCreated);
            return FakeLokiHandler.Always(HttpStatusCode.NoContent);
        });

        var options = Loki();
        var sinks = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => registry.GetLoki(options))));

        Assert.Single(sinks.Distinct());
        Assert.Equal(1, handlersCreated);
    }

    [Fact]
    public async Task Disposing_the_registry_shuts_its_sinks_down()
    {
        var registry = Registry();
        var sink = registry.GetLoki(Loki());

        await registry.DisposeAsync();

        Assert.False(sink.TryEnqueue(TestData.Entry()));
    }

    [Fact]
    public async Task A_disposed_registry_hands_out_nothing_new()
    {
        var registry = Registry();
        await registry.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => registry.GetLoki(Loki()));
        Assert.Throws<ObjectDisposedException>(() => registry.GetFile(new FileSinkOptions()));
    }
}
