using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexedRenderPass;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Indexed Render Pass"));

        appBuilder.AddSingleton<IRenderer<DefaultRenderContext>>(IndexedRenderPassRenderer.Create);

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
