using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Instancing;

static partial class Program
{
    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Instancing Demo"));

        builder.AddSingleton<InstancingRenderer>(InstancingRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, InstancingRenderer>();
    }
}
