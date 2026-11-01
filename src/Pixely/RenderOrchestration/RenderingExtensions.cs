using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

// Registers the render coordinator of the app's one window. Either the root registers it or every stage does, never both: each
// frame runs exactly one coordinator, see PixelyApp.RunFrame.
public static class RenderingExtensions
{
    public static ServiceCollection UseWindowRendering<TRenderContext>(this ServiceCollection services)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(services);
        if (typeof(TRenderContext) == typeof(BasicRenderContext))
        {
            AddBasicRenderContextProvider(services);
        }

        ConfigureWindowRendering<TRenderContext>(services);
        return services;
    }

    public static ServiceCollection UseDefaultRendering(this ServiceCollection services)
    {
        return UseWindowRendering<BasicRenderContext>(services);
    }

    private static void AddBasicRenderContextProvider(ServiceCollection services)
    {
        if (!services.IsRegistered<RenderContextProvider<BasicRenderContext>>())
        {
            services.AddSingleton<RenderContextProvider<BasicRenderContext>, BasicRenderContextProvider>(static _ => new BasicRenderContextProvider());
        }
    }

    private static void ConfigureWindowRendering<TRenderContext>(ServiceCollection services)
        where TRenderContext : IRenderContext
    {
        services.AddRegistry<IRenderer<TRenderContext>>(static renderer => renderer.RenderOrder);
        services.AddSingleton<IRenderCoordinator>(static provider => new RenderCoordinator<TRenderContext>(
            provider.GetRequiredService<Window>(),
            new RenderCoordinatorGpu(provider.GetRequiredService<GpuDevice>(), provider.GetRequiredService<GpuMemorySystem>()),
            provider.GetRequiredService<RenderContextProvider<TRenderContext>>(),
            provider.GetRequiredService<ServiceRegistry<IRenderer<TRenderContext>>>(),
            // PixelyAppBuilder.Build() registers the recorder; a provider built without it records nothing.
            provider.GetService<IFrameTimingRecorder>() ?? NullFrameTimingRecorder.Instance));
    }
}
