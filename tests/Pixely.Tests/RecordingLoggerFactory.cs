using Microsoft.Extensions.Logging;

namespace Pixely.Tests;

// Hands out the same recording logger for every category.
internal sealed class RecordingLoggerFactory(RecordingLogger logger) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName)
    {
        return logger;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
        logger.IsClosed = true;
    }
}
