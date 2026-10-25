using Logship.Options;

namespace Logship.Tests;

public class LokiOptionsValidatorTests
{
    private static LokiSinkOptions Valid() => new()
    {
        Url = "http://localhost:3100",
        AppLabel = "app",
        JobLabel = "job"
    };

    private static List<string> Errors(LokiSinkOptions o) => LokiOptionsValidator.Validate(o);

    [Fact]
    public void Valid_options_have_no_errors()
        => Assert.Empty(Errors(Valid()));

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://host")]
    public void Bad_url_is_rejected(string url)
    {
        var o = Valid();
        o.Url = url;

        Assert.Contains(Errors(o), e => e.Contains("Url"));
    }

    [Fact]
    public void Labels_are_required()
    {
        var o = Valid();
        o.AppLabel = " ";
        o.JobLabel = "";

        var errors = Errors(o);

        Assert.Contains(errors, e => e.Contains("AppLabel"));
        Assert.Contains(errors, e => e.Contains("JobLabel"));
    }

    [Fact]
    public void None_auth_rejects_credentials()
    {
        var o = Valid();
        o.Token = "abc";

        Assert.Contains(Errors(o), e => e.Contains("AuthType is None"));
    }

    [Fact]
    public void Basic_auth_needs_both_username_and_password()
    {
        var o = Valid();
        o.AuthType = LokiAuthType.Basic;
        o.Username = "user";

        Assert.Contains(Errors(o), e => e.Contains("Username and Password"));
    }

    [Fact]
    public void Basic_auth_cannot_be_mixed_with_a_token()
    {
        var o = Valid();
        o.AuthType = LokiAuthType.Basic;
        o.Username = "user";
        o.Password = "pass";
        o.Token = "abc";

        Assert.Contains(Errors(o), e => e.Contains("Token"));
    }

    [Fact]
    public void Token_auth_needs_a_token()
    {
        var o = Valid();
        o.AuthType = LokiAuthType.Token;

        Assert.Contains(Errors(o), e => e.Contains("needs a Token"));
    }

    [Fact]
    public void Token_auth_cannot_be_mixed_with_basic_credentials()
    {
        var o = Valid();
        o.AuthType = LokiAuthType.Token;
        o.Token = "abc";
        o.Username = "user";

        Assert.Contains(Errors(o), e => e.Contains("Username or Password"));
    }

    [Fact]
    public void Complete_basic_and_token_configs_are_valid()
    {
        var basic = Valid();
        basic.AuthType = LokiAuthType.Basic;
        basic.Username = "user";
        basic.Password = "pass";

        var token = Valid();
        token.AuthType = LokiAuthType.Token;
        token.Token = "abc";

        Assert.Empty(Errors(basic));
        Assert.Empty(Errors(token));
    }

    [Fact]
    public void Errors_never_contain_secrets()
    {
        var o = Valid();
        o.AuthType = LokiAuthType.Token;
        o.Token = "super-secret-token";
        o.Username = "user";
        o.Password = "super-secret-password";

        var text = string.Join(" ", Errors(o));

        Assert.DoesNotContain("super-secret", text);
    }

    [Fact]
    public void Every_problem_is_reported_not_just_the_first()
    {
        var o = Valid();
        o.Url = "nope";
        o.BatchSize = 0;
        o.MaxRetries = -1;

        Assert.True(Errors(o).Count >= 3);
    }

    [Fact]
    public void Numbers_and_times_must_make_sense()
    {
        var o = Valid();
        o.FlushInterval = TimeSpan.Zero;
        o.BatchSize = 10;
        o.BufferCap = 5;
        o.Timeout = TimeSpan.Zero;
        o.MaxRetries = -1;
        o.RetryBackoff = TimeSpan.FromSeconds(-1);

        var errors = Errors(o);

        Assert.Contains(errors, e => e.Contains("FlushInterval"));
        Assert.Contains(errors, e => e.Contains("BufferCap"));
        Assert.Contains(errors, e => e.Contains("Timeout"));
        Assert.Contains(errors, e => e.Contains("MaxRetries"));
        Assert.Contains(errors, e => e.Contains("RetryBackoff"));
    }
}
