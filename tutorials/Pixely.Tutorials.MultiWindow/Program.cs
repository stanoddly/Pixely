using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.MultiWindow;

static class Program
{
    internal static readonly ViewScope SecondaryView = new(1);

    static int Main(string[] args)
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (640, 480), Title: "Main Window"))
            .UseDefaultRendering(
                SecondaryView,
                new WindowConfig(
                    Size: (480, 360),
                    Title: "Secondary Window",
                    InitiallyVisible: false,
                    CloseBehavior: WindowCloseBehavior.HideWindow));

        appBuilder.AddSingleton<IRenderer<DefaultRenderContext>>(PrimaryRenderer.Create);
        appBuilder.AddSingleton<IRenderer<DefaultRenderContext>>(SecondaryWindowRenderer.Create);

        appBuilder.OnStart((WindowRegistry windowRegistry, IKeyboardService keyboardService) =>
        {
            Window secondaryWindow = windowRegistry.GetWindow(SecondaryView);
            Console.WriteLine("Press Space in the main window to show or hide the secondary window.");

            keyboardService.KeyUp += eventArgs =>
            {
                if (eventArgs.Key != VirtualKey.Space)
                {
                    return;
                }

                if (secondaryWindow.IsVisible)
                {
                    secondaryWindow.Hide();
                }
                else
                {
                    secondaryWindow.Show();
                }

                eventArgs.Consume();
            };
        });

        using IPixelyApp pixelyApp = appBuilder.Build();
        return pixelyApp.Run();
    }
}
