using System.Text.Json;
using Logship.Options;
using Logship.Sinks;
using Microsoft.Extensions.Logging;

namespace Logship.Tests;

public class FileSinkTests
{
    private static FileSinkOptions Options(string directory) => new()
    {
        Directory = directory,
        FlushInterval = TimeSpan.FromMilliseconds(30)
    };

    private static string[] Messages(string path)
        => TestData.ReadLines(path)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("message").GetString()!)
            .ToArray();

    [Fact]
    public async Task Writes_one_json_line_per_entry_to_a_dated_file()
    {
        using var dir = new TempDirectory();
        var sink = new FileSink(Options(dir.FullPath), new ManualTimeProvider(TestData.Start));

        for (var i = 0; i < 3; i++)
        {
            sink.TryEnqueue(TestData.Entry($"m{i}"));
        }

        await sink.DisposeAsync();

        var path = dir.Combine("application-2026-03-05.log");
        Assert.True(File.Exists(path));
        Assert.Equal(new[] { "m0", "m1", "m2" }, Messages(path));
    }

    [Fact]
    public async Task Writes_text_lines_when_asked()
    {
        using var dir = new TempDirectory();
        var options = Options(dir.FullPath);
        options.Format = LogFileFormat.Text;
        var sink = new FileSink(options, new ManualTimeProvider(TestData.Start));

        sink.TryEnqueue(TestData.Entry("hello", LogLevel.Warning, "My.Category"));
        await sink.DisposeAsync();

        var lines = TestData.ReadLines(dir.Combine("application-2026-03-05.log"));
        Assert.Equal(new[] { "2026-03-05T10:00:00.000Z [warn] My.Category: hello" }, lines);
    }

    [Fact]
    public async Task Creates_the_directory_when_it_is_missing()
    {
        using var dir = new TempDirectory();
        var nested = dir.Combine("a", "b", "logs");
        var sink = new FileSink(Options(nested), new ManualTimeProvider(TestData.Start));

        sink.TryEnqueue(TestData.Entry());
        await sink.DisposeAsync();

        Assert.True(File.Exists(Path.Combine(nested, "application-2026-03-05.log")));
    }

    [Fact]
    public async Task Appends_to_an_existing_file_after_a_restart()
    {
        using var dir = new TempDirectory();
        var clock = new ManualTimeProvider(TestData.Start);

        var first = new FileSink(Options(dir.FullPath), clock);
        first.TryEnqueue(TestData.Entry("before"));
        await first.DisposeAsync();

        var second = new FileSink(Options(dir.FullPath), clock);
        second.TryEnqueue(TestData.Entry("after"));
        await second.DisposeAsync();

        Assert.Equal(new[] { "before", "after" }, Messages(dir.Combine("application-2026-03-05.log")));
    }

    [Fact]
    public async Task Starts_numbered_files_when_the_size_limit_would_be_passed()
    {
        using var dir = new TempDirectory();
        var options = Options(dir.FullPath);
        options.MaxFileSizeBytes = 300;
        var sink = new FileSink(options, new ManualTimeProvider(TestData.Start));

        for (var i = 0; i < 20; i++)
        {
            sink.TryEnqueue(TestData.Entry($"message-{i:00}"));
        }

        await sink.DisposeAsync();

        var files = Directory.GetFiles(dir.FullPath);
        Assert.True(files.Length > 1, "Expected the output to be split across several files.");
        Assert.All(files, f => Assert.True(new FileInfo(f).Length <= 300, $"{f} is larger than the limit."));
        Assert.Contains(files, f => f.EndsWith("application-2026-03-05.1.log"));

        var all = files.SelectMany(Messages).OrderBy(m => m).ToArray();
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"message-{i:00}").ToArray(), all);
    }

    [Fact]
    public async Task Starts_a_new_file_when_the_date_changes()
    {
        using var dir = new TempDirectory();
        var clock = new ManualTimeProvider(TestData.Start);
        var sink = new FileSink(Options(dir.FullPath), clock);
        var day1 = dir.Combine("application-2026-03-05.log");
        var day2 = dir.Combine("application-2026-03-06.log");

        sink.TryEnqueue(TestData.Entry("day one"));
        await Wait.UntilAsync(() => File.Exists(day1) && TestData.ReadLines(day1).Length == 1);

        clock.Advance(TimeSpan.FromDays(1));
        sink.TryEnqueue(TestData.Entry("day two"));
        await sink.DisposeAsync();

        Assert.Equal(new[] { "day one" }, Messages(day1));
        Assert.Equal(new[] { "day two" }, Messages(day2));
    }

    [Fact]
    public async Task Survives_a_directory_that_cannot_be_created()
    {
        using var dir = new TempDirectory();
        var blocker = dir.Combine("blocker");
        await File.WriteAllTextAsync(blocker, "I am a file, not a folder.");
        var sink = new FileSink(Options(Path.Combine(blocker, "logs")), new ManualTimeProvider(TestData.Start));

        sink.TryEnqueue(TestData.Entry());
        await Wait.UntilAsync(() => sink.FailedBatchCount >= 1);

        await sink.DisposeAsync();
    }

    [Fact]
    public async Task Releases_the_file_when_disposed()
    {
        using var dir = new TempDirectory();
        var sink = new FileSink(Options(dir.FullPath), new ManualTimeProvider(TestData.Start));
        sink.TryEnqueue(TestData.Entry());
        await sink.DisposeAsync();

        // FileShare.None fails if anything still holds the file open.
        using var exclusive = new FileStream(dir.Combine("application-2026-03-05.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
    }
}
