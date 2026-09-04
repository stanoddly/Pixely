using Pixely;
using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.WindowConfiguration;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultRendering(
                new WindowConfig(
                    Size: (800, 600),
                    Title: "Window Configuration Demo",
                    AlwaysOnTop: true));

        builder.OnStart((WindowRegistry windowRegistry, IKeyboardService keyboardService, PlatformInfo platformInfo) =>
        {
            Window window = windowRegistry.GetWindow();
            Console.WriteLine($"SDL video driver: {platformInfo.SdlVideoDriver ?? "unknown"}");
            Console.WriteLine($"Always on top: {window.AlwaysOnTop}");
            Console.WriteLine($"Always-on-top supported by current SDL video driver: {window.SupportsAlwaysOnTop}");
            if (window.SupportsAlwaysOnTop)
            {
                Console.WriteLine("Press Space to toggle always-on-top.");
            }
            else
            {
                Console.WriteLine("The SDL Wayland backend does not currently apply always-on-top for normal windows.");
                Console.WriteLine("On KDE Wayland, try running with: SDL_VIDEO_DRIVER=x11 dotnet run");
            }

            keyboardService.KeyDown += eventArgs =>
            {
                // Toggling on every repeat while the key is held would flap the flag.
                if (eventArgs.Repeat || eventArgs.Key != VirtualKey.Space)
                {
                    return;
                }

                if (!window.SupportsAlwaysOnTop)
                {
                    Console.WriteLine("Always-on-top is not supported by the current SDL video driver.");
                    eventArgs.Consume();
                    return;
                }

                window.AlwaysOnTop = !window.AlwaysOnTop;
                Console.WriteLine($"Always on top: {window.AlwaysOnTop}");
                eventArgs.Consume();
            };
        });

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
