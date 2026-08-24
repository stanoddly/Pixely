// Requires a patched SDL3 build with the SDL_WINDOW_TRANSPARENT guard removed
// from SDL_ClaimWindowForGPUDevice() in src/gpu/SDL_gpu.c.
// The Vulkan backend supports transparent swapchains natively, but SDL3 blocks
// it at the API level because D3D12 does not support it yet.
// See: https://github.com/libsdl-org/SDL/issues/12410

using Pixely.App;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TransparentWindow;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(
                    Size: (800, 600),
                    Title: "Transparent Window",
                    Transparent: true,
                    Borderless: true));
        if (OperatingSystem.IsWindows())
        {
            appBuilder.AddSingleton(new PixelyConfig(GpuBackend: GpuBackend.Vulkan));
        }
        appBuilder.AddSingleton<IRenderer<DefaultRenderContext>>(TransparentWindowRenderer.Create);

        appBuilder.OnStart((IMouseService mouseService, AppControl appControl) =>
        {
            mouseService.ButtonPress += eventArgs => appControl.Quit();
        });

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
