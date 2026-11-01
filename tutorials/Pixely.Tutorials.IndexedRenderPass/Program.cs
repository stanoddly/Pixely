using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexedRenderPass;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (1280, 720), Title: "Indexed Render Pass"))
            .UseDefaultRendering();

        builder.AddSingleton<IRenderer<BasicRenderContext>>(IndexedRenderPassRenderer.Create);
    }
}
