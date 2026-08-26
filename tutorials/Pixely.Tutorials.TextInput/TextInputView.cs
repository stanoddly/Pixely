using System.Globalization;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Pencuil;
using Pixely.Text;

namespace Pixely.Tutorials.TextInput;

public class TextInputViewModel : IPencuilViewModel
{
    private readonly IClipboardService _clipboardService;

    public bool IsDirty { get; set; } = true;

    private string _name = "Player";
    private int _width = 64;
    private int _height = 48;
    private float _scale = 1f;

    public TextInputViewModel(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                IsDirty = true;
            }
        }
    }

    public int Width
    {
        get => _width;
        set
        {
            if (_width != value)
            {
                _width = value;
                IsDirty = true;
            }
        }
    }

    public int Height
    {
        get => _height;
        set
        {
            if (_height != value)
            {
                _height = value;
                IsDirty = true;
            }
        }
    }

    public float Scale
    {
        get => _scale;
        set
        {
            if (_scale != value)
            {
                _scale = value;
                IsDirty = true;
            }
        }
    }

    public string ClipboardText => _clipboardService.HasText ? (_clipboardService.GetText() ?? "") : "";
}

public class TextInputView : PencuilView<TextInputViewModel>
{
    private static readonly Color BackgroundColor = new(28, 30, 34, 255);
    private static readonly Color LabelColor = new(180, 180, 180, 255);
    private static readonly Color ValueColor = new(235, 238, 242, 255);

    private readonly Font _font;
    private readonly Font _labelFont;

    public TextInputView(TextInputViewModel viewModel, IFontSystem fontSystem)
        : base(viewModel)
    {
        _font = fontSystem.Load("fonts/GohuFont-Medium.ttf", 16);
        _labelFont = fontSystem.Load("fonts/GohuFont-Medium.ttf", 14);
    }

    public override void Build(Pencil pencil)
    {
        pencil.MoveTo(0, 0);
        pencil.Rectangle(pencil.BottomRight.X, pencil.BottomRight.Y, BackgroundColor);

        int startX = pencil.Center.X - 120;
        int startY = 80;

        pencil.MoveTo(startX, 40);
        pencil.Text("Enter or click away commits; Escape cancels.", _labelFont, LabelColor);

        using (pencil.WithGap(12))
        using (pencil.WithDirection(LayoutDirection.Bottom))
        {
            pencil.MoveTo(startX, startY);

            pencil.Text("Name", _labelFont, LabelColor);

            string name = ViewModel.Name;
            if (pencil.TextField(0, ref name, _font, 240))
            {
                ViewModel.Name = name;
            }

            pencil.Text("Width", _labelFont, LabelColor);

            int width = ViewModel.Width;
            if (pencil.NumberField(1, ref width, _font, 240, CultureInfo.InvariantCulture))
            {
                ViewModel.Width = width;
            }

            pencil.Text("Height", _labelFont, LabelColor);

            int height = ViewModel.Height;
            if (pencil.NumberField(2, ref height, _font, 240, CultureInfo.InvariantCulture))
            {
                ViewModel.Height = height;
            }

            pencil.Text("Scale", _labelFont, LabelColor);

            float scale = ViewModel.Scale;
            if (pencil.NumberField(3, ref scale, _font, 240, CultureInfo.InvariantCulture))
            {
                ViewModel.Scale = scale;
            }
        }

        pencil.MoveTo(startX, startY + 330);
        string scaleText = ViewModel.Scale.ToString(CultureInfo.InvariantCulture);
        pencil.Text(
            $"Name: {ViewModel.Name}  Size: {ViewModel.Width}x{ViewModel.Height}  Scale: {scaleText}",
            _font,
            ValueColor);

        string clipboardText = ViewModel.ClipboardText;
        if (clipboardText.Length > 0)
        {
            pencil.MoveTo(startX, startY + 360);
            pencil.Text($"Clipboard: {clipboardText}", _labelFont, LabelColor);
        }
    }
}
