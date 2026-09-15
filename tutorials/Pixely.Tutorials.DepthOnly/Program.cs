using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.DepthOnly;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Depth-Only Pipeline Test"));

        builder.AddSingleton<DepthOnlyRenderer>(DepthOnlyRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, DepthOnlyRenderer>();
    }
}
