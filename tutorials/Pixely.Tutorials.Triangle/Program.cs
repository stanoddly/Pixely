using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Triangle;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            //.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddZipPattern("data*.pak"))
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Game"));

        appBuilder.AddSingleton<IRenderer<DefaultRenderContext>>(TriangleRenderer.Create);

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
