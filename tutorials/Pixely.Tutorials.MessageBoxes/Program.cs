using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.MessageBoxes;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UsePencuil()
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(
                new WindowConfig(Size: (960, 540), Title: "Message Box"));

        builder.AddSingleton<IPencuilView, MessageBoxView>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
