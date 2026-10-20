using logship.core;
using Microsoft.Extensions.Logging;
namespace logship.tests;

public class LogEntryTests
{
    [Fact]
    public void Holds_the_values_it_was_given()
    {
        var entry = new LogEntry(DateTimeOffset.UtcNow, LogLevel.Error, "Cat", "hello", null);
        Assert.Equal("hello", entry.Message);
    }
}