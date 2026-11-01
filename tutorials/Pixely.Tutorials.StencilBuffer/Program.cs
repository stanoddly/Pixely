using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StencilBuffer;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (1280, 720), Title: "Stencil Buffer"))
            .UseDefaultRendering();

        builder.AddSingleton<StencilBufferRenderer>(StencilBufferRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, StencilBufferRenderer>();
    }
}
