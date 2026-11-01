using Pixely.App;
using Pixely.Gpu;
using Pixely.Testing;
using SDL;

namespace Pixely.Tests;

// For a test that builds a raw app because it checks something TestApp.CreateBuilder() changes, such as the frame clock, the config
// or the command console. Every app has a GPU device and a window, so without these the build would create the SDL device and a
// real window.
internal static class StubAppBuilderExtensions
{
    internal static PixelyAppBuilder AddStubGpuDevice(this PixelyAppBuilder builder)
    {
        builder.AddSingleton<StubGpuDevice>(static _ => StubGpuDevice.Create());
        builder.AddAlias<GpuDevice, StubGpuDevice>();
        return builder;
    }

    // The window is an SDL window on the dummy video driver, unless the environment picks another.
    internal static PixelyAppBuilder AddStubWindows(this PixelyAppBuilder builder)
    {
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
        builder.AddSingleton<IWindowFactory>(static provider => new StubWindowFactory(provider.GetRequiredService<PixelyFactory>()));
        return builder;
    }
}
