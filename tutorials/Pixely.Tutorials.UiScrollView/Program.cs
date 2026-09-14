using Pixely.App;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Tutorials.UiScrollView;

/// <summary>
/// Content larger than the panel it sits in. A vertical list of buttons scrolls with the wheel
/// and with its bar; a horizontal strip beneath it scrolls along the other axis.
/// </summary>
static class Program
{
    private static readonly Color Background = new(24, 27, 32, 255);
    private static readonly Color Panel = new(36, 42, 52, 255);
    private static readonly Color Chip = new(70, 168, 160, 255);

    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig(Headless: args.Contains("--headless")));
        builder
            .ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddProjectDirectory("../Pixely.Tutorials.Hotbar/Content"))
            .UseDefaultRendering(new WindowConfig(Size: (640, 480), Title: "Pixely.Ui — Scroll View"));

        builder.UseUi();

        builder.AddSingleton<UiStyle>(provider =>
        {
            IFontSystem fonts = provider.GetRequiredService<IFontSystem>();
            return new UiStyle(fonts.Load("fonts/GohuFont-Medium.ttf", 16))
            {
                Title = fonts.Load("fonts/GohuFont-Medium.ttf", 20),
                Scroll = new ScrollAppearance { Track = new SolidDrawable(new Color(255, 255, 255, 20)) }
            };
        });

        builder.OnStart((AppControl appControl, IKeyboardService keyboardService) =>
        {
            Console.WriteLine("Wheel over the list, or click its bar. Escape quits.");

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
        Label picked = new("Nothing picked") { Emphasis = TextEmphasis.Muted };

        // The list scrolls vertically, which is the default. It fills the column's width and grows to
        // whatever height the column leaves it, and the buttons inside grow to its width as they
        // would in any column of a definite width.
        ScrollView list = new(gap: 4)
        {
            Width = Sizing.Grow(),
            Height = Sizing.Grow(),
            Padding = new Thickness(8),
            Background = new SolidDrawable(Panel)
        };

        for (int i = 1; i <= 40; i++)
        {
            int number = i;
            Button button = new($"Item {number}") { Width = Sizing.Grow() };
            button.Clicked += () => picked.Content = $"Picked item {number}";
            list.Children.Add(button);
        }

        // The strip scrolls the other way: a horizontal stack, and only the horizontal axis. A
        // vertical wheel over it is refused, and since the strip is beside the list rather than
        // inside it, the wheel goes on to nothing.
        ScrollView strip = new()
        {
            Axes = ScrollAxes.Horizontal,
            Width = Sizing.Grow(),
            Layout = new StackLayout(Orientation.Horizontal, 6),
            Padding = new Thickness(8),
            Background = new SolidDrawable(Panel)
        };

        for (int i = 1; i <= 30; i++)
        {
            strip.Children.Add(new Element { Width = Sizing.Fixed(48), Height = Sizing.Fixed(48), Background = new SolidDrawable(Chip) });
        }

        return new Column(gap: 12)
        {
            Background = new SolidDrawable(Background),
            Padding = new Thickness(16),
            Children =
            {
                new Label("Scroll view") { Role = TextRole.Title },
                picked,
                list,
                strip
            }
        };
    }
}
