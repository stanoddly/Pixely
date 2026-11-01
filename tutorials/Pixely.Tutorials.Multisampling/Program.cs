using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Multisampling;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (800, 600), Title: "Multisampling"))
            .UseDefaultRendering();

        builder.AddSingleton<IRenderer<BasicRenderContext>>(MultisamplingRenderer.Create);
    }
}
