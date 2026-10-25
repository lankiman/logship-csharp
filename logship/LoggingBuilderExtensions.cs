using Logship.Core;
using Logship.Options;
using Microsoft.Extensions.DependencyInjection;

// This namespace is deliberate: it is already imported in most .NET apps, so AddLogship shows up
// next to AddConsole without an extra using.
namespace Microsoft.Extensions.Logging;

/// <summary>Registration for Logship.</summary>
public static class LogshipLoggingBuilderExtensions
{
    /// <summary>
    /// Adds the Logship logging provider. Nothing is written anywhere until you turn a sink on by setting
    /// <see cref="LogshipOptions.File"/> and/or <see cref="LogshipOptions.Loki"/>. Console output is not part
    /// of Logship; keep the built-in console provider if you want it.
    /// </summary>
    /// <param name="builder">The logging builder, for example <c>builder.Logging</c> in ASP.NET Core.</param>
    /// <param name="configure">Fills in the settings. It runs once, right away.</param>
    public static ILoggingBuilder AddLogship(this ILoggingBuilder builder, Action<LogshipOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new LogshipOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton<ILoggerProvider>(_ => new LogshipLoggerProvider(options));
        return builder;
    }
}
