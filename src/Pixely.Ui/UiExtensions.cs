using Pixely.App;
using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Ui;

public static class UiExtensions
{
    /// <summary>
    /// Registers a <see cref="UiRoot"/> and its renderer for a window.
    /// </summary>
    public static PixelyAppBuilder UseUi(
        this PixelyAppBuilder appBuilder,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UseUi<BasicRenderContext>(appBuilder, default, order, inputOrder, clearTarget);
    }

    public static PixelyAppBuilder UseUi(
        this PixelyAppBuilder appBuilder,
        ViewScope viewScope,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UseUi<BasicRenderContext>(appBuilder, viewScope, order, inputOrder, clearTarget);
    }

    public static PixelyAppBuilder UseUi<TRenderContext>(
        this PixelyAppBuilder appBuilder,
        ViewScope viewScope,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(appBuilder);

        if (!appBuilder.IsRegistered<ServiceRegistry<ScopedUiRoot>>())
        {
            appBuilder.ConfigureContent(contentSourceBuilder =>
                contentSourceBuilder.AddSource(EmbeddedContentSource.Create(typeof(UiExtensions).Assembly)));
            appBuilder.AddRegistry<ScopedUiRoot>();
        }

        appBuilder.AddSingleton<ScopedUiRoot>(_ => new ScopedUiRoot(viewScope, new UiRoot()));

        appBuilder.AddSingleton<IRenderer<TRenderContext>, UiRenderer<TRenderContext>>(provider =>
            new UiRenderer<TRenderContext>(
                ScopedUiRoot.GetRequired(provider, viewScope).Root,
                viewScope,
                order,
                clearTarget,
                provider.GetRequiredService<GraphicsPipelineBuilder>(),
                provider.GetRequiredService<GpuMemorySystem>(),
                provider.GetRequiredService<ShaderLoader>(),
                provider.GetRequiredService<GpuDevice>(),
                provider.GetWindow(viewScope)));

        return appBuilder;
    }

    /// <summary>Resolves the <see cref="UiRoot"/> registered for a window.</summary>
    public static UiRoot GetUiRoot(this ServiceProvider provider, ViewScope viewScope = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return ScopedUiRoot.GetRequired(provider, viewScope).Root;
    }
}

/// <summary>Binds a <see cref="UiRoot"/> to the window it draws into.</summary>
internal sealed class ScopedUiRoot
{
    internal ScopedUiRoot(ViewScope viewScope, UiRoot root)
    {
        ViewScope = viewScope;
        Root = root;
    }

    internal ViewScope ViewScope { get; }

    internal UiRoot Root { get; }

    internal static ScopedUiRoot GetRequired(ServiceProvider provider, ViewScope viewScope)
    {
        ServiceRegistry<ScopedUiRoot> registry = provider.GetRequiredService<ServiceRegistry<ScopedUiRoot>>();
        ScopedUiRoot? result = null;

        foreach (ScopedUiRoot candidate in registry)
        {
            if (candidate.ViewScope != viewScope)
            {
                continue;
            }

            if (result != null)
            {
                throw new InvalidOperationException($"Pixely.Ui is configured more than once for ViewScope {viewScope.Value}.");
            }

            result = candidate;
        }

        return result ?? throw new InvalidOperationException($"Pixely.Ui is not configured for ViewScope {viewScope.Value}.");
    }
}
