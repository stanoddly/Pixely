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
        {
            WindowConfig windowConfig = config ?? new WindowConfig();
            GpuDevice? gpuDevice = provider.GetService<GpuDevice>();
            PixelyFrameClock frameClock = provider.GetRequiredService<PixelyFrameClock>();
            PlatformInfo platformInfo = provider.GetRequiredService<PlatformInfo>();
            if (GetWindowFactory(provider) is { } windowFactory)
            {
                return windowFactory.CreateWindow(viewScope, gpuDevice, frameClock, windowConfig, platformInfo);
            }

            return provider.GetRequiredService<PixelyFactory>().CreateWindow(viewScope, gpuDevice, frameClock, windowConfig, platformInfo,
                provider.GetRequiredService<IImageLoader>());
        });
        return services;
    }

    // Two factories mean two kinds of window, such as headless mode's offscreen ones and the stub GPU device's, and neither works
    // on the other's terms, so picking one would hide the conflict.
    private static IWindowFactory? GetWindowFactory(ServiceProvider provider)
    {
        IReadOnlyList<IWindowFactory> windowFactories = provider.GetServices<IWindowFactory>();
        if (windowFactories.Count > 1)
        {
            throw new PixelyInitializationException(
                $"An app's windows come from one place, but {string.Join(" and ", windowFactories.Select(static factory => factory.GetType().Name))} are both registered to create them.");
        }

        return windowFactories.Count == 1 ? windowFactories[0] : null;
    }
}
