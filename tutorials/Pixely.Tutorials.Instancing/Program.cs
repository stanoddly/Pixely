using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Instancing;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (800, 600), Title: "Instancing Demo"))
            .UseDefaultRendering();

        builder.AddSingleton<InstancingRenderer>(InstancingRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, InstancingRenderer>();
    }
}
