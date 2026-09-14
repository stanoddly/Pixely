using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.DepthOnly;

static partial class Program
{
    static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (800, 600), Title: "Depth-Only Pipeline Test"));

        builder.AddSingleton<DepthOnlyRenderer>(DepthOnlyRenderer.Create);
        builder.AddAlias<IRenderer<BasicRenderContext>, DepthOnlyRenderer>();
    }
}
