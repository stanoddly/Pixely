using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.WindowCreation;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            //.AddContentFromZipPattern("data*.pak")
            //.AddContentFromProjectDirectory("_Content")
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Game"));

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
