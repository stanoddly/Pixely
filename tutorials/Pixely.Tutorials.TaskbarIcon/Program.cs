using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TaskbarIcon;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder.AddSingleton(new PixelyConfig(
            ApplicationIdentifier: "com.pixely.taskbaricon",
            TaskbarIconPath: "images/taskbar-icon.png"));
        builder
            .UseDefaultContent()
            .ConfigureWindow(new WindowConfig(Size: (640, 480), Title: "Taskbar Icon"))
            .UseDefaultRendering();

        builder.OnBuilt((IKeyboardService keyboardService, AppControl appControl, PlatformInfo platformInfo) =>
        {
            Console.WriteLine($"SDL video driver: {platformInfo.SdlVideoDriver ?? "unknown"}");
            Console.WriteLine("The window uses the taskbar icon configured by PixelyConfig.");
            Console.WriteLine("Press Escape to quit.");

            keyboardService.KeyDown += eventArgs =>
            {
                if (eventArgs.Key == VirtualKey.Escape)
                {
                    appControl.Quit();
                }
            };
        });
    }
}
