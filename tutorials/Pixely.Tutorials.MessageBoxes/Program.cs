using Pixely.App;
using Pixely.Pencuil;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.MessageBoxes;

static class Program
{
    static int Main(string[] args)
    {
        try
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
        catch (Exception exception)
        {
            ReportFatalError(exception);
            return 1;
        }
    }

    // Pixely does not report failures on its own, so an application that wants a player-visible
    // message installs its own handler. Build and Run are both covered: a failure during Build
    // happens before a window exists, which is why the box is shown without a parent.
    private static void ReportFatalError(Exception exception)
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
