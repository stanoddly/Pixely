using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public static class RenderingExtensions
{
    public static ServiceCollection UseWindowRendering<TRenderContext>(this ServiceCollection services, ViewScope viewScope = default)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ConfigureWindowRendering<TRenderContext>(services, viewScope);
        return services;
    }

    public static ServiceCollection UseDefaultRendering(this ServiceCollection services, WindowConfig? config = null)
    {
        return UseDefaultRendering(services, default, config);
    }

    public static ServiceCollection UseDefaultRendering(this ServiceCollection services, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddWindow(viewScope, config);
        if (!services.IsRegistered<RenderContextProvider<BasicRenderContext>>())
        {
            services.AddSingleton<RenderContextProvider<BasicRenderContext>, BasicRenderContextProvider>(provider =>
                new BasicRenderContextProvider(provider.GetRequiredService<GpuDevice>()));
        }
        ConfigureWindowRendering<BasicRenderContext>(services, viewScope);
        return services;
    }

    /// <summary>
    /// Like <see cref="UseDefaultRendering(ServiceCollection, WindowConfig?)"/> with an <see cref="OffscreenWindow"/>: nothing is shown and every
    /// frame is rendered into a texture the window can read back. Custom render contexts use
    /// <see cref="WindowServiceCollectionExtensions.AddOffscreenWindow"/> with <see cref="UseWindowRendering{TRenderContext}"/> instead.
    /// </summary>
    public static ServiceCollection UseOffscreenRendering(this ServiceCollection services, Size<uint>? size = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOffscreenWindow(size);
        if (!services.IsRegistered<RenderContextProvider<BasicRenderContext>>())
        {
            services.AddSingleton<RenderContextProvider<BasicRenderContext>, BasicRenderContextProvider>(provider =>
                new BasicRenderContextProvider(provider.GetRequiredService<GpuDevice>()));
        }
        ConfigureWindowRendering<BasicRenderContext>(services, default);
        return services;
    }

    private static void ConfigureWindowRendering<TRenderContext>(ServiceCollection services, ViewScope viewScope)
        where TRenderContext : IRenderContext
    {
        services.AddRegistry<IRenderer<TRenderContext>>(static renderer => renderer.RenderOrder);
        services.AddSingleton<IRenderCoordinator>(provider => new RenderCoordinator<TRenderContext>(
            provider.GetWindow(viewScope),
            provider.GetRequiredService<GpuMemorySystem>(),
            provider.GetRequiredService<RenderContextProvider<TRenderContext>>(),
            provider.GetRequiredService<ServiceRegistry<IRenderer<TRenderContext>>>()));
    }
}
