using System.Text.Json;
using Logship.Core;
using Logship.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Logship.Tests;

public class LogshipLoggerProviderTests
{
    private static readonly ManualTimeProvider Clock = new(TestData.Start);

    private static FileSinkOptions File(string directory, LogLevel min = LogLevel.Information) => new()
    {
        Directory = directory,
        MinLevel = min,
        FlushInterval = TimeSpan.FromMilliseconds(30)
    };

    private static LogshipLoggerProvider Provider(LogshipOptions options) => new(options, time: Clock);

    private static string LogFile(string directory) => Path.Combine(directory, "application-2026-03-05.log");

    private static JsonElement[] Lines(string directory)
    {
        var path = LogFile(directory);
        return File_Exists(path)
            ? TestData.ReadLines(path).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToArray()
            : [];
    }

    private static bool File_Exists(string path) => System.IO.File.Exists(path);

    [Fact]
    public async Task Messages_reach_the_file()
    {
        using var dir = new TempDirectory();
        var provider = Provider(new LogshipOptions { File = File(dir.FullPath) });
        var logger = provider.CreateLogger("MyApp.Orders");

        logger.LogInformation("hello {Name}", "world");
        logger.LogError(new InvalidOperationException("bad"), "it broke");
        await provider.DisposeAsync();

        var lines = Lines(dir.FullPath);
        Assert.Equal(2, lines.Length);
        Assert.Equal("hello world", lines[0].GetProperty("message").GetString());
        Assert.Equal("MyApp.Orders", lines[0].GetProperty("category").GetString());
        Assert.Equal("info", lines[0].GetProperty("level").GetString());
        Assert.Equal("error", lines[1].GetProperty("level").GetString());
        Assert.Contains("InvalidOperationException", lines[1].GetProperty("exception").GetString());
    }

    [Fact]
    public async Task One_logger_is_created_per_category()
    {
        var provider = Provider(new LogshipOptions { File = File("unused") });

        Assert.Same(provider.CreateLogger("A"), provider.CreateLogger("A"));
        Assert.NotSame(provider.CreateLogger("A"), provider.CreateLogger("B"));

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task Levels_below_the_sink_minimum_are_not_written()
    {
        using var dir = new TempDirectory();
        var provider = Provider(new LogshipOptions { File = File(dir.FullPath, LogLevel.Warning) });
        var logger = provider.CreateLogger("Cat");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));

        logger.LogInformation("ignored");
        logger.LogWarning("kept");
        await provider.DisposeAsync();

        var line = Assert.Single(Lines(dir.FullPath));
        Assert.Equal("kept", line.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Each_sink_gets_only_the_levels_it_wants()
    {
        using var dir = new TempDirectory();
        var options = new LogshipOptions { File = File(dir.FullPath, LogLevel.Error) };
        var provider = Provider(options);
        var logger = provider.CreateLogger("Cat");

        logger.LogWarning("too quiet");
        logger.LogCritical("loud enough");
        await provider.DisposeAsync();

        Assert.Equal(new[] { "loud enough" }, Lines(dir.FullPath).Select(l => l.GetProperty("message").GetString()).ToArray());
    }

    [Fact]
    public async Task A_named_target_ships_to_its_own_destination()
    {
        using var dir = new TempDirectory();
        var defaults = dir.Combine("default");
        var payments = dir.Combine("payments");
        var options = new LogshipOptions { File = File(defaults) };
        options.Targets["MyApp.Payments"] = new LogshipTargetOptions { File = File(payments) };
        var provider = Provider(options);

        provider.CreateLogger("MyApp.Payments.Service").LogInformation("a payment");
        provider.CreateLogger("MyApp.Orders.Service").LogInformation("an order");
        await provider.DisposeAsync();

        Assert.Equal(new[] { "a payment" }, Lines(payments).Select(l => l.GetProperty("message").GetString()).ToArray());
        Assert.Equal(new[] { "an order" }, Lines(defaults).Select(l => l.GetProperty("message").GetString()).ToArray());
    }

    [Fact]
    public async Task Categories_with_the_same_destination_share_one_file()
    {
        using var dir = new TempDirectory();
        var provider = Provider(new LogshipOptions { File = File(dir.FullPath) });

        provider.CreateLogger("First").LogInformation("one");
        provider.CreateLogger("Second").LogInformation("two");
        await provider.DisposeAsync();

        var messages = Lines(dir.FullPath).Select(l => l.GetProperty("message").GetString()).OrderBy(m => m).ToArray();
        Assert.Equal(new[] { "one", "two" }, messages);
    }

    [Fact]
    public async Task Invalid_options_disable_only_the_broken_sink()
    {
        using var dir = new TempDirectory();
        var options = new LogshipOptions
        {
            Loki = new LokiSinkOptions { Url = "not a url", AppLabel = "app", JobLabel = "job" },
            File = File(dir.FullPath)
        };
        var provider = Provider(options);

        provider.CreateLogger("Cat").LogInformation("still works");
        await provider.DisposeAsync();

        Assert.Single(Lines(dir.FullPath));
    }

    [Fact]
    public async Task With_nothing_configured_the_logger_is_switched_off()
    {
        var provider = Provider(new LogshipOptions());

        Assert.False(provider.CreateLogger("Cat").IsEnabled(LogLevel.Critical));

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task A_logger_never_throws_into_the_app()
    {
        using var dir = new TempDirectory();
        var provider = Provider(new LogshipOptions { File = File(dir.FullPath) });
        var logger = provider.CreateLogger("Cat");

        logger.Log<string>(LogLevel.Error, new EventId(1), "state", null, (_, _) => throw new InvalidOperationException("formatter failed"));
        logger.LogInformation("after the failure");
        await provider.DisposeAsync();
        logger.LogInformation("after disposal"); // must not throw either

        Assert.Equal(new[] { "after the failure" }, Lines(dir.FullPath).Select(l => l.GetProperty("message").GetString()).ToArray());
    }

    [Fact]
    public void Synchronous_dispose_also_flushes()
    {
        using var dir = new TempDirectory();
        var provider = Provider(new LogshipOptions { File = File(dir.FullPath) });

        provider.CreateLogger("Cat").LogInformation("flushed");
        provider.Dispose();

        Assert.Single(Lines(dir.FullPath));
    }

    [Fact]
    public async Task AddLogship_registers_the_provider()
    {
        using var dir = new TempDirectory();
        var services = new ServiceCollection();
        var builder = new FakeLoggingBuilder(services);

        var returned = builder.AddLogship(o => o.File = File(dir.FullPath));

        Assert.Same(builder, returned);
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(ILoggerProvider));
        var provider = Assert.IsType<LogshipLoggerProvider>(descriptor.ImplementationFactory!(null!));
        await provider.DisposeAsync();
    }

    private sealed class FakeLoggingBuilder(IServiceCollection services) : ILoggingBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
