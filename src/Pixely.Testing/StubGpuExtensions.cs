using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.Testing;

public static class StubGpuExtensions
{
    /// <summary>
    /// Registers a <see cref="StubGpuDevice"/> as the app's GPU device, with every GPU service on top of it, so a game's tests
    /// build the game's real services and renderers without a GPU. Call it before <c>UseGpu</c>, <c>UseDefaultRendering</c>
    /// or any registrar that calls them, which then keep the stub. Every window is a <see cref="StubWindow"/>, which hands out a swapchain texture each frame without a GPU.
    /// </summary>
    public static ServiceCollection UseStubGpu(this ServiceCollection services, StubGpuDeviceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.IsRegistered<GpuDevice>())
        {
            throw new InvalidOperationException($"A {nameof(GpuDevice)} is already registered. Call {nameof(UseStubGpu)} before UseGpu, UseDefaultRendering or a registrar that calls them.");
        }

        // Building still initializes SDL video, which needs no display with the dummy driver. The environment still wins.
        // It is written into the registered config and stays there, which is fine: a test builds a config for its one app, and a
        // game builds one app for its lifetime, so no other app reuses the instance.
        services.OnActivated(static (instance, _) =>
        {
            if (instance is PixelyConfig config)
            {
                config.SdlVideoDriver ??= "dummy";
            }
        });

        services.AddSingleton<StubGpuDevice>(_ => StubGpuDevice.Create(options));
        services.AddAlias<GpuDevice, StubGpuDevice>();
        services.AddSingleton<IWindowFactory>(provider => new StubWindowFactory(provider.GetRequiredService<PixelyFactory>(), provider.GetRequiredService<StubGpuDevice>()));
        GpuExtensions.AddGpuServices(services);
        return services;
    }
}
