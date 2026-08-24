using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely;

public static class WindowServiceCollectionExtensions
{
    public static PixelyAppBuilder AddWindow(this PixelyAppBuilder builder, WindowConfig? config = null)
    {
        return AddWindow(builder, default, config);
    }

    public static PixelyAppBuilder AddWindow(this PixelyAppBuilder builder, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureWindow(builder, viewScope, config ?? new WindowConfig());
        return builder;
    }

    public static ServiceCollection AddWindow(this ServiceCollection services, WindowConfig? config = null)
    {
        return AddWindow(services, default, config);
    }

    public static ServiceCollection AddWindow(this ServiceCollection services, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ConfigureWindow(services, viewScope, config ?? new WindowConfig());
        return services;
    }

    private static void ConfigureWindow(ServiceCollection services, ViewScope viewScope, WindowConfig config)
    {
        services.AddSingleton<Window>(provider =>
            provider.GetRequiredService<PixelyFactory>().CreateWindow(
                viewScope,
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<PixelyFrameContext>(),
                config,
                provider.GetRequiredService<PlatformInfo>()));
    }
}
