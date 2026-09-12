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
    /// Keeps every window hidden and renders its frames into a texture that <see cref="IFrameCapture"/> can read back.
    /// Frames are paced at <paramref name="frameInterval"/>, 60 per second by default, since there is no swapchain to wait on.
    /// Combine with <see cref="UseDefaultRendering(ServiceCollection, WindowConfig?)"/> or <see cref="UseWindowRendering{TRenderContext}"/>.
    /// </summary>
    public static ServiceCollection UseOffscreenRendering(this ServiceCollection services, TimeSpan? frameInterval = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new OffscreenRenderingConfig(frameInterval ?? TimeSpan.FromSeconds(1.0 / 60)));
        services.AddSingleton<IFrameCapture>(provider => (OffscreenWindow)provider.GetWindow());
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
