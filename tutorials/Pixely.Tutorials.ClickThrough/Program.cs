using Pixely;
using Pixely.App;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.ClickThrough;

static partial class Program
{
    // Matches the NDC quad rendered by ClickThroughRenderer in a 400x400 window.
    // Points outside this region are excluded from the window shape, so clicks pass through to whatever is behind the window.
    static readonly Rectangle InteractiveRegion = new Rectangle(50, 50, 300, 300);

    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultContent()
            .ConfigureWindow(
                new WindowConfig(
                    Size: (400, 400),
                    Title: "Click Through",
                    Transparent: true,
                    Borderless: true))
            .UseDefaultRendering();

        builder.AddSingleton<IRenderer<BasicRenderContext>>(ClickThroughRenderer.Create);

        builder.OnBuilt((Window window, IKeyboardService keyboardService, AppControl appControl) =>
        {
            Size<uint> size = window.Size;
            BitMask shape = new(size);
            for (int y = 0; y < (int)size.Height; y++)
            {
                for (int x = 0; x < (int)size.Width; x++)
                {
                    shape[x, y] = InteractiveRegion.Intersects(new Vector2Int(x, y));
                }
            }
            window.SetShape(shape);

            keyboardService.KeyDown += e =>
            {
                if (e.Key == VirtualKey.Escape)
                {
                    appControl.Quit();
                }
            };
        });
    }
}
