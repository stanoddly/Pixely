using Pixely;
using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
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
            using RawImage icon = CreateIcon(32, 32);
            window.SetIcon(icon);

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
                if (eventArgs.Key != VirtualKey.Space)
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

    static RawImage CreateIcon(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                bool isWhite = (x / 4 + y / 4) % 2 == 0;

                pixels[i + 0] = isWhite ? (byte)255 : (byte)100; // R
                pixels[i + 1] = isWhite ? (byte)255 : (byte)100; // G
                pixels[i + 2] = isWhite ? (byte)255 : (byte)200; // B
                pixels[i + 3] = 255;                              // A
            }
        }

        return new RawImage(pixels, new ShortSize((ushort)width, (ushort)height), PixelFormat.Rgba8888);
    }
}
