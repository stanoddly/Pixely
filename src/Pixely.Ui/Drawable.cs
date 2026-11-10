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
}

public sealed class SolidDrawable : Drawable
{
    public SolidDrawable(SrgbColor color) => Color = color;

    public SrgbColor Color { get; }

    public override void Paint(PaintContext context, Rectangle bounds) => context.FillRectangle(bounds, Color);
}

public sealed class SpriteDrawable : Drawable
{
    /// <param name="filter">How the sprite is sampled, or null to follow the root, as <see cref="Image.Filter"/> describes.</param>
    public SpriteDrawable(SpriteAsset sprite, SrgbColor tint, TextureFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        Sprite = sprite;
        Tint = tint;
        Filter = filter;
    }

    public SpriteAsset Sprite { get; }
    public SrgbColor Tint { get; }
    public TextureFilter? Filter { get; }

    public override void Paint(PaintContext context, Rectangle bounds) => context.DrawSprite(Sprite, bounds, Tint, Filter ?? context.Filter);
}

/// <summary>
/// Stretches a sprite's middle while keeping its corners and edges at their natural size, which is
/// what lets one panel graphic fit any element.
/// </summary>
public sealed class NinePatchDrawable : Drawable
{
    private readonly Thickness _insets;

    /// <param name="filter">How the sprite is sampled, or null to follow the root, as <see cref="Image.Filter"/> describes.</param>
    public NinePatchDrawable(SpriteAsset sprite, Thickness insets, SrgbColor tint, TextureFilter? filter = null)
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
        Filter = filter;
    }

    public SpriteAsset Sprite { get; }
    public Thickness Insets => _insets;
    public SrgbColor Tint { get; }
    public TextureFilter? Filter { get; }

    public override void Paint(PaintContext context, Rectangle bounds)
    {
        int spriteWidth = Sprite.Size.X;
        int spriteHeight = Sprite.Size.Y;

        // Columns and rows in source pixels, then the same three bands stretched to the target.
        Span<int> sourceColumns = [0, _insets.Left, spriteWidth - _insets.Right, spriteWidth];
        Span<int> sourceRows = [0, _insets.Top, spriteHeight - _insets.Bottom, spriteHeight];

        // In raster pixels, with the insets rounded on their own, so the four corners keep one size
        // wherever the element sits.
        Rectangle raster = context.ToRaster(bounds);
        int left = context.ToRasterThickness(_insets.Left);
        int right = context.ToRasterThickness(_insets.Right);
        int top = context.ToRasterThickness(_insets.Top);
        int bottom = context.ToRasterThickness(_insets.Bottom);

        int middleWidth = Math.Max(0, raster.Width - left - right);
        int middleHeight = Math.Max(0, raster.Height - top - bottom);

        Span<int> targetColumns = [0, left, left + middleWidth, left + middleWidth + right];
        Span<int> targetRows = [0, top, top + middleHeight, top + middleHeight + bottom];

        Vector4 spriteUvs = Sprite.CalculateTextureRegionUVs();
        LinearColor tint = Tint;
        TextureFilter filter = Filter ?? context.Filter;

        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Rectangle area = new(
                    raster.X + targetColumns[column],
                    raster.Y + targetRows[row],
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

                context.DrawRaster(Sprite.Texture, area, uvs, tint, filter);
            }
        }
    }

    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);
}

/// <summary>
/// An outline drawn inside an element's bounds, with an optional <see cref="Fill"/> painted in
/// what is left. The fill is a <see cref="Drawable"/> rather than a colour, so a nine-patch or a
/// gradient sits inside a border without needing a separate composition type.
/// </summary>
public sealed class BorderDrawable : Drawable
{
    private readonly Thickness _thickness;

    public BorderDrawable(SrgbColor color, Thickness thickness, Drawable? fill = null)
    {
        if (thickness.HasNegativeEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(thickness), thickness, "Border thickness must not be negative.");
        }

        Color = color;
        _thickness = thickness;
        Fill = fill;
    }

    public SrgbColor Color { get; }
    public Thickness Thickness => _thickness;
    public Drawable? Fill { get; }

    public override void Paint(PaintContext context, Rectangle bounds)
    {
        // Clamped here because nothing downstream does it: Thickness.Deflate clamps the inner size
        // alone, and the context clips against the current clip rather than these bounds, so an
        // edge wider than the element would paint outside it and over its opposite edge.
        int left = Math.Min(_thickness.Left, bounds.Width);
        int right = Math.Min(_thickness.Right, bounds.Width - left);
        int top = Math.Min(_thickness.Top, bounds.Height);
        int bottom = Math.Min(_thickness.Bottom, bounds.Height - top);

        // From the clamped edges rather than Thickness.Deflate: the two agree while the edges fit,
        // but a thickness wider than the element deflates to a rectangle past its far edge, and the
        // totals Deflate adds up can overflow.
        int innerWidth = bounds.Width - left - right;
        int innerHeight = bounds.Height - top - bottom;

        // The edges in raster pixels, each thickness rounded on its own, so a 1 px border is as
        // thick on one side as on the other wherever the element sits. Clamped the same way.
        Rectangle outer = context.ToRaster(bounds);
        int rasterLeft = Math.Min(context.ToRasterThickness(_thickness.Left), outer.Width);
        int rasterRight = Math.Min(context.ToRasterThickness(_thickness.Right), outer.Width - rasterLeft);
        int rasterTop = Math.Min(context.ToRasterThickness(_thickness.Top), outer.Height);
        int rasterBottom = Math.Min(context.ToRasterThickness(_thickness.Bottom), outer.Height - rasterTop);
        Rectangle rasterInner = new(outer.X + rasterLeft, outer.Y + rasterTop, outer.Width - rasterLeft - rasterRight, outer.Height - rasterTop - rasterBottom);
        LinearColor color = Color;

        // The horizontal edges span the full width and the vertical ones only what is between
        // them, which is what keeps the four disjoint at any thickness.
        context.FillRaster(new Rectangle(outer.X, outer.Y, outer.Width, rasterTop), color);
        context.FillRaster(new Rectangle(outer.X, outer.Y + outer.Height - rasterBottom, outer.Width, rasterBottom), color);
        context.FillRaster(new Rectangle(outer.X, rasterInner.Y, rasterLeft, rasterInner.Height), color);
        context.FillRaster(new Rectangle(outer.X + outer.Width - rasterRight, rasterInner.Y, rasterRight, rasterInner.Height), color);

        // An empty interior is skipped rather than handed on, because a NinePatchDrawable emits
        // its corners at their natural size even from bounds with no room for them.
        if (Fill == null || innerWidth <= 0 || innerHeight <= 0 || rasterInner.Width <= 0 || rasterInner.Height <= 0)
        {
            return;
        }

        Rectangle inner = new(bounds.X + left, bounds.Y + top, innerWidth, innerHeight);

        // The fill's edges land on the border's, which rounding the logical interior on its own
        // would miss by a pixel. Clipped for the same reason as above: an interior too small for
        // what fills it — a nine-patch narrower than its own insets — would otherwise paint over the
        // border that is supposed to surround it.
        using PaintContext.SnapScope snap = context.PushSnap(inner, rasterInner);
        using ClipScope scope = context.PushClip(inner);
        Fill.Paint(context, inner);
    }
}
