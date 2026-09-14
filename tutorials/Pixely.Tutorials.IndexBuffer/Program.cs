using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexBuffer;

static partial class Program
{
    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Index Buffer"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(IndexBufferRenderer.Create);
    }
}
