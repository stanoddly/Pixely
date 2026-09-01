using Pixely.Gpu;
using Pixely.Pencuil;
using Pixely.Text;

namespace Pixely.Tutorials.MessageBoxes;

public class MessageBoxView : IPencuilView
{
    private const int ButtonWidth = 260;
    private const int ButtonHeight = 44;
    private const int ButtonGap = 12;

    private static readonly Color BackgroundColor = new(28, 30, 34, 255);
    private static readonly Color TextColor = new(235, 238, 242, 255);

    private readonly Window _window;
    private readonly Font _font;
    private bool _dirty = true;

    public MessageBoxView(WindowRegistry windowRegistry, IFontSystem fontSystem)
    {
        _window = windowRegistry.GetWindow();
        _font = fontSystem.Load("fonts/GohuFont-Medium.ttf", 16);
    }

    public bool ConsumeDirty()
    {
        bool dirty = _dirty;
        _dirty = false;
        return dirty;
    }

    public void Build(Pencil pencil)
    {
        pencil.MoveTo(0, 0);
        pencil.Rectangle(pencil.BottomRight.X, pencil.BottomRight.Y, BackgroundColor);

        int x = pencil.Center.X - ButtonWidth / 2;
        int y = 60;

        DrawLabel(pencil, x, y, "Parented to the window");
        y += 28;

        foreach (MessageBoxSeverity severity in Enum.GetValues<MessageBoxSeverity>())
        {
            pencil.MoveTo(x, y);
            if (pencil.Button(severity.ToString(), _font, ButtonWidth, ButtonHeight))
            {
                _window.ShowModalMessageBox(severity, "Pixely", $"A {severity} message box parented to the window.");
            }

            y += ButtonHeight + ButtonGap;
        }

        y += 20;
        DrawLabel(pencil, x, y, "Without a window");
        y += 28;

        pencil.MoveTo(x, y);
        if (pencil.Button("Windowless", _font, ButtonWidth, ButtonHeight))
        {
            MessageBox.Show(MessageBoxSeverity.Information, "Pixely", "A message box shown without a parent window.");
        }

        y += ButtonHeight + ButtonGap * 2;

        // throwing here leaves the frame loop, so PixelyApp.Run reports it and rethrows
        pencil.MoveTo(x, y);
        if (pencil.Button("Throw a fatal error", _font, ButtonWidth, ButtonHeight))
        {
            throw new PixelyException("This exception escapes the frame loop.");
        }
    }

    private void DrawLabel(Pencil pencil, int x, int y, string text)
    {
        pencil.MoveTo(x, y);
        pencil.Text(text, _font, TextColor);
    }
}
