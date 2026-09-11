using Pixely.Gpu;
using Pixely.Input;
using Pixely.Sprites;
using Pixely.Ui;

namespace Pixely.Tutorials.Hotbar;

/// <summary>
/// One slot: an icon on a background that shows whether it is selected or under the pointer. A
/// <see cref="Button"/> would give the click and the hover look, but it keeps hover to itself, and
/// the label above the bar needs to know which slot the pointer is on. Implementing
/// <see cref="IPointerTarget"/> directly is what makes an element solid to the pointer.
/// </summary>
public sealed class HotbarSlot : Element, IPointerTarget
{
    private static readonly Drawable Normal = new SolidDrawable(new Color(60, 60, 60, 255));
    private static readonly Drawable Selected = new SolidDrawable(new Color(200, 200, 200, 255));
    private static readonly Drawable Hovered = new SolidDrawable(new Color(100, 100, 100, 255));

    private bool _isSelected;
    private bool _isHovered;

    public HotbarSlot(SpriteAsset icon, int size)
    {
        Layout = OverlayLayout.Instance;
        Width = Sizing.Fixed(size);
        Height = Sizing.Fixed(size);
        Children.Add(new Image(icon) { HorizontalAlignment = Alignment.Center, VerticalAlignment = Alignment.Center });
    }

    /// <summary>Raised on a press and release that both landed on this slot.</summary>
    public event Action? Clicked;

    /// <summary>Raised as the pointer enters and leaves.</summary>
    public event Action<bool>? HoverChanged;

    protected override int MaxChildCount => 1;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetPaintProperty(ref _isSelected, value);
    }

    // Selection beats hover, so the selected slot does not dim as the pointer crosses it.
    protected override Drawable? EffectiveBackground => _isSelected ? Selected : _isHovered ? Hovered : Normal;

    void IPointerTarget.OnPointerEnter(Vector2Int position)
    {
        _isHovered = true;
        InvalidatePaint();
        HoverChanged?.Invoke(true);
    }

    void IPointerTarget.OnPointerLeave()
    {
        _isHovered = false;
        InvalidatePaint();
        HoverChanged?.Invoke(false);
    }

    bool IPointerTarget.OnPointerPress(Vector2Int position, MouseButton button) => button == MouseButton.Left;

    void IPointerTarget.OnPointerRelease(Vector2Int position, MouseButton button, bool inside)
    {
        if (inside)
        {
            Clicked?.Invoke();
        }
    }

    void IPointerTarget.OnPointerCancel(MouseButton button)
    {
    }
}
