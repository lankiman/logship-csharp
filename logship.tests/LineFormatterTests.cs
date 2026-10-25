using System.Text.Json;
using Logship.Formatting;
using Logship.Options;
using Microsoft.Extensions.Logging;

namespace Logship.Tests;

public class LineFormatterTests
{
    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 12, 0, 0, 123, TimeSpan.Zero);

    [Fact]
    public void Json_line_contains_every_field()
    {
        var entry = TestData.Entry("hello", LogLevel.Warning, "My.Category", Moment, "boom");

        using var json = JsonDocument.Parse(LineFormatter.Format(entry, LogFileFormat.Json));
        var root = json.RootElement;

        Assert.Equal("2026-01-01T12:00:00.123Z", root.GetProperty("timestamp").GetString());
        Assert.Equal("warn", root.GetProperty("level").GetString());
        Assert.Equal("My.Category", root.GetProperty("category").GetString());
        Assert.Equal("hello", root.GetProperty("message").GetString());
        Assert.Equal("boom", root.GetProperty("exception").GetString());
    }

    [Fact]
    public void Json_line_leaves_out_the_exception_when_there_is_none()
    {
        using var json = JsonDocument.Parse(LineFormatter.Format(TestData.Entry(), LogFileFormat.Json));

        Assert.False(json.RootElement.TryGetProperty("exception", out _));
    }

    [Fact]
    public void Json_stays_on_one_line_even_when_the_message_does_not()
    {
        var entry = TestData.Entry("first\nsecond\r\nthird");

        var line = LineFormatter.Format(entry, LogFileFormat.Json);

        Assert.DoesNotContain("\n", line);
        using var json = JsonDocument.Parse(line);
        Assert.Equal("first\nsecond\r\nthird", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Text_line_is_readable()
    {
        var entry = TestData.Entry("hello", LogLevel.Error, "My.Category", Moment);

        Assert.Equal("2026-01-01T12:00:00.123Z [error] My.Category: hello", LineFormatter.Format(entry, LogFileFormat.Text));
    }

    [Fact]
    public void Text_line_puts_the_exception_after_the_entry()
    {
        var entry = TestData.Entry("hello", exception: "System.Exception: boom");

        var text = LineFormatter.Format(entry, LogFileFormat.Text);

        Assert.EndsWith("\nSystem.Exception: boom", text);
    }

    [Fact]
    public void A_lone_surrogate_is_replaced_instead_of_throwing()
    {
        var entry = TestData.Entry("cut in half \uD83D here", category: "Bad\uDE00Category");

        var line = LineFormatter.Format(entry, LogFileFormat.Json);

        using var json = JsonDocument.Parse(line);
        Assert.Contains("\uFFFD", json.RootElement.GetProperty("message").GetString());
        Assert.Contains("\uFFFD", json.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public void Emoji_survive()
    {
        var entry = TestData.Entry("done \U0001F680");

        using var json = JsonDocument.Parse(LineFormatter.Format(entry, LogFileFormat.Json));

        Assert.Equal("done \U0001F680", json.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trace")]
    [InlineData(LogLevel.Debug, "debug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Critical, "critical")]
    public void Level_names_match_what_Grafana_recognises(LogLevel level, string expected)
        => Assert.Equal(expected, LineFormatter.LevelName(level));
}
