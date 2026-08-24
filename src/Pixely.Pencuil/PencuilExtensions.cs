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
    public static ServiceCollection UsePencuil(
        this ServiceCollection services,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UsePencuil<DefaultRenderContext>(
            services,
            default,
            order,
            inputOrder,
            clearTarget);
    }

    public static ServiceCollection UsePencuil(
        this ServiceCollection services,
        ViewScope viewScope,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
    {
        return UsePencuil<DefaultRenderContext>(
            services,
            viewScope,
            order,
            inputOrder,
            clearTarget);
    }

    public static ServiceCollection UsePencuil<TRenderContext>(
        this ServiceCollection services,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
        where TRenderContext : IRenderContext
    {
        return UsePencuil<TRenderContext>(
            services,
            default,
            order,
            inputOrder,
            clearTarget);
    }

    public static ServiceCollection UsePencuil<TRenderContext>(
        this ServiceCollection services,
        ViewScope viewScope,
        int order = 10_000,
        int inputOrder = -10_000,
        bool clearTarget = false)
        where TRenderContext : IRenderContext
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.IsRegistered<PencuilViewRegistry>())
        {
            services.AddFileSystem(EmbeddedFileSystem.Create(typeof(PencuilExtensions).Assembly));
            services.AddSingleton(GuiStyles.Style);
            services.AddRegistry<Pencuil>();
            PencuilViewRegistry.AddPencuilViewRegistry(services);
        }

        services.AddSingleton<Pencuil>(provider =>
            new Pencuil(
                viewScope,
                new Pencil(
                    provider.GetRequiredService<IFontSystem>(),
                    provider.GetRequiredService<IClipboardService>(),
                    provider.GetRequiredService<GuiStyle>())));

        services.AddSingleton<IRenderer<TRenderContext>, PencuilRenderer<TRenderContext>>(provider =>
            new PencuilRenderer<TRenderContext>(
                Pencuil.GetRequired(provider, viewScope),
                order,
                clearTarget,
                provider.GetRequiredService<GraphicsPipelineBuilder>(),
                provider.GetRequiredService<GpuMemorySystem>(),
                provider.GetRequiredService<ShaderLoader>(),
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<WindowRegistry>()));
        services.AddSingleton<PencilSystem>(provider =>
            new PencilSystem(
                Pencuil.GetRequired(provider, viewScope),
                inputOrder,
                provider.GetRequiredService<PencuilViewRegistry>(),
                provider.GetRequiredService<WindowRegistry>(),
                provider.GetRequiredService<IMouseService>(),
                provider.GetRequiredService<IKeyboardService>(),
                provider.GetRequiredService<ITextInputService>()));
        return services;
    }
}
