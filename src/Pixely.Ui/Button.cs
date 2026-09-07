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
    /// <summary>
    /// Used when neither the button nor the root's <see cref="UiStyle"/> supplies one, so a button
    /// is visible without any setup. <see cref="UiStyle.ButtonBackground"/> defaults to it too,
    /// which keeps the look defined in exactly one place.
    /// </summary>
    public static StateDrawables DefaultBackground { get; } = new(new SolidDrawable(new Color(52, 60, 74, 255)))
    {
        Hovered = new SolidDrawable(new Color(70, 80, 98, 255)),
        Pressed = new SolidDrawable(new Color(38, 44, 55, 255)),
        Disabled = new SolidDrawable(new Color(40, 44, 51, 255))
    };

    /// <summary>
    /// What a button's text falls back on. <see cref="UiStyle.ButtonForeground"/> defaults to it,
    /// and it deliberately carries no hover tint: recolouring text on hover is a theme's decision,
    /// not something every button should start out doing.
    /// </summary>
    public static StateColors DefaultForeground { get; } = new(UiStyle.DefaultForeground)
    {
        Disabled = UiStyle.DefaultDisabledForeground
    };

    private StateDrawables? _backgrounds;
    private StateColors? _foregrounds;
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

    /// <summary>
    /// Backgrounds for this button alone. When null the inherited <see cref="Element.Background"/>
    /// is used for every state if it was set, and the root style's otherwise.
    /// </summary>
    public StateDrawables? Backgrounds
    {
        get => _backgrounds;
        set => SetPaintProperty(ref _backgrounds, value);
    }

    /// <summary>
    /// Colours for the text in this button alone. When null the root style's
    /// <see cref="UiStyle.ButtonForeground"/> is used, and <see cref="DefaultForeground"/> when
    /// there is no style. A <see cref="Label"/> given a colour of its own uses that while it is
    /// enabled, and the <see cref="VisualState.Disabled"/> entry here when it is not.
    /// </summary>
    public StateColors? Foregrounds
    {
        get => _foregrounds;
        set => SetPaintProperty(ref _foregrounds, value);
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

    protected override Drawable? EffectiveBackground
    {
        get
        {
            if (_backgrounds != null)
            {
                return _backgrounds.Resolve(VisualState);
            }

            // A plain Background assigned through the inherited property means one look for every
            // state. Honouring it is what keeps that property from accepting a value and then
            // quietly doing nothing on this one element.
            return base.EffectiveBackground
                ?? (OwnerRoot?.Style?.ButtonBackground ?? DefaultBackground).Resolve(VisualState);
        }
    }

    StateColors IVisualStateSource.ContentForeground =>
        _foregrounds ?? OwnerRoot?.Style?.ButtonForeground ?? DefaultForeground;

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
