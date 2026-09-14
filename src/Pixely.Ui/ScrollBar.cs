using Pixely.Input;

namespace Pixely.Ui;

/// <summary>
/// One of a <see cref="ScrollView"/>'s bars. Holds no range of its own: offset, extent and viewport
/// are read from the scroll view when painting, which has arranged by then. What it holds is what
/// the pointer is doing to it.
/// </summary>
/// <remarks>
/// A press on the track pages the scroll view by one viewport towards the press. A press on the
/// thumb is taken so that it does not fall through to the game, and does nothing more yet:
/// dragging needs a move notification <see cref="IPointerTarget"/> does not have.
/// </remarks>
internal sealed class ScrollBar : Element, IPointerTarget
{
    private readonly ScrollView _owner;
    private readonly Orientation _orientation;
    private bool _isHovered;
    private bool _isPressed;

    internal ScrollBar(ScrollView owner, Orientation orientation)
    {
        _owner = owner;
        _orientation = orientation;
    }

    protected override int MaxChildCount => 0;

    internal VisualState VisualState
    {
        get
        {
            if (!IsEffectivelyEnabled)
            {
                return VisualState.Disabled;
            }

            if (_isPressed)
            {
                return VisualState.Pressed;
            }

            return _isHovered ? VisualState.Hovered : VisualState.Normal;
        }
    }

    // The track is the bar's background, so a Background assigned through the inherited property is
    // honoured ahead of the style's, as on a button.
    protected override Drawable? EffectiveBackground => base.EffectiveBackground ?? Style.Scroll.Track;

    /// <summary>
    /// Where the thumb is, from the scroll view's current range. Empty when the bar is not shown.
    /// Its length is the viewport's share of the extent, held above the style's minimum so it stays
    /// visible over very long content, and its position is the offset's share of what is left.
    /// </summary>
    internal Rectangle ThumbBounds
    {
        get
        {
            int length = Along(Bounds.Width, Bounds.Height);

            if (length <= 0)
            {
                return default;
            }

            int extent = Along(_owner.ScrollExtent.X, _owner.ScrollExtent.Y);
            int viewport = Along(_owner.ViewportSize.X, _owner.ViewportSize.Y);
            int max = Along(_owner.MaxScrollOffset.X, _owner.MaxScrollOffset.Y);
            int offset = Along(_owner.ScrollOffset.X, _owner.ScrollOffset.Y);

            int thumbLength = extent <= viewport
                ? length
                : Math.Clamp((int)((long)length * viewport / extent), Math.Min(Style.Scroll.MinimumThumbLength, length), length);

            int travel = length - thumbLength;
            int thumbStart = max <= 0 ? 0 : (int)((long)travel * offset / max);

            return _orientation == Orientation.Horizontal
                ? new Rectangle(Bounds.X + thumbStart, Bounds.Y, thumbLength, Bounds.Height)
                : new Rectangle(Bounds.X, Bounds.Y + thumbStart, Bounds.Width, thumbLength);
        }
    }

    protected override Vector2Int MeasureContent(Constraints constraints) => default;

    protected override void PaintContent(PaintContext context)
    {
        Rectangle thumb = ThumbBounds;

        if (thumb.Width > 0 && thumb.Height > 0)
        {
            Style.Scroll.Thumb.Resolve(VisualState).Paint(context, thumb);
        }
    }

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

    bool IPointerTarget.OnPointerPress(Vector2Int position, MouseButton button)
    {
        if (button != MouseButton.Left)
        {
            return false;
        }

        _isPressed = true;
        InvalidatePaint();

        Rectangle thumb = ThumbBounds;
        int pressed = Along(position.X, position.Y);
        int thumbStart = Along(thumb.X, thumb.Y);
        int thumbLength = Along(thumb.Width, thumb.Height);
        int page = Along(_owner.ViewportSize.X, _owner.ViewportSize.Y);

        if (pressed < thumbStart)
        {
            _owner.ScrollBy(Compose(-page));
        }
        else if (pressed >= thumbStart + thumbLength)
        {
            _owner.ScrollBy(Compose(page));
        }

        return true;
    }

    void IPointerTarget.OnPointerRelease(Vector2Int position, MouseButton button, bool inside)
    {
        _isPressed = false;
        InvalidatePaint();
    }

    void IPointerTarget.OnPointerCancel(MouseButton button)
    {
        _isPressed = false;
        InvalidatePaint();
    }

    private int Along(int x, int y) => _orientation == Orientation.Horizontal ? x : y;

    private Vector2Int Compose(int along) => _orientation == Orientation.Horizontal ? new Vector2Int(along, 0) : new Vector2Int(0, along);
}
