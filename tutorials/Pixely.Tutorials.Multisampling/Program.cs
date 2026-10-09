using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Multisampling;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Multisampling"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(MultisamplingRenderer.Create);
    }
}
