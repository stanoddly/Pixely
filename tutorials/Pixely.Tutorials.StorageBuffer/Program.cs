using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StorageBuffer;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .AddContentFromProjectDirectory("Content")
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Storage Buffer Demo"));

        builder.AddSingleton<StorageBufferRenderer>(StorageBufferRenderer.Create);
        builder.AddAlias<IRenderer<DefaultRenderContext>, StorageBufferRenderer>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
