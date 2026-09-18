using Microsoft.Extensions.Logging;
using SDL;

namespace Pixely.Tests;

[NonParallelizable]
public class SdlLogOutputTests
{
    [Test]
    public void Write_WithLogger_LogsAtTheMappedLevel()
    {
        RecordingLogger logger = new();
        SdlLogOutput.Install(logger);
        try
        {
            SdlLogOutput.Write(SDL_LogPriority.SDL_LOG_PRIORITY_WARN, "careful");
        }
        finally
        {
            SdlLogOutput.Uninstall();
        }

        Assert.That(logger.Entries, Is.EqualTo(new[] { (LogLevel.Warning, "careful") }));
    }

    // SDL's own log entry points are varargs, which .NET cannot call, so the installed function is invoked directly.
    [Test]
    public unsafe void Install_RoutesSdlMessagesThroughTheCallback()
    {
        RecordingLogger logger = new();
        SdlLogOutput.Install(logger);
        try
        {
            delegate* unmanaged[Cdecl]<IntPtr, int, SDL_LogPriority, byte*, void> callback;
            IntPtr userdata;
            SDL3.SDL_GetLogOutputFunction(&callback, &userdata);
            fixed (byte* message = "careful\0"u8)
            {
                callback(userdata, (int)SDL_LogCategory.SDL_LOG_CATEGORY_APPLICATION, SDL_LogPriority.SDL_LOG_PRIORITY_WARN, message);
            }
        }
        finally
        {
            SdlLogOutput.Uninstall();
        }

        Assert.That(logger.Entries, Is.EqualTo(new[] { (LogLevel.Warning, "careful") }));
    }

    [TestCase(SDL_LogPriority.SDL_LOG_PRIORITY_TRACE, LogLevel.Trace)]
    [TestCase(SDL_LogPriority.SDL_LOG_PRIORITY_VERBOSE, LogLevel.Trace)]
    [TestCase(SDL_LogPriority.SDL_LOG_PRIORITY_DEBUG, LogLevel.Debug)]
    [TestCase(SDL_LogPriority.SDL_LOG_PRIORITY_INFO, LogLevel.Information)]
    [TestCase(SDL_LogPriority.SDL_LOG_PRIORITY_WARN, LogLevel.Warning)]
    [TestCase(SDL_LogPriority.SDL_LOG_PRIORITY_ERROR, LogLevel.Error)]
    [TestCase(SDL_LogPriority.SDL_LOG_PRIORITY_CRITICAL, LogLevel.Critical)]
    public void ToLogLevel_MapsEveryPriority(SDL_LogPriority priority, LogLevel expected)
    {
        Assert.That(SdlLogOutput.ToLogLevel(priority), Is.EqualTo(expected));
    }

    [Test]
    public void Write_WithoutLogger_SplitsTheConsoleByPriority()
    {
        using ConsoleCapture console = new();

        SdlLogOutput.Write(SDL_LogPriority.SDL_LOG_PRIORITY_INFO, "hello");
        SdlLogOutput.Write(SDL_LogPriority.SDL_LOG_PRIORITY_WARN, "careful");
        SdlLogOutput.Write(SDL_LogPriority.SDL_LOG_PRIORITY_ERROR, "broken");

        Assert.That(console.Output, Is.EqualTo($"hello{Environment.NewLine}"));
        Assert.That(console.Error, Is.EqualTo($"WARNING: careful{Environment.NewLine}ERROR: broken{Environment.NewLine}"));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }
    }

    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextWriter _originalOutput = Console.Out;
        private readonly TextWriter _originalError = Console.Error;
        private readonly StringWriter _output = new();
        private readonly StringWriter _error = new();

        public ConsoleCapture()
        {
            Console.SetOut(_output);
            Console.SetError(_error);
        }

        public string Output => _output.ToString();
        public string Error => _error.ToString();

        public void Dispose()
        {
            Console.SetOut(_originalOutput);
            Console.SetError(_originalError);
        }
    }
}
