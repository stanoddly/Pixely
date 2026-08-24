using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TextInput;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultRendering(
                new WindowConfig(Size: (640, 500), Title: "Text Input"))
            .UsePencuil()
            .AddContentFromProjectDirectory("../Pixely.Tutorials.Hotbar/Content");

        builder.AddSingleton<TextInputViewModel>();
        builder.AddSingleton<IPencuilView, TextInputView>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
