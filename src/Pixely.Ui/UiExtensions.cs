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
    /// Registers the app's <see cref="UiRoot"/> and its renderer.
    /// </summary>
    public static PixelyAppBuilder UseUi(
        this PixelyAppBuilder appBuilder,
        int renderOrder = RenderOrders.Ui,
        int updateOrder = UpdateOrders.Ui,
        int inputOrder = -10_000,
        bool clearTarget = false,
        float scale = 1f)
    {
        return UseUi<BasicRenderContext>(appBuilder, renderOrder, updateOrder, inputOrder, clearTarget, scale);
    }

    /// <param name="renderOrder">When the UI is drawn relative to the other renderers, lower first. Defaults late, so it draws over the game.</param>
    /// <param name="updateOrder">
    /// When the tree is built relative to the other updatables, lower first. Defaults late, so the
    /// UI builds after ordinary order-0 game systems and views sync against the state this frame
    /// produced. Updatables with an equal order run in registration order.
    /// </param>
    /// <param name="inputOrder">When the UI sees input relative to the other subscribers, lower first. Defaults early, so it takes events before the game does.</param>
    /// <param name="scale">
    /// How many target pixels one logical pixel covers, see <see cref="UiRoot.Scale"/>. The root
    /// starts at it, so the first build is laid out at it; <see cref="UiRoot.RequestScale"/> changes
    /// it at runtime. Must be finite and at least 1.
    /// </param>
    /// <param name="selectColorTarget">
    /// The texture the UI is presented into, picked from the frame's render context. Defaults to
    /// <see cref="IRenderContext.ColorTarget"/>. A game that post-processes the UI on its own
    /// selects a texture of its own here and composites it in a later renderer; the texture must be
    /// the size <see cref="RenderContextProvider{TRenderContext}.GetColorTargetSize"/> reports and
    /// in the window's colour format.
    /// </param>
    public static PixelyAppBuilder UseUi<TRenderContext>(
        this PixelyAppBuilder appBuilder,
        int renderOrder = RenderOrders.Ui,
        int updateOrder = UpdateOrders.Ui,
        int inputOrder = -10_000,
        bool clearTarget = false,
        float scale = 1f,
        Func<TRenderContext, Texture>? selectColorTarget = null)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(appBuilder);

        // Refused here rather than when the root is built, so a bad scale names the UseUi call.
        UiRoot.ValidateScale(scale);

        if (appBuilder.IsRegistered<UiRoot>())
        {
            throw new InvalidOperationException("Pixely.Ui is configured more than once.");
        }

        appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(EmbeddedContentSource.Create(typeof(UiExtensions).Assembly)));

        // Built like any other singleton, which is what hands it the provider its views need.
        UiViewRegistry registry = UiViewRegistry.Register(appBuilder);
        appBuilder.AddSingleton<UiViewRegistry>(registry.Bind);

        // The style is optional: an application that gives every label an explicit font needs none.
        // The clipboard is not: every application registers one, and a missing one should be loud
        // rather than showing up as fields that quietly will not paste.
        appBuilder.AddSingleton<UiRoot>(provider =>
            new UiRoot { Style = provider.GetService<UiStyle>() ?? UiStyle.Default, Clipboard = provider.GetRequiredService<IClipboardService>(), Scale = scale });

        appBuilder.AddSingleton<UiInputSystem>(provider =>
        {
            // The window's logical size, read per event. The window is resolved here rather than inside the input system, which needs
            // the size and nothing else.
            Window window = provider.GetRequiredService<Window>();
            return new UiInputSystem(provider.GetRequiredService<UiRoot>(), () => window.Size, inputOrder, provider.GetRequiredService<IMouseService>(),
                provider.GetRequiredService<IKeyboardService>(), provider.GetRequiredService<ITextInputService>());
        });

        appBuilder.AddSingleton<UiUpdateSystem<TRenderContext>>(provider => new UiUpdateSystem<TRenderContext>(provider.GetRequiredService<UiRoot>(),
            provider.GetRequiredService<Window>(), provider.GetRequiredService<RenderContextProvider<TRenderContext>>(), updateOrder));

        appBuilder.AddSingleton<IRenderer<TRenderContext>, UiRenderer<TRenderContext>>(provider =>
            UiRenderer<TRenderContext>.Create(
                provider.GetRequiredService<UiRoot>(),
                renderOrder,
                clearTarget,
                selectColorTarget ?? (static renderContext => renderContext.ColorTarget),
                provider.GetRequiredService<GraphicsPipelineBuilder>(),
                provider.GetRequiredService<GpuMemorySystem>(),
                provider.GetRequiredService<ShaderLoader>(),
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<Window>()));

        return appBuilder;
    }
}
