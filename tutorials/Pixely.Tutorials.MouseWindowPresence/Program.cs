using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.MouseWindowPresence;

static partial class Program
{
    private static partial void Configure(PixelyAppBuilder builder, string[] args)
    {
        builder
            .UseDefaultRendering(
                new WindowConfig(Size: (640, 480), Title: "Mouse Window Presence"));

        builder.OnBuilt((IMouseService mouseService) =>
        {
            Console.WriteLine($"Mouse starts in window: {mouseService.IsInWindow()}");
            Console.WriteLine("Move the mouse into and out of the window to see enter and leave events.");

            mouseService.WindowEnter += eventArgs =>
            {
                Console.WriteLine($"Mouse entered window at {eventArgs.Timestamp}. IsInWindow: {mouseService.IsInWindow()}");
            };

            mouseService.WindowLeave += eventArgs =>
            {
                Console.WriteLine($"Mouse left window at {eventArgs.Timestamp}. IsInWindow: {mouseService.IsInWindow()}");
            };
        });
    }
}
