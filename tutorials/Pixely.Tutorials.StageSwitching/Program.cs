using Pixely.App;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Tutorials.StageSwitching;

static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(new WindowConfig(Size: (960, 540), Title: "Stage Switching"));

        builder.UseUi();
        builder.AddSingleton<UiStyle>(provider =>
            new UiStyle(provider.GetRequiredService<IFontSystem>().Load("fonts/GohuFont-Medium.ttf", 16))
            {
                Text = new TextAppearance { Foreground = new Color(235, 238, 242, 255) },
                Button = new ButtonAppearance
                {
                    Background = new StateDrawables(new SolidDrawable(new Color(62, 87, 121, 255))) { Hovered = new SolidDrawable(new Color(78, 112, 156, 255)) }
                }
            });

        builder.AddSingleton(new MenuViewModel());
        builder.AddSingleton<IUiView, MenuView>();

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }
}
