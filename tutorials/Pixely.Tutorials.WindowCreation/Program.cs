using Pixely.App;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.WindowCreation;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            //.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddZipPattern("data*.pak").AddProjectDirectory("_Content"))
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Game"));
    }
}
