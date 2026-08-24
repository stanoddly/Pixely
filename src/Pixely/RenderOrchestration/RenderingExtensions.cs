using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public static class RenderingExtensions
{
    public static PixelyAppBuilder UseWindowRendering<TRenderContext>(this PixelyAppBuilder builder, ViewScope viewScope = default)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureWindowRendering<TRenderContext>(builder, viewScope);
        return builder;
    }

    public static ServiceCollection UseWindowRendering<TRenderContext>(this ServiceCollection services, ViewScope viewScope = default)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ConfigureWindowRendering<TRenderContext>(services, viewScope);
        return services;
    }

    public static PixelyAppBuilder UseDefaultRendering(this PixelyAppBuilder builder, WindowConfig? config = null)
    {
        return UseDefaultRendering(builder, default, config);
    }

    public static PixelyAppBuilder UseDefaultRendering(this PixelyAppBuilder builder, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureDefaultRendering(builder, viewScope, config ?? new WindowConfig());
        return builder;
    }

    public static ServiceCollection UseDefaultRendering(this ServiceCollection services, WindowConfig? config = null)
    {
        return UseDefaultRendering(services, default, config);
    }

    public static ServiceCollection UseDefaultRendering(this ServiceCollection services, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ConfigureDefaultRendering(services, viewScope, config ?? new WindowConfig());
        return services;
    }

    private static void ConfigureWindowRendering<TRenderContext>(ServiceCollection services, ViewScope viewScope)
        where TRenderContext : IRenderContext
    {
        services.AddRegistry<IRenderer<TRenderContext>>(static (left, right) => left.Order.CompareTo(right.Order));
        services.AddSingleton<IRenderCoordinator>(provider => new RenderCoordinator<TRenderContext>(
            GetWindow(provider, viewScope),
            provider.GetRequiredService<GpuMemorySystem>(),
            provider.GetRequiredService<IRenderContextProvider<TRenderContext>>(),
            provider.GetRequiredService<ServiceRegistry<IRenderer<TRenderContext>>>()));
    }

    private static void ConfigureDefaultRendering(ServiceCollection services, ViewScope viewScope, WindowConfig config)
    {
        services.AddWindow(viewScope, config);
        if (!services.IsRegistered<IRenderContextProvider<DefaultRenderContext>>())
        {
            services.AddSingleton<IRenderContextProvider<DefaultRenderContext>, DefaultRenderContextProvider>(provider =>
                new DefaultRenderContextProvider(provider.GetRequiredService<GpuDevice>()));
        }
        ConfigureWindowRendering<DefaultRenderContext>(services, viewScope);
    }

    private static Window GetWindow(ServiceProvider provider, ViewScope viewScope)
    {
        foreach (Window window in provider.GetServices<Window>())
        {
            if (window.ViewScope == viewScope)
            {
                return window;
            }
        }

        throw new InvalidOperationException($"No window is registered for ViewScope {viewScope.Value}.");
    }
}
