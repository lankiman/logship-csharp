using logship.options;
using Logship.Options;
using Xunit;

public class LokiOptionsValidatorTests
{
    private static LokiSinkOptions Valid() => new()
    {
        Url = "http://localhost:3100",
        AppLabel = "app",
        JobLabel = "job"
    };

    [Fact]
    public void Valid_options_have_no_errors()
        => Assert.Empty(LokiOptionsValidator.Validate(Valid()));

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://host")]
    public void Bad_url_is_rejected(string url)
    {
        var o = Valid();
        o.Url = url;
        Assert.Contains(LokiOptionsValidator.Validate(o), e => e.Contains("Url"));
    }
}