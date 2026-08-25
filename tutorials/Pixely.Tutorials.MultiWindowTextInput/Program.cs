using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;
using Pixely.Text;

namespace Pixely.Tutorials.MultiWindowTextInput;

static class Program
{
    internal static readonly ViewScope LeftView = new(0);
    internal static readonly ViewScope RightView = new(1);

    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UsePencuil(LeftView, clearTarget: true)
            .UsePencuil(RightView, clearTarget: true)
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(
                LeftView,
                new WindowConfig(Size: (520, 300), Title: "Left text input"))
            .UseDefaultRendering(
                RightView,
                new WindowConfig(Size: (520, 300), Title: "Right text input"));

        builder.AddSingleton<IPencuilView>(provider =>
            new TextInputView(
                LeftView,
                "Left View",
                new TextInputViewModel("left"),
                provider.GetRequiredService<IFontSystem>()));
        builder.AddSingleton<IPencuilView>(provider =>
            new TextInputView(
                RightView,
                "Right View",
                new TextInputViewModel("right"),
                provider.GetRequiredService<IFontSystem>()));

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
