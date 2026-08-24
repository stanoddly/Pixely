using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TextInput;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UsePencuil()
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(
                new WindowConfig(Size: (640, 500), Title: "Text Input"));

        appBuilder.AddSingleton<TextInputViewModel>();
        appBuilder.AddSingleton<IPencuilView, TextInputView>();

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
