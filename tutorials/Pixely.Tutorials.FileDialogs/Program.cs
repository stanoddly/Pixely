using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.FileDialogs;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultRendering(
                new WindowConfig(Size: (960, 540), Title: "File Dialogs"))
            .UsePencuil()
            .AddContentFromProjectDirectory("../Pixely.Tutorials.Hotbar/Content");

        builder.AddSingleton(new FileDialogsViewModel());
        builder.AddSingleton<IPencuilView, FileDialogsView>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
