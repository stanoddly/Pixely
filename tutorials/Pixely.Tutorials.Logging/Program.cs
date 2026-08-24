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
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Logging"));

        appBuilder.AddZLogger(logging =>
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
        appBuilder.AddSingleton<PlayerInputService>(PlayerInputService.Create);
        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
