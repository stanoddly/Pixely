using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Hotbar;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UsePencuil()
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (1280, 720), Title: "Hotbar"));

        builder.AddSingleton(new HotbarViewModel());
        builder.AddSingleton<IPencuilView, Hotbar>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
