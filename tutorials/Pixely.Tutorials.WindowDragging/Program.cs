using System.Numerics;
using Pixely;
using Pixely.App;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.WindowDragging;

static partial class Program
{
    static void Configure(PixelyAppBuilder builder)
    {
        builder
            .UseDefaultRendering(
                new WindowConfig(
                    Size: (400, 400),
                    Title: "Window Dragging",
                    Borderless: true));

        builder.AddSingleton<IRenderer<BasicRenderContext>>(static () => new ClearRenderer(FColors.SkyBlue));

        builder.OnBuilt((WindowRegistry windowRegistry, IMouseService mouseService, IKeyboardService keyboardService, UpdateSystem updateSystem, AppControl appControl) =>
        {
            Window window = windowRegistry.GetWindow();

            if (window.SupportsSetWindowPosition)
            {
                Console.WriteLine("Active window dragging path: programmatic positioning");
                Console.WriteLine("Hold the middle mouse button to drag the window.");

                bool wasMiddleButtonPressed = false;
                Vector2 initialCursorPosition = default;
                Vector2 initialWindowPosition = default;

                updateSystem.Add(() =>
                {
                    MouseState mouseState = mouseService.GetGlobalState();
                    bool middleButtonPressed = mouseState.IsPressed(MouseButton.Middle);

                    if (middleButtonPressed && !wasMiddleButtonPressed)
                    {
                        initialCursorPosition = mouseState.Position;
                        initialWindowPosition = window.Position;
                    }

                    if (middleButtonPressed)
                    {
                        Vector2 targetPosition = initialWindowPosition
                            + mouseState.Position
                            - initialCursorPosition;

                        window.Position = new Vector2Int(
                            (int)MathF.Round(targetPosition.X),
                            (int)MathF.Round(targetPosition.Y));
                    }

                    wasMiddleButtonPressed = middleButtonPressed;
                });
            }
            else
            {
                Console.WriteLine("Active window dragging path: native window-manager dragging");
                Console.WriteLine("Hold Ctrl and drag with the left mouse button.");

                keyboardService.KeyDown += eventArgs =>
                {
                    if (eventArgs.Scancode == Scancode.LeftCtrl || eventArgs.Scancode == Scancode.RightCtrl)
                    {
                        window.Draggable = eventArgs.Keyboard.Ctrl;
                    }
                };

                keyboardService.KeyUp += eventArgs =>
                {
                    if (eventArgs.Scancode == Scancode.LeftCtrl || eventArgs.Scancode == Scancode.RightCtrl)
                    {
                        window.Draggable = eventArgs.Keyboard.Ctrl;
                    }
                };
            }

            Console.WriteLine("Right mouse button or Escape: quit");

            mouseService.ButtonPress += eventArgs =>
            {
                if (eventArgs.Button == MouseButton.Right)
                {
                    appControl.Quit();
                }
            };

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

internal sealed class ClearRenderer : IRenderer<BasicRenderContext>
{
    private readonly FColor _color;


    public ClearRenderer(FColor color)
    {
        _color = color;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using RenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(new ColorTargetSettings
            {
                ClearColorValue = _color,
                LoadOperation = LoadOperation.Clear
            })
            .Build();
    }
}
