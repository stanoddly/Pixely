using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Gpu;
using Pixely.Ui;

namespace Pixely.Tutorials.UiTextInput;

/// <summary>
/// Editable fields in a retained UI. The immediate-mode version of this tutorial lives in
/// <c>Pixely.Tutorials.TextInput</c>; the difference is that a field here is an element holding its
/// own edit, so nothing is passed by reference through a build that runs every frame. Focus belongs
/// to the root, which tells the field when it has it.
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(new WindowConfig(Size: (640, 500), Title: "Pixely.Ui — Text Input"));

        builder.UseUi();
        builder.AddSingleton(new SettingsViewModel());

        // Fields take their font, their caret colour and the colour they paint a selection in from
        // the style, so nothing below has to be told about any of them.
        builder.AddSingleton<UiStyle>(provider =>
        {
            IFontSystem fonts = provider.GetRequiredService<IFontSystem>();
            return new UiStyle(fonts.Load("fonts/GohuFont-Medium.ttf", 16))
            {
                Title = fonts.Load("fonts/GohuFont-Medium.ttf", 20),
                Text = new TextAppearance
                {
                    Foreground = new Color(235, 238, 242, 255),
                    Muted = new Color(150, 162, 180, 255),
                    Accent = new Color(233, 138, 76, 255)
                }
            };
        });

        builder.OnStart((AppControl appControl, IKeyboardService keyboardService) =>
        {
            Console.WriteLine("Click a field to edit it. Enter or clicking away commits a valid value, Escape cancels, Escape outside a field quits.");

            keyboardService.KeyDown += eventArgs =>
            {
                // The UI subscribes ahead of this and consumes the keys it uses. A field being
                // edited uses Escape to cancel, so Escape reaches here only when none is — which is
                // what keeps one press from both cancelling and quitting. Keys a field does not use
                // still come through.
                // Repeats are ignored because cancelling an edit releases focus: holding Escape
                // would otherwise cancel on the first event and quit on the next one.
                if (eventArgs.Key == VirtualKey.Escape && !eventArgs.Repeat)
                {
                    appControl.Quit();
                }
            };
        });

        using IPixelyApp pixelyApp = builder.Build();

        SettingsView view = new(
            pixelyApp.ServiceProvider.GetRequiredService<SettingsViewModel>(),
            pixelyApp.ServiceProvider.GetRequiredService<IClipboardService>());

        pixelyApp.ServiceProvider.GetUiRoot().AddView(view);

        return pixelyApp.Run();
    }
}
