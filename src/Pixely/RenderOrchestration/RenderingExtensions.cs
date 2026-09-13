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
        AddDefaultRendering(services, viewScope);
        return services;
    }

    public static ServiceCollection UseHeadlessRendering(this ServiceCollection services, WindowConfig? config = null)
    {
        return UseHeadlessRendering(services, default, config);
    }

    /// <summary>
    /// <see cref="UseDefaultRendering(ServiceCollection, ViewScope, WindowConfig?)"/> with a headless window: nothing is shown, every
    /// frame is rendered into a texture the window can read back, and the app is driven through input automation. Custom render
    /// contexts use <see cref="HeadlessWindowServiceCollectionExtensions.AddHeadlessWindow(ServiceCollection, ViewScope, WindowConfig?)"/>
    /// with <see cref="UseWindowRendering{TRenderContext}"/> instead.
    /// </summary>
    public static ServiceCollection UseHeadlessRendering(this ServiceCollection services, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHeadlessWindow(viewScope, config);
        AddDefaultRendering(services, viewScope);
        return services;
    }

    private static void AddDefaultRendering(ServiceCollection services, ViewScope viewScope)
    {
        if (!services.IsRegistered<RenderContextProvider<BasicRenderContext>>())
        {
            services.AddSingleton<RenderContextProvider<BasicRenderContext>, BasicRenderContextProvider>(provider =>
                new BasicRenderContextProvider(provider.GetRequiredService<GpuDevice>()));
        }
        ConfigureWindowRendering<BasicRenderContext>(services, viewScope);
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
