using System.Globalization;
using Pixely.Gpu;
using Pixely.Ui;

namespace Pixely.Tutorials.UiTextInput;

/// <summary>
/// The fields are elements that hold their own edit, and the view only hears about one when it
/// commits. Nothing is passed in and out of a call every build, as an immediate-mode field would need.
/// </summary>
public sealed class SettingsView : UiView<SettingsViewModel>
{
    private static readonly Color Background = new(28, 30, 34, 255);
    private static readonly Color Panel = new(42, 50, 63, 255);

    private const int FieldWidth = 240;

    private readonly TextBox _name;
    private readonly NumberBox<int> _width;
    private readonly NumberBox<int> _height;
    private readonly NumberBox<float> _scale;
    private readonly Label _summary;
    private readonly Label _lockCaption;
    private readonly Button _lock;

    public SettingsView(SettingsViewModel viewModel)
        : base(viewModel)
    {
        _name = Field(new TextBox());
        _name.Committed += text => ViewModel.Name = text;

        // A NumberBox refuses a keystroke that cannot lead to a number of its own type, while still
        // allowing the ones on the way to one: an int field takes "-" but not "1.", where the float
        // field below takes both. Finishing is what requires a complete number, so ValueCommitted
        // only ever carries one the view model can take as it is.
        _width = Field(new NumberBox<int>(formatProvider: CultureInfo.InvariantCulture));
        _width.ValueCommitted += value => ViewModel.Width = value;

        _height = Field(new NumberBox<int>(formatProvider: CultureInfo.InvariantCulture));
        _height.ValueCommitted += value => ViewModel.Height = value;

        _scale = Field(new NumberBox<float>(formatProvider: CultureInfo.InvariantCulture));
        _scale.ValueCommitted += value => ViewModel.Scale = value;

        _summary = new Label ();

        // The caption is kept and written to rather than replaced. Re-measuring still happens —
        // the text changed width — but no element is allocated and nothing is reparented.
        _lockCaption = new Label();
        _lock = new Button { Content = _lockCaption };
        _lock.Clicked += () => ViewModel.IsLocked = !ViewModel.IsLocked;
    }

    protected override Element Build()
    {
        return new Column(gap: 18)
        {
            // No sizing here: a layer is measured against the viewport and arranged to it, so its
            // own Width and Height are never consulted.
            Background = new SolidDrawable(Background),
            Padding = new Thickness(28),
            Children =
            {
                new Label("Settings") { Role = TextRole.Title, Emphasis = TextEmphasis.Accent },
                new Label("Click a field to edit. Enter or clicking away commits a valid value; Escape cancels.") { Emphasis = TextEmphasis.Muted },

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

        // A disabled field paints its disabled background and refuses the pointer. It would give up
        // focus too, but not visibly here: clicking the lock button has already taken focus off the
        // field before this runs.
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
                new Label(caption) { Emphasis = TextEmphasis.Muted, Width = Sizing.Fixed(70) },
                field
            }
        };
    }
}
