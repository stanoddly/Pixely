using Pixely.App;
using Pixely.Logging;
using Pixely.RenderOrchestration;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace Pixely.Tutorials.Logging;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Logging"));

        builder.AddZLogger(logging =>
        {
#if DEBUG
            logging.SetMinimumLevel(LogLevel.Debug);
#else
            logging.SetMinimumLevel(LogLevel.Information);
#endif
            logging.AddZLoggerFileWithRetention(
                "pixely",
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
        builder.AddSingleton<PlayerInputService>(PlayerInputService.Create);
        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
