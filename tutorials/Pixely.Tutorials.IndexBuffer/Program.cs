using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexBuffer;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (1280, 720), Title: "Index Buffer"))
            .UseDefaultRendering();

        builder.AddSingleton<IRenderer<BasicRenderContext>>(IndexBufferRenderer.Create);
    }
}
