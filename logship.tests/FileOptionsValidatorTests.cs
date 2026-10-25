using Logship.Options;

namespace Logship.Tests;

public class FileOptionsValidatorTests
{
    private static List<string> Errors(FileSinkOptions o) => FileOptionsValidator.Validate(o);

    [Fact]
    public void Defaults_are_valid()
        => Assert.Empty(Errors(new FileSinkOptions()));

    [Fact]
    public void Directory_is_required()
        => Assert.Contains(Errors(new FileSinkOptions { Directory = " " }), e => e.Contains("Directory"));

    [Theory]
    [InlineData("app.log")]
    [InlineData("")]
    public void File_name_pattern_needs_the_date_placeholder(string pattern)
        => Assert.Contains(Errors(new FileSinkOptions { FileNamePattern = pattern }), e => e.Contains("{date}"));

    [Theory]
    [InlineData("sub/app-{date}.log")]
    [InlineData("sub\\app-{date}.log")]
    public void File_name_pattern_cannot_contain_folders(string pattern)
        => Assert.Contains(Errors(new FileSinkOptions { FileNamePattern = pattern }), e => e.Contains("without folders"));

    [Theory]
    [InlineData("")]
    [InlineData("yyyy/MM/dd")]
    [InlineData("%")]
    public void Date_pattern_must_produce_a_legal_file_name(string pattern)
        => Assert.Contains(Errors(new FileSinkOptions { DatePattern = pattern }), e => e.Contains("DatePattern"));

    [Fact]
    public void Numbers_and_times_must_make_sense()
    {
        var errors = Errors(new FileSinkOptions
        {
            MaxFileSizeBytes = 0,
            FlushInterval = TimeSpan.Zero,
            BufferCap = 0
        });

        Assert.Contains(errors, e => e.Contains("MaxFileSizeBytes"));
        Assert.Contains(errors, e => e.Contains("FlushInterval"));
        Assert.Contains(errors, e => e.Contains("BufferCap"));
    }
}
