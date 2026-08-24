using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.StageSwitching;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultRendering(
                new WindowConfig(Size: (960, 540), Title: "Stage Switching"))
            .UsePencuil()
            .AddContentFromProjectDirectory("../Pixely.Tutorials.Hotbar/Content");

        builder.AddSingleton<IPencuilView, MenuView>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
