using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StorageBuffer;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (800, 600), Title: "Storage Buffer Demo"))
            .UseDefaultRendering();

        builder.AddSingleton<StorageBufferRenderer>(StorageBufferRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, StorageBufferRenderer>();
    }
}
