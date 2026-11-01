using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Triangle;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            //.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddZipPattern("data*.pak"))
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (1280, 720), Title: "Game"))
            .UseDefaultRendering();

        builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);
    }
}
