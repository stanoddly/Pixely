using Pixely.Gpu;
using Pixely.Ui;

namespace Pixely.Tutorials.MultiWindowTextInput;

public sealed class TextInputViewModel : IUiViewModel
{
    private string _text;

    public event Action? Changed;

    public TextInputViewModel(string text)
    {
        _text = text;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            Changed?.Invoke();
        }
    }
}

public sealed class TextInputView : UiView<TextInputViewModel>
{
    private static readonly Color BackgroundColor = new(28, 30, 34, 255);

    private readonly string _name;
    private readonly TextBox _field = new() { Width = Sizing.Fixed(360) };
    private readonly Label _value = new();

    public TextInputView(ViewScope viewScope, string name, TextInputViewModel viewModel)
        : base(viewModel)
    {
        ViewScope = viewScope;
        _name = name;
        _field.Committed += text => ViewModel.Text = text;
    }

    public override ViewScope ViewScope { get; }

    protected override Element Build()
    {
        return new Overlay
        {
            Background = new SolidDrawable(BackgroundColor),
            Children =
            {
                new Column(gap: 14)
                {
                    HorizontalAlignment = Alignment.Center,
                    Margin = new Thickness(0, 70, 0, 0),
                    Children =
                    {
                        new Label(_name) { Emphasis = TextEmphasis.Muted },
                        _field,
                        _value,
                        new Label("Input is routed through this ViewScope.") { Emphasis = TextEmphasis.Muted }
                    }
                }
            }
        };
    }

    protected override void Sync()
    {
        _field.Text = ViewModel.Text;
        _value.Content = $"This window contains: {ViewModel.Text}";
    }
}
