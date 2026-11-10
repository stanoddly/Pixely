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
    private SrgbColor _tint = SrgbColors.White;
    private TextureFilter? _filter;

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

    public SrgbColor Tint
    {
        get => _tint;
        set => SetPaintProperty(ref _tint, value);
    }

    /// <summary>
    /// How the sprite is sampled when it is not shown at its texture's size, or null to follow the root: nearest under
    /// <see cref="UiScaleMode.Pixelated"/> and linear under <see cref="UiScaleMode.Smooth"/>. Nearest keeps pixel art sharp;
    /// linear keeps a photo or an illustration from stair-stepping, including one shown much smaller than its texture.
    /// </summary>
    public TextureFilter? Filter
    {
        get => _filter;
        set => SetPaintProperty(ref _filter, value);
    }

    protected override Vector2Int MeasureContent(Constraints constraints) =>
        new(_sprite.Size.X, _sprite.Size.Y);

    protected override void PaintContent(PaintContext context) => context.DrawSprite(_sprite, Bounds, _tint, _filter ?? context.Filter);
}
