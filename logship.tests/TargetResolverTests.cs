using Logship.Options;

namespace Logship.Tests;

public class TargetResolverTests
{
    private static LokiSinkOptions Loki(string app) => new() { Url = "http://loki:3100", AppLabel = app, JobLabel = "job" };

    private static FileSinkOptions File(string directory) => new() { Directory = directory };

    private static LogshipOptions Defaults() => new() { Loki = Loki("default"), File = File("default-logs") };

    [Fact]
    public void Categories_with_no_matching_target_use_the_defaults()
    {
        var options = Defaults();

        var (loki, file) = TargetResolver.Resolve("Anything.Else", options);

        Assert.Same(options.Loki, loki);
        Assert.Same(options.File, file);
    }

    [Fact]
    public void A_matching_target_replaces_the_setting_it_provides()
    {
        var options = Defaults();
        var payments = Loki("payments");
        options.Targets["MyApp.Payments"] = new LogshipTargetOptions { Loki = payments };

        var (loki, file) = TargetResolver.Resolve("MyApp.Payments.PaymentService", options);

        Assert.Same(payments, loki);
        Assert.Same(options.File, file); // not set by the target, so inherited
    }

    [Fact]
    public void A_target_can_stop_inheriting_the_defaults()
    {
        var options = Defaults();
        options.Targets["Quiet"] = new LogshipTargetOptions { File = File("quiet-logs"), InheritDefaults = false };

        var (loki, file) = TargetResolver.Resolve("Quiet.Thing", options);

        Assert.Null(loki);
        Assert.Equal("quiet-logs", file!.Directory);
    }

    [Fact]
    public void The_longest_matching_name_wins()
    {
        var options = Defaults();
        options.Targets["MyApp"] = new LogshipTargetOptions { File = File("app-logs") };
        options.Targets["MyApp.Payments"] = new LogshipTargetOptions { File = File("payment-logs") };

        Assert.Equal("payment-logs", TargetResolver.Resolve("MyApp.Payments.Service", options).File!.Directory);
        Assert.Equal("app-logs", TargetResolver.Resolve("MyApp.Orders", options).File!.Directory);
    }

    [Theory]
    [InlineData("MyApp.Payments", true)]
    [InlineData("MyApp.Payments.Service", true)]
    [InlineData("myapp.payments.service", true)]
    [InlineData("MyApp.PaymentsExtra", false)]
    [InlineData("MyApp.Pay", false)]
    [InlineData("Other.MyApp.Payments", false)]
    public void Names_match_whole_dot_separated_segments_ignoring_case(string category, bool matches)
    {
        var options = Defaults();
        options.Targets["MyApp.Payments"] = new LogshipTargetOptions { File = File("payment-logs") };

        var (_, file) = TargetResolver.Resolve(category, options);

        Assert.Equal(matches ? "payment-logs" : "default-logs", file!.Directory);
    }

    [Fact]
    public void Blank_target_names_are_ignored()
    {
        var options = Defaults();
        options.Targets[" "] = new LogshipTargetOptions { File = File("blank-logs") };

        Assert.Equal("default-logs", TargetResolver.Resolve("Anything", options).File!.Directory);
    }

    [Fact]
    public void With_no_defaults_and_no_targets_nothing_is_enabled()
    {
        var (loki, file) = TargetResolver.Resolve("Anything", new LogshipOptions());

        Assert.Null(loki);
        Assert.Null(file);
    }
}
