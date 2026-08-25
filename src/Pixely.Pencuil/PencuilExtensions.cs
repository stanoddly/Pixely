using Pixely.App;
using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Shaders;
using Pixely.Text;

namespace Pixely.Pencuil;

public static class PencuilExtensions
{
    public static PixelyAppBuilder UsePencuil(
        this PixelyAppBuilder appBuilder,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UsePencuil<DefaultRenderContext>(
            appBuilder,
            default,
            order,
            inputOrder,
            clearTarget);
    }

    public static PixelyAppBuilder UsePencuil(
        this PixelyAppBuilder appBuilder,
        ViewScope viewScope,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UsePencuil<DefaultRenderContext>(
            appBuilder,
            viewScope,
            order,
            inputOrder,
            clearTarget);
    }

    public static PixelyAppBuilder UsePencuil<TRenderContext>(
        this PixelyAppBuilder appBuilder,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
        where TRenderContext : IRenderContext
    {
        return UsePencuil<TRenderContext>(
            appBuilder,
            default,
            order,
            inputOrder,
            clearTarget);
    }

    public static PixelyAppBuilder UsePencuil<TRenderContext>(
        this PixelyAppBuilder appBuilder,
        ViewScope viewScope,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(appBuilder);

        if (!appBuilder.IsRegistered<PencuilViewRegistry>())
        {
            appBuilder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(EmbeddedContentSource.Create(typeof(PencuilExtensions).Assembly)));
            appBuilder.AddSingleton(GuiStyles.Style);
            appBuilder.AddRegistry<Pencuil>();
            PencuilViewRegistry.AddPencuilViewRegistry(appBuilder);
        }

        appBuilder.AddSingleton<Pencuil>(provider =>
            new Pencuil(
                viewScope,
                new Pencil(
                    provider.GetRequiredService<IFontSystem>(),
                    provider.GetRequiredService<IClipboardService>(),
                    provider.GetRequiredService<GuiStyle>())));

        appBuilder.AddSingleton<IRenderer<TRenderContext>, PencuilRenderer<TRenderContext>>(provider =>
            new PencuilRenderer<TRenderContext>(
                Pencuil.GetRequired(provider, viewScope),
                order,
                clearTarget,
                provider.GetRequiredService<GraphicsPipelineBuilder>(),
                provider.GetRequiredService<GpuMemorySystem>(),
                provider.GetRequiredService<ShaderLoader>(),
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<WindowRegistry>()));
        appBuilder.AddSingleton<PencilSystem>(provider =>
            new PencilSystem(
                Pencuil.GetRequired(provider, viewScope),
                inputOrder,
                provider.GetRequiredService<PencuilViewRegistry>(),
                provider.GetRequiredService<WindowRegistry>(),
                provider.GetRequiredService<IMouseService>(),
                provider.GetRequiredService<IKeyboardService>(),
                provider.GetRequiredService<ITextInputService>()));
        return appBuilder;
    }
}
