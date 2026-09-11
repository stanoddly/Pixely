using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Tutorials.MultiWindowTextInput;

/// <summary>
/// One <see cref="UiRoot"/> per window, each with its own focus and its own text input. A view
/// says which root it belongs to through its <see cref="IUiView.ViewScope"/>, so registering a
/// view is the same line whichever window it is for.
/// </summary>
static class Program
{
    internal static readonly ViewScope LeftView = new(0);
    internal static readonly ViewScope RightView = new(1);

    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(LeftView, new WindowConfig(Size: (520, 300), Title: "Left text input"))
            .UseDefaultRendering(RightView, new WindowConfig(Size: (520, 300), Title: "Right text input"));

        builder.UseUi(LeftView, clearTarget: true);
        builder.UseUi(RightView, clearTarget: true);

        // One style serves both roots: each root reads it from the container when it is built.
        builder.AddSingleton<UiStyle>(provider =>
            new UiStyle(provider.GetRequiredService<IFontSystem>().Load("fonts/GohuFont-Medium.ttf", 16))
            {
                Text = new TextAppearance { Foreground = new Color(235, 238, 242, 255), Muted = new Color(180, 180, 180, 255) }
            });

        builder.AddSingleton<IUiView>(new TextInputView(LeftView, "Left View", new TextInputViewModel("left")));
        builder.AddSingleton<IUiView>(new TextInputView(RightView, "Right View", new TextInputViewModel("right")));

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
