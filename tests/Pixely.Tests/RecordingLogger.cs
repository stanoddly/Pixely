using Microsoft.Extensions.Logging;

namespace Pixely.Tests;

internal sealed class RecordingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IEnumerable<string> Messages => Entries.Select(entry => entry.Message);

    // Set when the factory that handed the logger out is disposed, after which a real logger drops what it is given.
    public bool IsClosed { get; set; }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsClosed)
        {
            throw new ObjectDisposedException(nameof(RecordingLogger), "Logged after its logger factory was disposed.");
        }

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
