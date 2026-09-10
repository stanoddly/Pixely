using Pixely.App;
using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;
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
        int renderOrder = 10_000,
        int updateOrder = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UseUi<BasicRenderContext>(appBuilder, default, renderOrder, updateOrder, inputOrder, clearTarget);
    }

    public static PixelyAppBuilder UseUi(
        this PixelyAppBuilder appBuilder,
        ViewScope viewScope,
        int renderOrder = 10_000,
        int updateOrder = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UseUi<BasicRenderContext>(appBuilder, viewScope, renderOrder, updateOrder, inputOrder, clearTarget);
    }

    /// <param name="renderOrder">When the UI is drawn relative to the other renderers, lower first. Defaults late, so it draws over the game.</param>
    /// <param name="updateOrder">
    /// When the tree is built relative to the other updatables, lower first. Defaults late, so the
    /// UI builds after ordinary order-0 game systems and views sync against the state this frame
    /// produced. Equal orders are unspecified, not registration order.
    /// </param>
    /// <param name="inputOrder">When the UI sees input relative to the other subscribers, lower first. Defaults early, so it takes events before the game does.</param>
    public static PixelyAppBuilder UseUi<TRenderContext>(
        this PixelyAppBuilder appBuilder,
        ViewScope viewScope,
        int renderOrder = 10_000,
        int updateOrder = 10_000,
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

            // Built like any other singleton, which is what hands it the provider its views need.
            UiViewRegistry registry = UiViewRegistry.Register(appBuilder);
            appBuilder.AddSingleton<UiViewRegistry>(registry.Bind);
        }

        // The style is optional: an application that gives every label an explicit font needs none.
        // The clipboard is not: every application registers one, and a missing one should be loud
        // rather than showing up as fields that quietly will not paste.
        appBuilder.AddSingleton<ScopedUiRoot>(provider =>
            new ScopedUiRoot(
                viewScope,
                new UiRoot { Style = provider.GetService<UiStyle>() ?? UiStyle.Default, Clipboard = provider.GetRequiredService<IClipboardService>() }));

        appBuilder.AddSingleton<UiInputSystem>(provider =>
            new UiInputSystem(
                ScopedUiRoot.GetRequired(provider, viewScope).Root,
                CreateWindowSizeSource(provider, viewScope),
                viewScope,
                inputOrder,
                provider.GetRequiredService<IMouseService>(),
                provider.GetRequiredService<IKeyboardService>(),
                provider.GetRequiredService<ITextInputService>()));

        appBuilder.AddSingleton<UiUpdateSystem<TRenderContext>>(provider =>
        {
            (UiRoot root, Window window, RenderContextProvider<TRenderContext> contextProvider) = ResolveUpdateTargets<TRenderContext>(provider, viewScope);
            return new UiUpdateSystem<TRenderContext>(root, window, contextProvider, updateOrder);
        });

        appBuilder.AddSingleton<IRenderer<TRenderContext>, UiRenderer<TRenderContext>>(provider =>
            UiRenderer<TRenderContext>.Create(
                ScopedUiRoot.GetRequired(provider, viewScope).Root,
                viewScope,
                renderOrder,
                clearTarget,
                provider.GetRequiredService<GraphicsPipelineBuilder>(),
                provider.GetRequiredService<GpuMemorySystem>(),
                provider.GetRequiredService<ShaderLoader>(),
                provider.GetRequiredService<GpuDevice>(),
                provider.GetWindow(viewScope)));

        return appBuilder;
    }

    /// <summary>
    /// What one scope's update system drives and lays out against. Extracted so the lookups are
    /// observable to a test: the system itself holds only closures, and nothing can tell from
    /// outside which window or provider they captured.
    /// </summary>
    internal static (UiRoot Root, Window Window, RenderContextProvider<TRenderContext> ContextProvider) ResolveUpdateTargets<TRenderContext>(
        ServiceProvider provider,
        ViewScope viewScope)
        where TRenderContext : IRenderContext
    {
        // The scope has to be threaded through: GetWindow's viewScope parameter is defaulted, so
        // dropping it compiles and silently binds every window's UI to the first one.
        return (
            ScopedUiRoot.GetRequired(provider, viewScope).Root,
            provider.GetWindow(viewScope),
            provider.GetRequiredService<RenderContextProvider<TRenderContext>>());
    }

    /// <summary>
    /// The window's logical size, resolved once and read per event. The window itself is resolved
    /// here rather than inside the input system, which needs the size and nothing else.
    /// </summary>
    private static Func<Size<uint>> CreateWindowSizeSource(ServiceProvider provider, ViewScope viewScope)
    {
        Window window = provider.GetWindow(viewScope);
        return () => window.Size;
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
