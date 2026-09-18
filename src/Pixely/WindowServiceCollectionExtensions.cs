using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely;

public static class WindowServiceCollectionExtensions
{
    // The window has a GPU device only when a rendering registrar or UseGpu() registered one; otherwise it is created unclaimed.
    public static ServiceCollection AddWindow(this ServiceCollection services, WindowConfig? config = null)
    {
        return AddWindow(services, default, config);
    }

    public static ServiceCollection AddWindow(this ServiceCollection services, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<Window>(provider =>
            provider.GetRequiredService<PixelyFactory>().CreateWindow(
                viewScope,
                provider.GetService<GpuDevice>(),
                provider.GetRequiredService<PixelyFrameContext>(),
                config ?? new WindowConfig(),
                provider.GetRequiredService<PlatformInfo>(),
                provider.GetRequiredService<IImageLoader>()));
        return services;
    }
}
