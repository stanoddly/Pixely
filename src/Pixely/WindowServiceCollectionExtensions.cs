using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely;

public static class WindowServiceCollectionExtensions
{
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
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<PixelyFrameContext>(),
                config ?? new WindowConfig(),
                provider.GetRequiredService<PlatformInfo>(),
                provider.GetRequiredService<IImageLoader>()));
        return services;
    }

    /// <summary>
    /// Registers an <see cref="OffscreenWindow"/> as the default scope's <see cref="Window"/>. Only the size is configurable; nothing about it reaches the desktop.
    /// </summary>
    public static ServiceCollection AddOffscreenWindow(this ServiceCollection services, Size<uint>? size = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<OffscreenWindow>(provider =>
            provider.GetRequiredService<PixelyFactory>().CreateOffscreenWindow(
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<PixelyFrameContext>(),
                provider.GetRequiredService<PlatformInfo>(),
                size));
        services.AddAlias<Window, OffscreenWindow>();
        return services;
    }
}
