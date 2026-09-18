using Pixely.App;
using Pixely.Logging;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace Pixely.Tests;

public class PixelyAppLoggingTests
{
    [Test]
    public void Dispose_DrainsAndClosesOwnedLoggerFactory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"PixelyAppLoggingTests-{Guid.NewGuid():N}");

        try
        {
            PixelyAppBuilder builder = new();
            builder.AddSingleton(new PixelyConfig(Headless: true));
            builder.AddZLogger(logging =>
            {
                logging.AddZLoggerFileWithRetention(
                    directoryPath,
                    "game",
                    static options =>
                    {
                        options.InternalErrorLogger = static _ => { };
                    });
            });

            IPixelyApp app = builder.Build();
            ILogger logger = app.GetRequiredService<ILogger>();
            logger.ZLogInformation($"last message");

            app.Dispose();

            string logPath = Directory.GetFiles(directoryPath, "game_*.log").Single();
            Assert.That(File.ReadAllText(logPath), Does.Contain("last message"));

            using FileStream exclusiveStream = new(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            if (Directory.Exists(directoryPath))
            {
                Directory.Delete(directoryPath, true);
            }
        }
    }
}
