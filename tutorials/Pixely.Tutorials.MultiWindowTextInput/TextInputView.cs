using Pixely.Gpu;
using Pixely.Pencuil;
using Pixely.Text;

namespace Pixely.Tutorials.MultiWindowTextInput;

public sealed class TextInputViewModel : IPencuilViewModel
{
    private string _text;

    public bool IsDirty { get; set; } = true;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                IsDirty = true;
            }
        }
    }

    public TextInputViewModel(string text)
    {
        _text = text;
    }
}

public sealed class TextInputView : PencuilView<TextInputViewModel>
{
    private static readonly Color _backgroundColor = new(28, 30, 34, 255);
    private static readonly Color _labelColor = new(180, 180, 180, 255);
    private static readonly Color _valueColor = new(235, 238, 242, 255);

    private readonly string _name;
    private readonly Font _font;

    public TextInputView(
        ViewScope viewScope,
        string name,
        TextInputViewModel viewModel,
        IFontSystem fontSystem)
        : base(viewScope, viewModel)
    {
        _name = name;
        _font = fontSystem.Load("fonts/GohuFont-Medium.ttf", 16);
    }

    public override void Build(Pencil pencil)
    {
        pencil.MoveTo(0, 0);
        pencil.Rectangle(pencil.BottomRight.X, pencil.BottomRight.Y, _backgroundColor);

        int x = pencil.Center.X - 180;
        int y = 70;
        pencil.MoveTo(x, y);
        pencil.Text(_name, _font, _labelColor);

        string text = ViewModel.Text;
        pencil.MoveTo(x, y + 45);
        if (pencil.TextField(0, ref text, _font, 360))
        {
            ViewModel.Text = text;
        }

        pencil.MoveTo(x, y + 100);
        pencil.Text($"This window contains: {ViewModel.Text}", _font, _valueColor);

        pencil.MoveTo(x, y + 145);
        pencil.Text("Input is routed through this ViewScope.", _font, _labelColor);
    }
}
