using Pixely.Gpu;
using Pixely.Sprites;

namespace Pixely.Ui;

/// <summary>
/// A sprite sized to its natural dimensions unless the box model says otherwise, in which case it
/// is stretched to the bounds it was given.
/// </summary>
public sealed class Image : Element
{
    private SpriteAsset _sprite;
    private Color _tint = Colors.White;

    public Image(SpriteAsset sprite)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        _sprite = sprite;
    }

    protected override int MaxChildCount => 0;

    public SpriteAsset Sprite
    {
        get => _sprite;
        set => SetMeasureProperty(ref _sprite, value ?? throw new ArgumentNullException(nameof(value)));
    }

    public Color Tint
    {
        get => _tint;
        set => SetPaintProperty(ref _tint, value);
    }

    protected override Vector2Int MeasureContent(Constraints constraints) =>
        new(_sprite.Size.X, _sprite.Size.Y);

    protected override void PaintContent(PaintContext context) => context.DrawSprite(_sprite, Bounds, _tint);
}
