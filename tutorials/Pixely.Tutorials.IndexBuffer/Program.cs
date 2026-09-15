using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexBuffer;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Index Buffer"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(IndexBufferRenderer.Create);
    }
}
