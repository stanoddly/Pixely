using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.TaskbarIcon;

static partial class Program
{
    private static readonly ViewScope SecondaryView = new(1);

    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder.AddSingleton(new PixelyConfig(
            ApplicationIdentifier: "com.pixely.taskbaricon",
            TaskbarIconPath: "images/taskbar-icon.png"));
        builder
            .UseDefaultContent()
            .UseDefaultRendering(new WindowConfig(Size: (640, 480), Title: "Taskbar Icon — Main Window"))
            .UseDefaultRendering(SecondaryView, new WindowConfig(Size: (480, 360), Title: "Taskbar Icon — Secondary Window"));

        builder.OnBuilt((IKeyboardService keyboardService, AppControl appControl, PlatformInfo platformInfo) =>
        {
            Console.WriteLine($"SDL video driver: {platformInfo.SdlVideoDriver ?? "unknown"}");
            Console.WriteLine("Both windows use the taskbar icon configured by PixelyConfig.");
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
