using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexedRenderPass;

static partial class Program
{
    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Indexed Render Pass"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(IndexedRenderPassRenderer.Create);
    }
}
