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
        PixelyAppBuilder builder = new();
        builder
            .AddContentFromProjectDirectory("Content")
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
        builder.AddSingleton<IRenderer<DefaultRenderContext>>(TransparentWindowRenderer.Create);

        builder.OnStart((IMouseService mouseService, AppControl appControl) =>
        {
            mouseService.ButtonPress += eventArgs => appControl.Quit();
        });

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
