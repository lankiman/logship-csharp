# Logship (C#)

A logging provider for `Microsoft.Extensions.Logging` that writes **rolling log files** and ships logs to **Grafana Loki**.
It plugs into the standard `ILogger`, so your code keeps logging the way it always has.

This is the .NET counterpart of the Node.js `logship` package.

- **Never blocks your app.** Logging a line only puts it in a queue. A background worker does the file and network work.
- **Never crashes your app.** A full disk, an unreachable Loki or a bad config is reported on stderr, and the app carries on.
- **Bounded memory.** Each destination has a fixed-size queue. When it is full, new entries are dropped and counted, and the app is never slowed down.
- **Shared destinations.** Loggers that point at the same Loki or the same file share one queue, one worker and one connection.
- **Named targets.** Send different parts of one app to different places.

> Logship has no console output of its own. Keep the built-in console provider if you want it.

## Install

The package is not on NuGet yet. Until then, reference the project or build the package locally (see [Development](#development)).

```
dotnet add package Logship
```

## Quick start (ASP.NET Core)

```csharp
using Logship.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddLogship(o =>
{
    o.File = new FileSinkOptions { Directory = "logs" };

    o.Loki = new LokiSinkOptions
    {
        Url = "http://loki:3100",
        AppLabel = "my-api",
        JobLabel = "my-api",
        Environment = "production"
    };
});
```

Nothing is written anywhere until you set `File` and/or `Loki`. A sink you do not set is off.

Then log as usual:

```csharp
public class OrderService(ILogger<OrderService> logger)
{
    public void Place(int id)
    {
        logger.LogInformation("Placing order {OrderId}", id);
    }
}
```

`AddLogship` lives in the `Microsoft.Extensions.Logging` namespace, so it appears next to `AddConsole` without an extra `using`.

### Log levels

.NET's own filtering runs first. If your app's minimum level is `Information` (the ASP.NET Core default),
a `Debug` line never reaches Logship, whatever the sink's `MinLevel` says. To log `Debug`, lower both:

```csharp
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddLogship(o => o.File = new FileSinkOptions { MinLevel = LogLevel.Debug });
```

## File logging

Lines are appended to `Directory/FileNamePattern`, where `{date}` becomes the UTC date.

| | |
|---|---|
| New file | when the date changes, or when the next line would pass `MaxFileSizeBytes` |
| Names | `application-2026-03-05.log`, then `application-2026-03-05.1.log`, `.2.log`, … |
| Restarts | an existing file for the day is appended to, and full files are skipped |
| Format | `Json` (one object per line, the default) or `Text` |
| Readable while running | yes, the file is opened with `FileShare.Read`, so `tail -f` works |

JSON line:

```json
{"timestamp":"2026-03-05T10:00:00.123Z","level":"info","category":"MyApp.OrderService","message":"Placing order 7"}
```

An exception adds an `"exception"` field (its full `ToString()`). In `Text` format it is written on the lines after the entry.

Old files are **not** deleted yet. Clean them up with your own job or logrotate.

## Loki

Each batch is sent to `/loki/api/v1/push` (added to `Url` when missing).

**Labels:** `job`, `app_name`, `environment` (when set), `level`, `context` (the logger category).
Entries with the same level and category share a stream. The line itself is the same JSON as in the file, so in Grafana you can use `| json`.

**Auth** (`AuthType`): `None`, `Basic` (`Username` + `Password`), or `Token` (sent as `Authorization: Bearer <token>`).
`OrgId` is sent as `X-Scope-OrgID`.

**Delivery:** a batch is sent when it holds `BatchSize` entries or `FlushInterval` has passed.
Network errors, timeouts, `408`, `429` and `5xx` are retried up to `MaxRetries` times with doubling delays (capped at 30 s).
Any other `4xx` means the batch itself is bad, so it is dropped without retrying. Dropped batches are reported on stderr.

## Named targets

A logger's category decides where it ships. The longest matching name in `Targets` wins.
Names match whole dot-separated segments and ignore case, so `MyApp.Payments` matches `MyApp.Payments.PaymentService` but not `MyApp.PaymentsExtra`.

```csharp
builder.Logging.AddLogship(o =>
{
    o.File = new FileSinkOptions { Directory = "logs" };
    o.Loki = new LokiSinkOptions { Url = "http://loki:3100", AppLabel = "my-api", JobLabel = "my-api" };

    o.Targets["MyApp.Payments"] = new LogshipTargetOptions
    {
        // Replaces the default Loki settings as a whole for payment code.
        Loki = new LokiSinkOptions { Url = "http://audit-loki:3100", AppLabel = "my-api", JobLabel = "payments" },
        // File is not set, so payments still use the default file.
    };

    o.Targets["Noisy.Library"] = new LogshipTargetOptions
    {
        File = new FileSinkOptions { Directory = "logs/noisy" },
        InheritDefaults = false, // no Loki for this one
    };
});
```

`ILogger<PaymentService>` gets its category from the type name, and `ILoggerFactory.CreateLogger("MyApp.Payments")` lets you choose one yourself.
A target's `Loki` or `File` replaces the default **as a whole**; settings are not merged field by field.

## How it works

```
logger.LogInformation(...)
   → LogshipLogger          (builds a LogEntry; never blocks, never throws)
   → each enabled sink's bounded queue
        ├─ FileSink worker  (formats lines, writes, rolls files)
        └─ LokiSink worker  (batches, POSTs, retries)
```

The provider is a singleton. It asks a `SinkRegistry` for each category's sinks, and the registry creates a sink only once per distinct setting.
On shutdown the provider disposes the registry, which stops accepting entries, lets each worker finish what is queued (up to 5 seconds), and releases files and connections.

## Options reference

`LogshipOptions`: `Loki`, `File`, `Targets`.

`LokiSinkOptions`

| Option | Default | |
|---|---|---|
| `Url` | required | Loki address |
| `AppLabel`, `JobLabel` | required | `app_name` and `job` labels |
| `Environment` | none | `environment` label |
| `OrgId` | none | `X-Scope-OrgID` header |
| `AuthType`, `Username`, `Password`, `Token` | `None` | |
| `MinLevel` | `Information` | |
| `FlushInterval` | 1 s | |
| `BatchSize` | 500 | |
| `BufferCap` | 5000 | queue size; entries are dropped past it |
| `Timeout` | 5 s | per request |
| `MaxRetries` | 2 | extra attempts |
| `RetryBackoff` | 500 ms | doubles on each retry |

`FileSinkOptions`

| Option | Default | |
|---|---|---|
| `Directory` | `logs` | created when missing |
| `FileNamePattern` | `application-{date}.log` | must contain `{date}`, no folders |
| `DatePattern` | `yyyy-MM-dd` | .NET date format, UTC |
| `MaxFileSizeBytes` | 20 MB | |
| `Format` | `Json` | or `Text` |
| `MinLevel` | `Information` | |
| `FlushInterval` | 1 s | |
| `BufferCap` | 5000 | |

Invalid options (a malformed URL, both basic auth and a token, a pattern without `{date}`, and so on) disable that sink and print the reasons on stderr. They never throw.

## Limitations

- Logging scopes (`BeginScope`) and structured message properties are not recorded yet. Only the formatted message is.
- Old log files are not deleted.
- Settings cannot be bound from `appsettings.json` yet. Configure in code.
- Dates in file names are UTC.
- A log line that is dropped (queue full, or logged after shutdown) is counted but not reported.

## Development

```
dotnet build
dotnet test
dotnet pack logship -c Release   # creates the .nupkg
```

## License

[MIT](LICENSE)
