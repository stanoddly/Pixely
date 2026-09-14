using Pixely.App;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Tutorials.MessageBoxes;

static partial class Program
{
    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(new WindowConfig(Size: (960, 540), Title: "Message Box"));

        builder.UseUi();
        builder.AddSingleton<UiStyle>(provider =>
            new UiStyle(provider.GetRequiredService<IFontSystem>().Load("fonts/GohuFont-Medium.ttf", 16))
            {
                Text = new TextAppearance { Foreground = new Color(235, 238, 242, 255) }
            });

        builder.AddSingleton<IUiView, MessageBoxView>();
    }

    // Pixely does not report failures on its own. Implementing this optional partial method makes
    // the generated Main pass a failure from Configure, Build or Run here and return 1; without it
    // the exception propagates. It runs before the application is disposed, so a failure during
    // Run still has the window open behind the box, while a failure during Build has no window
    // yet, which is why the box is shown without a parent.
    static partial void OnException(Exception exception)
    {
        Console.Error.WriteLine(exception);

        try
        {
            MessageBox.Show(MessageBoxSeverity.Error, "Fatal error", exception.Message);
        }
        catch (Exception messageBoxException)
        {
            // a message box is unavailable on a headless system, where stderr is the only report
            Console.Error.WriteLine(messageBoxException);
        }
    }
}
