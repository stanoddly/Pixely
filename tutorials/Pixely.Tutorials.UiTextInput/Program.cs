using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Tutorials.UiTextInput;

/// <summary>
/// Editable fields in a retained UI. The immediate-mode version of this tutorial lives in
/// <c>Pixely.Tutorials.TextInput</c>; the difference is that a field here keeps its own edit and
/// its own focus, so nothing is passed by reference through a build that runs every frame.
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
                Title = fonts.Load("fonts/GohuFont-Medium.ttf", 20)
            };
        });

        builder.OnStart((AppControl appControl, IKeyboardService keyboardService) =>
        {
            Console.WriteLine("Click a field to edit it. Enter or clicking away commits, Escape cancels, Escape outside a field quits.");

            keyboardService.KeyDown += eventArgs =>
            {
                // The UI sees keys first and consumes the ones it uses, so this only runs when no
                // field is being edited — which is what keeps Escape from both cancelling and quitting.
                if (eventArgs.Key == VirtualKey.Escape)
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
