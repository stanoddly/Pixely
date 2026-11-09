// Stock SDL refuses transparent GPU windows (https://github.com/libsdl-org/SDL/issues/12410); XDL, which Pixely binds,
// removes that check (README.md).

using Pixely.App;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TransparentWindow;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(
                    Size: (800, 600),
                    Title: "Transparent Window",
                    Transparent: true,
                    Borderless: true));
        if (OperatingSystem.IsWindows())
        {
            builder.AddSingleton(new PixelyConfig(GpuBackend: GpuBackend.Vulkan));
        }
        builder.AddSingleton<IRenderer<BasicRenderContext>>(TransparentWindowRenderer.Create);

        builder.OnBuilt((IMouseService mouseService, AppControl appControl) =>
        {
            mouseService.ButtonPress += eventArgs => appControl.Quit();
        });
    }
}
