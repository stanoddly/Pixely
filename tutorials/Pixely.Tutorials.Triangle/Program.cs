using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Triangle;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            //.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddZipPattern("data*.pak"))
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Game"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);
    }
}
