using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StencilBuffer;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Stencil Buffer"));

        appBuilder.AddSingleton<StencilBufferRenderer>(StencilBufferRenderer.Create);
        appBuilder.AddAlias<IRenderer<DefaultRenderContext>, StencilBufferRenderer>();

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
