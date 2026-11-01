using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.ImageLoading;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (443, 410), Title: "Image Loading Demo"))
            .UseDefaultRendering();

        builder.AddSingleton<ImageLoadingRenderer>(ImageLoadingRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, ImageLoadingRenderer>();
    }
}
