using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Pixely.Tests;

// Hands out the recording logger for one category, or for every category when none is given. Other categories get a logger
// that drops everything.
internal sealed class RecordingLoggerFactory(RecordingLogger logger, string? category = null) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName)
    {
        return category is null || categoryName == category ? logger : NullLogger.Instance;
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
        logger.IsClosed = true;
    }
}
