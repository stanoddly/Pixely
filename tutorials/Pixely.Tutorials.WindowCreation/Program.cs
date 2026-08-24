using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.WindowCreation;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            //.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddZipPattern("data*.pak").AddProjectDirectory("_Content"))
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Game"));

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
