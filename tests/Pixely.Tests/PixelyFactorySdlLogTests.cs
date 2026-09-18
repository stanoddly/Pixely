using Microsoft.Extensions.Logging;
using SDL;

namespace Pixely.Tests;

public class PixelyFactorySdlLogTests
{
    [Test]
    public void WriteSdlLogMessage_WithLoggerFactory_LogsAtTheMappedLevel()
    {
        RecordingLoggerFactory loggerFactory = new();
        using PixelyFactory factory = new(new PixelyConfig(), loggerFactory);

        factory.WriteSdlLogMessage(SDL_LogPriority.SDL_LOG_PRIORITY_WARN, "careful");

        Assert.That(loggerFactory.Category, Is.EqualTo("SDL"));
        Assert.That(loggerFactory.Entries, Is.EqualTo(new[] { (LogLevel.Warning, "careful") }));
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
        Assert.That(PixelyFactory.ToLogLevel(priority), Is.EqualTo(expected));
    }

    [Test]
    public void WriteSdlLogMessage_WithoutLogger_SplitsTheConsoleByPriority()
    {
        using PixelyFactory factory = new(new PixelyConfig(), null);
        using ConsoleCapture console = new();

        factory.WriteSdlLogMessage(SDL_LogPriority.SDL_LOG_PRIORITY_INFO, "hello");
        factory.WriteSdlLogMessage(SDL_LogPriority.SDL_LOG_PRIORITY_WARN, "careful");
        factory.WriteSdlLogMessage(SDL_LogPriority.SDL_LOG_PRIORITY_ERROR, "broken");

        Assert.That(console.Output, Is.EqualTo($"hello{Environment.NewLine}"));
        Assert.That(console.Error, Is.EqualTo($"WARNING: careful{Environment.NewLine}ERROR: broken{Environment.NewLine}"));
    }

    [Test]
    public void WriteSdlLogMessage_WithoutLoggerInHeadlessMode_KeepsStandardOutputClear()
    {
        using PixelyFactory factory = new(new PixelyConfig(Headless: true), null);
        using ConsoleCapture console = new();

        factory.WriteSdlLogMessage(SDL_LogPriority.SDL_LOG_PRIORITY_INFO, "hello");

        Assert.That(console.Output, Is.Empty);
        Assert.That(console.Error, Is.EqualTo($"hello{Environment.NewLine}"));
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory, ILogger
    {
        public string? Category { get; private set; }
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            Category = categoryName;
            return this;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

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

        public void Dispose()
        {
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
