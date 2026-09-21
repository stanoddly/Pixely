using Pixely.App;
using Pixely.Content;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Browser;

// One project, two hosts: `dotnet run` opens a desktop window, `dotnet publish -r browser-wasm` produces a page under
// bin/Release/net11.0/browser-wasm/publish/wwwroot that fills the browser window. Both draw the same magenta quad: on the desktop
// through Vulkan, Direct3D 12 or Metal, in the browser through SDL's WebGPU backend and the WGSL the shader compiler emits.
// The shaders are embedded in the assembly (see the project file) because a content directory has no browser equivalent.
// Escape quits, which in the browser ends the frame loop and tears the GPU device down.
static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .ConfigureContent(content => content.AddSource(EmbeddedContentSource.Create(typeof(Program).Assembly)))
            .UseDefaultRendering(new WindowConfig(Size: (640, 480), Title: "Browser"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);

        builder.OnBuilt((Window window, IKeyboardService keyboardService, AppControl appControl) =>
        {
            Console.WriteLine($"Window size: {window.Size}");
            window.ResolutionChanged += eventArgs =>
            {
                Console.WriteLine($"Resolution changed from {eventArgs.OldSize} to {eventArgs.NewSize}");
            };
            keyboardService.KeyDown += eventArgs =>
            {
                if (eventArgs.Key == VirtualKey.Escape)
                {
                    appControl.Quit();
                }
            };
        });
    }
}
