using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Ui;

namespace Pixely.Tutorials.UiBoxes;

/// <summary>
/// Exercises the whole Pixely.Ui render path without any input: the box model, Grow and Percent
/// sizing, nested clipping and the single-pipeline batching. Everything here is static, so what
/// appears on screen is decided entirely by layout and painting.
/// </summary>
static class Program
{
    private static readonly Color Background = new(24, 27, 32, 255);
    private static readonly Color Panel = new(52, 62, 78, 255);
    private static readonly Color Accent = new(233, 138, 76, 255);
    private static readonly Color Teal = new(70, 168, 160, 255);
    private static readonly Color Pale = new(226, 232, 240, 255);

    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultContent()
            .UseDefaultRendering(new WindowConfig(Size: (800, 600), Title: "Pixely.Ui — Static Boxes"));

        builder.UseUi();

        builder.OnStart((IKeyboardService keyboardService, AppControl appControl) =>
        {
            Console.WriteLine("Static Pixely.Ui layout. Press Escape to quit.");

            keyboardService.KeyDown += eventArgs =>
            {
                if (eventArgs.Key == VirtualKey.Escape)
                {
                    appControl.Quit();
                }
            };
        });

        using IPixelyApp pixelyApp = builder.Build();
        pixelyApp.ServiceProvider.GetUiRoot().AddLayer(BuildUi());
        return pixelyApp.Run();
    }

    private static Element BuildUi()
    {
        return new Column(gap: 12)
        {
            Background = new SolidDrawable(Background),
            Padding = new Thickness(16),
            Children =
            {
                // A fixed-height header spanning the full width.
                new Column
                {
                    Background = new SolidDrawable(Accent),
                    Height = Sizing.Fixed(48),
                    Width = Sizing.Grow()
                },

                // Sidebar fixed, content fills — the case cursor-based layout cannot express.
                new Row(gap: 12)
                {
                    Height = Sizing.Grow(),
                    Width = Sizing.Grow(),
                    Children =
                    {
                        new Column
                        {
                            Background = new SolidDrawable(Panel),
                            Width = Sizing.Fixed(180),
                            Height = Sizing.Grow()
                        },
                        new Column(gap: 12)
                        {
                            Width = Sizing.Grow(),
                            Height = Sizing.Grow(),
                            Children =
                            {
                                // Two halves of the remaining width, each exactly 50%.
                                new Row(gap: 12)
                                {
                                    Height = Sizing.Fixed(120),
                                    Width = Sizing.Grow(),
                                    Children =
                                    {
                                        new Column { Background = new SolidDrawable(Teal), Width = Sizing.Percent(0.5f), Height = Sizing.Grow() },
                                        new Column { Background = new SolidDrawable(Panel), Width = Sizing.Grow(), Height = Sizing.Grow() }
                                    }
                                },

                                // A deliberately oversized child inside a clipper: only the top
                                // left corner of the pale box may appear.
                                new ClipBorder
                                {
                                    Width = Sizing.Fixed(200),
                                    Height = Sizing.Fixed(100),
                                    Content = new Column
                                    {
                                        Background = new SolidDrawable(Pale),
                                        Width = Sizing.Fixed(600),
                                        Height = Sizing.Fixed(400)
                                    }
                                },

                                // Weighted grow: 1 / 2 / 1 of the leftover height.
                                new Row(gap: 8)
                                {
                                    Height = Sizing.Grow(),
                                    Width = Sizing.Grow(),
                                    Children =
                                    {
                                        new Column { Background = new SolidDrawable(Panel), Width = Sizing.Grow(1f), Height = Sizing.Grow() },
                                        new Column { Background = new SolidDrawable(Teal), Width = Sizing.Grow(2f), Height = Sizing.Grow() },
                                        new Column { Background = new SolidDrawable(Panel), Width = Sizing.Grow(1f), Height = Sizing.Grow() }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
