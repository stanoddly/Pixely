using System.Globalization;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Ui;

namespace Pixely.Tutorials.UiTextInput;

/// <summary>
/// The retained counterpart of <c>Pixely.Tutorials.TextInput</c>. The immediate-mode version passes
/// each value in and out of a <c>TextField</c> call every build; here the fields are elements that
/// hold their own edit, and the view only hears about one when it commits.
/// </summary>
public sealed class SettingsView : UiView<SettingsViewModel>
{
    private static readonly Color Background = new(28, 30, 34, 255);
    private static readonly Color Panel = new(42, 50, 63, 255);
    private static readonly Color Accent = new(233, 138, 76, 255);
    private static readonly Color Caption = new(150, 162, 180, 255);
    private static readonly Color Value = new(235, 238, 242, 255);

    private const int FieldWidth = 240;

    private readonly IClipboardService _clipboard;

    private readonly TextBox _name;
    private readonly NumberBox<int> _width;
    private readonly NumberBox<int> _height;
    private readonly NumberBox<float> _scale;
    private readonly Label _summary;
    private readonly Label _lockCaption;
    private readonly Button _lock;

    public SettingsView(SettingsViewModel viewModel, IClipboardService clipboard)
        : base(viewModel)
    {
        _clipboard = clipboard;

        _name = Field(new TextBox());
        _name.Committed += text => ViewModel.Name = text;

        // A NumberBox refuses a keystroke that would leave the field unparseable, so there is no
        // invalid state to validate on commit — only a value the view model can take as it is.
        _width = Field(new NumberBox<int>(formatProvider: CultureInfo.InvariantCulture));
        _width.ValueCommitted += value => ViewModel.Width = value;

        _height = Field(new NumberBox<int>(formatProvider: CultureInfo.InvariantCulture));
        _height.ValueCommitted += value => ViewModel.Height = value;

        _scale = Field(new NumberBox<float>(formatProvider: CultureInfo.InvariantCulture));
        _scale.ValueCommitted += value => ViewModel.Scale = value;

        _summary = new Label { Color = Value };

        // The caption is kept and written to rather than replaced, so toggling the lock re-measures
        // one label instead of building a new element and re-laying out the row around it.
        _lockCaption = new Label();
        _lock = new Button { Content = _lockCaption };
        _lock.Clicked += () => ViewModel.IsLocked = !ViewModel.IsLocked;
    }

    protected override Element Build()
    {
        return new Column(gap: 18)
        {
            Background = new SolidDrawable(Background),
            Padding = new Thickness(28),
            Width = Sizing.Grow(),
            Height = Sizing.Grow(),
            Children =
            {
                new Label("Settings") { Role = TextRole.Title, Color = Accent },
                new Label("Click a field to edit. Enter or clicking away commits; Escape cancels.") { Color = Caption },

                new Column(gap: 10)
                {
                    Background = new SolidDrawable(Panel),
                    Padding = new Thickness(18),
                    Children =
                    {
                        FieldRow("Name", _name),
                        FieldRow("Width", _width),
                        FieldRow("Height", _height),
                        FieldRow("Scale", _scale)
                    }
                },

                new Row(gap: 10)
                {
                    Children =
                    {
                        _lock,
                        Action("Reset", ViewModel.Reset)
                    }
                },

                _summary
            }
        };
    }

    protected override void Sync()
    {
        // Assigning to a focused field leaves what is being typed alone, so writing every field on
        // any change does not fight the edit in progress.
        _name.Text = ViewModel.Name;
        _width.SetValue(ViewModel.Width);
        _height.SetValue(ViewModel.Height);
        _scale.SetValue(ViewModel.Scale);

        _summary.Content =
            $"Name: {ViewModel.Name}  Size: {ViewModel.Width}x{ViewModel.Height}  " +
            $"Scale: {ViewModel.Scale.ToString(CultureInfo.InvariantCulture)}";

        // A disabled field paints its disabled background, refuses the pointer, and gives up focus
        // if it happens to be the field being edited.
        _name.IsEnabled = !ViewModel.IsLocked;
        _width.IsEnabled = !ViewModel.IsLocked;
        _height.IsEnabled = !ViewModel.IsLocked;
        _scale.IsEnabled = !ViewModel.IsLocked;

        _lockCaption.Content = ViewModel.IsLocked ? "Unlock" : "Lock";
    }

    private TBox Field<TBox>(TBox box)
        where TBox : TextBox
    {
        box.Width = Sizing.Fixed(FieldWidth);

        // Cut, copy and paste do nothing until a field is given somewhere to put the text.
        box.Clipboard = _clipboard;
        return box;
    }

    private static Button Action(string text, Action onClick)
    {
        Button button = new(text);
        button.Clicked += onClick;
        return button;
    }

    private static Element FieldRow(string caption, Element field)
    {
        return new Row(gap: 12)
        {
            Children =
            {
                new Label(caption) { Color = Caption, Width = Sizing.Fixed(70) },
                field
            }
        };
    }
}
