using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.WindowCreation;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            //.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddZipPattern("data*.pak").AddProjectDirectory("_Content"))
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Game"));

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
