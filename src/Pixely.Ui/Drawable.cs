using System.Numerics;
using Pixely.Gpu;
using Pixely.Sprites;

namespace Pixely.Ui;

/// <summary>
/// Fills an element's bounds. Open for extension: <see cref="Paint"/> and the
/// <see cref="PaintContext"/> emit methods are public, so a gradient or scanline drawable needs
/// no change here.
/// </summary>
public abstract class Drawable
{
    public abstract void Paint(PaintContext context, Rectangle bounds);

    public static Drawable Solid(Color color) => new SolidDrawable(color);

    public static Drawable Sprite(SpriteAsset sprite, Color tint) => new SpriteDrawable(sprite, tint);

    public static Drawable NinePatch(SpriteAsset sprite, Thickness insets, Color tint) =>
        new NinePatchDrawable(sprite, insets, tint);
}

public sealed class SolidDrawable : Drawable
{
    public SolidDrawable(Color color) => Color = color;

    public Color Color { get; }

    public override void Paint(PaintContext context, Rectangle bounds) => context.FillRectangle(bounds, Color);
}

public sealed class SpriteDrawable : Drawable
{
    public SpriteDrawable(SpriteAsset sprite, Color tint)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        Sprite = sprite;
        Tint = tint;
    }

    public SpriteAsset Sprite { get; }
    public Color Tint { get; }

    public override void Paint(PaintContext context, Rectangle bounds) => context.DrawSprite(Sprite, bounds, Tint);
}

/// <summary>
/// Stretches a sprite's middle while keeping its corners and edges at their natural size, which is
/// what lets one panel graphic fit any element.
/// </summary>
public sealed class NinePatchDrawable : Drawable
{
    private readonly Thickness _insets;

    public NinePatchDrawable(SpriteAsset sprite, Thickness insets, Color tint)
    {
        ArgumentNullException.ThrowIfNull(sprite);

        if (insets.HasNegativeEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(insets), insets, "Nine-patch insets must not be negative.");
        }

        if (insets.Horizontal > sprite.Size.X || insets.Vertical > sprite.Size.Y)
        {
            throw new ArgumentOutOfRangeException(
                nameof(insets),
                insets,
                $"Nine-patch insets {insets} exceed the sprite size {sprite.Size.X}x{sprite.Size.Y}.");
        }

        Sprite = sprite;
        _insets = insets;
        Tint = tint;
    }

    public SpriteAsset Sprite { get; }
    public Thickness Insets => _insets;
    public Color Tint { get; }

    public override void Paint(PaintContext context, Rectangle bounds)
    {
        int spriteWidth = Sprite.Size.X;
        int spriteHeight = Sprite.Size.Y;

        // Columns and rows in source pixels, then the same three bands stretched to the target.
        Span<int> sourceColumns = [0, _insets.Left, spriteWidth - _insets.Right, spriteWidth];
        Span<int> sourceRows = [0, _insets.Top, spriteHeight - _insets.Bottom, spriteHeight];

        int middleWidth = Math.Max(0, bounds.Width - _insets.Horizontal);
        int middleHeight = Math.Max(0, bounds.Height - _insets.Vertical);

        Span<int> targetColumns = [0, _insets.Left, _insets.Left + middleWidth, _insets.Left + middleWidth + _insets.Right];
        Span<int> targetRows = [0, _insets.Top, _insets.Top + middleHeight, _insets.Top + middleHeight + _insets.Bottom];

        Vector4 spriteUvs = Sprite.CalculateTextureRegionUVs();

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Rectangle area = new(
                    bounds.X + targetColumns[column],
                    bounds.Y + targetRows[row],
                    targetColumns[column + 1] - targetColumns[column],
                    targetRows[row + 1] - targetRows[row]);

                if (area.Width <= 0 || area.Height <= 0)
                {
                    continue;
                }

                Vector4 uvs = new(
                    Lerp(spriteUvs.X, spriteUvs.Z, sourceColumns[column] / (float)spriteWidth),
                    Lerp(spriteUvs.Y, spriteUvs.W, sourceRows[row] / (float)spriteHeight),
                    Lerp(spriteUvs.X, spriteUvs.Z, sourceColumns[column + 1] / (float)spriteWidth),
                    Lerp(spriteUvs.Y, spriteUvs.W, sourceRows[row + 1] / (float)spriteHeight));

                context.DrawTexture(Sprite.Texture, area, uvs, (FColor)Tint);
            }
        }
    }

    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);
}
