using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.ImageLoading;

static partial class Program
{
    static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (443, 410), Title: "Image Loading Demo"));

        builder.AddSingleton<ImageLoadingRenderer>(ImageLoadingRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, ImageLoadingRenderer>();
    }
}
