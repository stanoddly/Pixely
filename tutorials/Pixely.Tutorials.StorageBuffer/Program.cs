using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StorageBuffer;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Storage Buffer Demo"));

        appBuilder.AddSingleton<StorageBufferRenderer>(StorageBufferRenderer.Create);
        appBuilder.AddAlias<IRenderer<DefaultRenderContext>, StorageBufferRenderer>();

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
