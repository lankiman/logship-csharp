using System.Text.Json;
using Logship.Core;
using Logship.Formatting;
using Logship.Options;
using Microsoft.Extensions.Logging;

namespace Logship.Tests;

public class LokiPayloadBuilderTests
{
    private static LokiSinkOptions Options(string? environment = "production") => new()
    {
        Url = "http://loki:3100",
        AppLabel = "my-app",
        JobLabel = "my-job",
        Environment = environment
    };

    private static JsonDocument Build(IReadOnlyList<LogEntry> entries, LokiSinkOptions options)
        => JsonDocument.Parse(LokiPayloadBuilder.Build(entries, options));

    [Fact]
    public void Entries_with_the_same_level_and_category_share_a_stream()
    {
        var entries = new[]
        {
            TestData.Entry("a", LogLevel.Information, "A"),
            TestData.Entry("b", LogLevel.Information, "A"),
            TestData.Entry("c", LogLevel.Error, "A"),
            TestData.Entry("d", LogLevel.Information, "B")
        };

        using var json = Build(entries, Options());

        Assert.Equal(3, json.RootElement.GetProperty("streams").GetArrayLength());
    }

    [Fact]
    public void Streams_carry_the_expected_labels()
    {
        using var json = Build([TestData.Entry("a", LogLevel.Warning, "My.Category")], Options());

        var labels = json.RootElement.GetProperty("streams")[0].GetProperty("stream");

        Assert.Equal("my-job", labels.GetProperty("job").GetString());
        Assert.Equal("my-app", labels.GetProperty("app_name").GetString());
        Assert.Equal("production", labels.GetProperty("environment").GetString());
        Assert.Equal("warn", labels.GetProperty("level").GetString());
        Assert.Equal("My.Category", labels.GetProperty("context").GetString());
    }

    [Fact]
    public void The_environment_label_is_left_out_when_not_set()
    {
        using var json = Build([TestData.Entry()], Options(environment: null));

        var labels = json.RootElement.GetProperty("streams")[0].GetProperty("stream");

        Assert.False(labels.TryGetProperty("environment", out _));
    }

    [Fact]
    public void Each_value_is_a_nanosecond_string_and_a_json_line()
    {
        using var json = Build([TestData.Entry("hello")], Options());

        var pair = json.RootElement.GetProperty("streams")[0].GetProperty("values")[0];
        var nanoseconds = pair[0].GetString()!;
        using var line = JsonDocument.Parse(pair[1].GetString()!);

        Assert.True(nanoseconds.All(char.IsDigit));
        Assert.Equal("hello", line.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Timestamps_convert_to_unix_nanoseconds()
    {
        Assert.Equal("1767225600000000000", LokiPayloadBuilder.ToUnixNanoseconds(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.Equal("1767225600123456700", LokiPayloadBuilder.ToUnixNanoseconds(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_567)));
        Assert.Equal("0", LokiPayloadBuilder.ToUnixNanoseconds(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Values_within_a_stream_are_in_time_order()
    {
        var entries = new[]
        {
            TestData.Entry("late", at: TestData.Start.AddSeconds(10)),
            TestData.Entry("early", at: TestData.Start),
            TestData.Entry("middle", at: TestData.Start.AddSeconds(5))
        };

        using var json = Build(entries, Options());

        var values = json.RootElement.GetProperty("streams")[0].GetProperty("values");
        var messages = values.EnumerateArray()
            .Select(v => JsonDocument.Parse(v[1].GetString()!).RootElement.GetProperty("message").GetString())
            .ToArray();

        Assert.Equal(new[] { "early", "middle", "late" }, messages);
    }
}
