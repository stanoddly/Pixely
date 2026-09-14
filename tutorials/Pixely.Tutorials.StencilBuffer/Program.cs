using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StencilBuffer;

static partial class Program
{
    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Stencil Buffer"));

        builder.AddSingleton<StencilBufferRenderer>(StencilBufferRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, StencilBufferRenderer>();
    }
}
