using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.IndexBuffer;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Index Buffer"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(IndexBufferRenderer.Create);

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
