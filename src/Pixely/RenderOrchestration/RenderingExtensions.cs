using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public static class RenderingExtensions
{
    public static PixelyAppBuilder UseRenderCoordinator<TRenderContext>(
        this PixelyAppBuilder builder,
        Func<
            ServiceProvider,
            ServiceRegistry<IRenderer<TRenderContext>>,
            RenderCoordinator<TRenderContext>> factory)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureRenderCoordinator(builder, factory);
        return builder;
    }

    public static ServiceCollection UseRenderCoordinator<TRenderContext>(
        this ServiceCollection services,
        Func<
            ServiceProvider,
            ServiceRegistry<IRenderer<TRenderContext>>,
            RenderCoordinator<TRenderContext>> factory)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ConfigureRenderCoordinator(services, factory);
        return services;
    }

    public static PixelyAppBuilder UseDefaultRendering(
        this PixelyAppBuilder builder,
        WindowConfig? config = null)
    {
        return UseDefaultRendering(builder, default, config);
    }

    public static PixelyAppBuilder UseDefaultRendering(
        this PixelyAppBuilder builder,
        ViewScope viewScope,
        WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureDefaultRendering(builder, viewScope, config ?? new WindowConfig());
        return builder;
    }

    public static ServiceCollection UseDefaultRendering(
        this ServiceCollection services,
        WindowConfig? config = null)
    {
        return UseDefaultRendering(services, default, config);
    }

    public static ServiceCollection UseDefaultRendering(
        this ServiceCollection services,
        ViewScope viewScope,
        WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ConfigureDefaultRendering(services, viewScope, config ?? new WindowConfig());
        return services;
    }

    private static void ConfigureRenderCoordinator<TRenderContext>(
        ServiceCollection services,
        Func<
            ServiceProvider,
            ServiceRegistry<IRenderer<TRenderContext>>,
            RenderCoordinator<TRenderContext>> factory)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(factory);
        services.AddRegistry<IRenderer<TRenderContext>>(
            static (left, right) => left.Order.CompareTo(right.Order));
        services.AddSingleton<IRenderCoordinator>(provider => factory(
            provider,
            provider.GetRequiredService<ServiceRegistry<IRenderer<TRenderContext>>>()));
    }

    private static void ConfigureDefaultRendering(
        ServiceCollection services,
        ViewScope viewScope,
        WindowConfig config)
    {
        services.AddWindow(viewScope, config);
        services.AddSingleton<IRenderCoordinator>(provider =>
            new DefaultRenderCoordinator(
                provider.GetWindow(viewScope),
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<GpuMemorySystem>(),
                provider.GetRequiredService<ServiceRegistry<IRenderer<DefaultRenderContext>>>()));
    }
}
