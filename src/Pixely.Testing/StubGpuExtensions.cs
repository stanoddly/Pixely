using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.Testing;

public static class StubGpuExtensions
{
    /// <summary>
    /// Registers a <see cref="StubGpuDevice"/> as the app's GPU device, with every GPU service on top of it, so a game's tests
    /// build the game's real services and renderers without a GPU. Call it before <c>UseGpu</c>, <c>UseDefaultRendering</c>
    /// or any registrar that calls them, which then keep the stub. Every window renders into a texture, as in headless mode.
    /// </summary>
    public static ServiceCollection UseStubGpu(this ServiceCollection services, StubGpuDeviceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.IsRegistered<GpuDevice>())
        {
            throw new InvalidOperationException($"A {nameof(GpuDevice)} is already registered. Call {nameof(UseStubGpu)} before UseGpu, UseDefaultRendering or a registrar that calls them.");
        }

        // Building still initializes SDL video, which needs no display with the dummy driver. The environment still wins.
        services.OnActivated(static (instance, _) =>
        {
            if (instance is PixelyConfig config)
            {
                config.SdlVideoDriver ??= "dummy";
            }
        });

        // SDL is initialized before the device, as for the SDL device: SDL_ttf, which the font system starts while the app is
        // built, finds SDL only once SDL is loaded.
        services.AddSingleton<StubGpuDevice>(provider =>
        {
            provider.GetService<PixelyFactory>()?.EnsureSdlInitialized();
            return StubGpuDevice.Create(options);
        });
        services.AddAlias<GpuDevice, StubGpuDevice>();
        GpuExtensions.AddGpuServices(services);
        return services;
    }
}
