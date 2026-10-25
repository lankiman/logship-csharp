using Logship.Core;
using Microsoft.Extensions.Logging;

namespace Logship.Tests;

public class LogEntryTests
{
    [Fact]
    public void Holds_the_values_it_was_given()
    {
        var at = DateTimeOffset.UtcNow;
        var entry = new LogEntry(at, LogLevel.Error, "Cat", "hello", "boom");

        Assert.Equal(at, entry.Timestamp);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Cat", entry.Category);
        Assert.Equal("hello", entry.Message);
        Assert.Equal("boom", entry.Exception);
    }

    [Fact]
    public void Exception_is_optional()
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "Cat", "hello", null);

        Assert.Null(entry.Exception);
    }
}
