using Pixely.Gpu;
using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// A clickable element holding one child. It paints a background per <see cref="VisualState"/> and
/// nothing else: what the button says is an ordinary child element, so the same type carries a
/// label, an icon or a row of both without knowing about any of them.
/// </summary>
public sealed class Button : Element, IPointerTarget, IVisualStateSource
{
    private bool _isHovered;
    private bool _isPressed;

    public Button()
    {
        // Overlay rather than the default stack, so content stays centred in a button given a fixed
        // size instead of sitting at its top edge.
        Layout = OverlayLayout.Instance;
        Padding = new Thickness(10, 6);
    }

    /// <summary>Wraps <paramref name="text"/> in a centred <see cref="Label"/>, which takes its font from the style.</summary>
    public Button(string text) : this()
    {
        ArgumentNullException.ThrowIfNull(text);

        Content = new Label(text)
        {
            HorizontalAlignment = Alignment.Center,
            VerticalAlignment = Alignment.Center
        };
    }

    /// <summary>Raised on a press and release that both landed on this button.</summary>
    public event Action? Clicked;

    protected override int MaxChildCount => 1;

    public Element? Content
    {
        get => Children.Count == 0 ? null : Children[0];
        set
        {
            Children.Clear();

            if (value != null)
            {
                Children.Add(value);
            }
        }
    }

    public VisualState VisualState
    {
        get
        {
            if (!IsEffectivelyEnabled)
            {
                return VisualState.Disabled;
            }

            // Pressed only while the pointer is still on the button: a press dragged off shows as
            // normal again, which is the feedback that says releasing there will not click.
            if (_isPressed && _isHovered)
            {
                return VisualState.Pressed;
            }

            return _isHovered ? VisualState.Hovered : VisualState.Normal;
        }
    }

    // A plain Background assigned through the inherited property means one look for every state.
    // Honouring it is what keeps that property from accepting a value and then quietly doing
    // nothing on this one element.
    protected override Drawable? EffectiveBackground =>
        base.EffectiveBackground ?? Style.Button.Background.Resolve(VisualState);

    StateColors IVisualStateSource.ContentForeground => Style.Button.Foreground;

    void IPointerTarget.OnPointerEnter(Vector2Int position)
    {
        _isHovered = true;
        InvalidatePaint();
    }

    void IPointerTarget.OnPointerLeave()
    {
        _isHovered = false;
        InvalidatePaint();
    }

    // Left only. A button that took every button would activate on a right-click, and would also
    // swallow one meant for whatever the UI is drawn over.
    bool IPointerTarget.OnPointerPress(Vector2Int position, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return false;
        }

        _isPressed = true;
        InvalidatePaint();
        return true;
    }

    void IPointerTarget.OnPointerRelease(Vector2Int position, MouseButton button, bool inside)
    {
        _isPressed = false;
        InvalidatePaint();

        if (inside)
        {
            Clicked?.Invoke();
        }
    }

    void IPointerTarget.OnPointerCancel(MouseButton button)
    {
        _isPressed = false;
        InvalidatePaint();
    }
}
