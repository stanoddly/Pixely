using Pixely.Gpu;
using Pixely.Ui;

namespace Pixely.Tutorials.MessageBoxes;

/// <summary>
/// Buttons only, and no view model: nothing here changes after the tree is built, so the view
/// derives from <see cref="UiView"/> directly and has nothing to synchronise.
/// </summary>
public sealed class MessageBoxView : UiView
{
    private const int ButtonWidth = 260;
    private const int ButtonHeight = 44;

    private static readonly Color BackgroundColor = new(28, 30, 34, 255);

    private readonly Window _window;

    public MessageBoxView(WindowRegistry windowRegistry)
    {
        _window = windowRegistry.GetWindow();
    }

    protected override Element BuildRoot()
    {
        Column column = new(gap: 12)
        {
            HorizontalAlignment = Alignment.Center,
            Margin = new Thickness(0, 60, 0, 0),
            Children = { new Label("Parented to the window") }
        };

        foreach (MessageBoxSeverity severity in Enum.GetValues<MessageBoxSeverity>())
        {
            column.Children.Add(Action(severity.ToString(), () =>
                _window.ShowModalMessageBox(severity, "Pixely", $"A {severity} message box parented to the window.")));
        }

        column.Children.Add(new Label("Without a window") { Margin = new Thickness(0, 20, 0, 0) });
        column.Children.Add(Action("Windowless", () =>
            MessageBox.Show(MessageBoxSeverity.Information, "Pixely", "A message box shown without a parent window.")));

        // throwing here escapes Run and is caught by the handler in Program.Main
        Button fatal = Action("Throw a fatal error", () => throw new PixelyException("This exception escapes the frame loop."));
        fatal.Margin = new Thickness(0, 12, 0, 0);
        column.Children.Add(fatal);

        return new Overlay { Background = new SolidDrawable(BackgroundColor), Children = { column } };
    }

    protected override void Synchronize()
    {
    }

    private static Button Action(string text, Action onClick)
    {
        Button button = new(text) { Width = Sizing.Fixed(ButtonWidth), Height = Sizing.Fixed(ButtonHeight) };
        button.Clicked += onClick;
        return button;
    }
}
