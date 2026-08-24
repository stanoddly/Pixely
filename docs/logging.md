# Logging

`Pixely.Logging` integrates ZLogger with Pixely's service collection. The logger factory belongs to the root service provider, remains available across stage transitions, and drains queued entries when the application is disposed.

See the [logging tutorial](../tutorials/Pixely.Tutorials.Logging) for a complete runnable example with file and debug-console providers and an injected application logger inside a registered service.

## Registration

Register the logger factory and application logger:

```csharp
using Pixely.App;
using Pixely.Logging;
using Microsoft.Extensions.Logging;
using ZLogger;

PixelyAppBuilder builder = new();
builder.AddZLogger(logging =>
{
    logging.SetMinimumLevel(LogLevel.Information);
    logging.AddZLoggerFileWithRetention(
        "game",
        static options =>
        {
            options.InternalErrorLogger = static exception => Console.Error.WriteLine(exception);
        });

#if DEBUG
    logging.AddZLoggerConsole(static options =>
    {
        options.FullMode = BackgroundBufferFullMode.Grow;
        options.InternalErrorLogger = static exception => Console.Error.WriteLine(exception);
    });
#endif
});
```

`AddZLoggerFileWithRetention` creates one file for the process using this naming policy:

```text
{prefix}_20260807_090416Z_pid48545.log
```

The timestamp is UTC and the process ID is labeled explicitly. When no directory is supplied, the helper first attempts `AppContext.BaseDirectory`, then falls back to `LocalApplicationData/Pixely/Logs`. A failed preferred location is reported through `InternalErrorLogger`. Before opening the new file, the helper keeps the latest nine existing matching files, leaving at most 10 after the new file is created. Other prefixes and unrelated files are not changed.

Pass a directory explicitly when the application has a platform-provided location. Explicit paths are strict and do not fall back:

```csharp
logging.AddZLoggerFileWithRetention(
    logDirectory,
    "game",
    static options =>
    {
        options.InternalErrorLogger = static exception => Console.Error.WriteLine(exception);
    });
```

The file provider uses an unbounded asynchronous buffer. Logging does not wait for file I/O, but a sustained output failure can retain queued entries and their captured values until writing recovers or the application shuts down. The integration sets `BackgroundBufferFullMode.Grow` explicitly and does not enable shared-file mode.

The internal error callback must write directly to a separate destination such as standard error or a platform diagnostic API. Do not send it through the failing logger.

## Application logger

`AddZLogger` registers one `ILogger` using the `Application` category. Services and static factory methods can receive the same logger directly:

```csharp
public sealed class PlayerSystem
{
    private readonly ILogger _logger;

    public PlayerSystem(ILogger logger)
    {
        _logger = logger;
    }
}
```

Child stage providers resolve the application logger from the root provider. Unloading a stage does not dispose it.

`ILoggerFactory` remains available when a subsystem needs a separate category. Adding category loggers later does not require replacing services that use the application logger.

## Logging calls

Use ZLogger interpolated handlers in hot paths so disabled levels do not evaluate interpolation expressions:

```csharp
logger.ZLogInformation($"Loaded level {levelName}");
logger.ZLogDebug($"Entity {entityId} moved to {position}");
```

`ZLogDebug` is controlled by runtime log-level filters and remains available in Release builds. Use `ZLogConditionalDebug` for diagnostics that must be removed from a build when `DEBUG` is not defined:

```csharp
logger.ZLogConditionalDebug($"Entity {entityId} moved to {position}");
logger.ZLogConditionalDebug($"Entity {entityId} failed", exception: exception);
```

Release call sites contain no logging invocation, handler construction, or interpolation-expression evaluation. Debug call sites still respect `ILogger.IsEnabled(LogLevel.Debug)`.

The background writer formats captured values later. Prefer small immutable values or snapshots; do not capture mutable objects whose state may change before formatting.

## Shutdown and durability

Dispose `IPixelyApp`, normally with a `using` declaration. Disposal completes the logging channel, drains queued entries, flushes the stream, and closes the current file.

Logging is best-effort. Returning from a logging call does not mean the entry is on disk, and a process crash can lose queued or operating-system-buffered entries.
