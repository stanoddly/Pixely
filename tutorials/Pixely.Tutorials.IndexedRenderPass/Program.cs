using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexedRenderPass;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Indexed Render Pass"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(IndexedRenderPassRenderer.Create);

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
