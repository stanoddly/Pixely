using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Hotbar;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UsePencuil()
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Hotbar"));

        appBuilder.AddSingleton(new HotbarViewModel());
        appBuilder.AddSingleton<IPencuilView, Hotbar>();

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
