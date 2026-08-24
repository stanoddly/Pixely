using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Instancing;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Instancing Demo"));

        appBuilder.AddSingleton<InstancingRenderer>(InstancingRenderer.Create);
        appBuilder.AddAlias<IRenderer<DefaultRenderContext>, InstancingRenderer>();

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
