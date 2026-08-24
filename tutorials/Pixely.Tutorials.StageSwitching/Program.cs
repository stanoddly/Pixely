using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StageSwitching;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UsePencuil()
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(
                new WindowConfig(Size: (960, 540), Title: "Stage Switching"));

        appBuilder.AddSingleton<IPencuilView, MenuView>();

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
