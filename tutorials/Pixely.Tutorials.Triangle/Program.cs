using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Triangle;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            //.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddZipPattern("data*.pak"))
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Game"));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(TriangleRenderer.Create);

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
