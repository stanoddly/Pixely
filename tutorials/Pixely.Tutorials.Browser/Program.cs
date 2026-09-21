using Pixely.App;

namespace Pixely.Tutorials.Browser;

// One project, two hosts: `dotnet run` opens a desktop window, `dotnet publish -r browser-wasm` produces a page under
// bin/Release/net11.0/browser-wasm/publish/wwwroot that fills the browser window. No GPU is used, so the desktop window
// stays black and its loop spins (nothing waits for vsync); in the browser requestAnimationFrame paces the frames.
static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder.AddWindow(new WindowConfig(Size: (640, 480), Title: "Browser"));

        builder.OnBuilt((Window window) =>
        {
            Console.WriteLine($"Window size: {window.Size}");
            window.ResolutionChanged += eventArgs =>
            {
                Console.WriteLine($"Resolution changed from {eventArgs.OldSize} to {eventArgs.NewSize}");
            };
        });
    }
}
