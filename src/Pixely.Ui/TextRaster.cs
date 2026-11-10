using Pixely.Gpu;
using Pixely.Text;

namespace Pixely.Ui;

/// <summary>
/// The font a paint rasterises text in, and how many raster pixels each of its pixels covers: 1
/// when the font could be scaled to the paint's scale, so glyphs land on raster pixels one to one,
/// and the scale itself when it could not, so the text is stretched to its logical size instead.
/// </summary>
internal readonly record struct TextRaster(IFont Font, float PixelScale, TextureFilter Filter)
{
    /// <summary>A length in the font's pixels, in raster pixels.</summary>
    internal int ToRaster(int length) => PixelScale == 1f ? length : (int)Math.Floor((length * (double)PixelScale) + 0.5);

    /// <summary>How wide <paramref name="text"/> is in raster pixels.</summary>
    internal int Width(string text) => text.Length == 0 ? 0 : ToRaster(Font.Measure(text).Width);

    /// <summary>
    /// Draws <paramref name="sprite"/>, rasterised in <see cref="Font"/>, with its top left corner
    /// on the raster pixel (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    internal void Draw(PaintContext context, TextSpriteAsset sprite, int x, int y, LinearColor tint)
    {
        Rectangle area = new(x, y, ToRaster(sprite.Size.X), ToRaster(sprite.Size.Y));
        context.DrawRaster(sprite.Texture, area, sprite.CalculateTextureRegionUVs(), tint, Filter);
    }

    /// <summary>
    /// How big <paramref name="text"/> is in logical pixels when it is drawn at <paramref name="scale"/>: its size in the scaled
    /// font, divided by the scale and rounded up, so the bounds laid out for it hold what is drawn. Hinting rounds each glyph's
    /// advance at each size, so the logical font's own measure can be a few pixels off either way.
    /// </summary>
    internal static Vector2Int MeasureLogical(IFont font, string text, float scale)
    {
        IFont? scaled = scale == 1f ? null : font.GetScaled(scale);

        if (scaled == null)
        {
            ShortSize size = font.Measure(text);
            return new Vector2Int(size.Width, size.Height);
        }

        // Divided in double, because a float quotient a rounding error above a whole number would
        // round up to a phantom logical pixel.
        ShortSize scaledSize = scaled.Measure(text);
        return new Vector2Int((int)Math.Ceiling(scaledSize.Width / (double)scale), (int)Math.Ceiling(scaledSize.Height / (double)scale));
    }

    /// <summary>What <paramref name="context"/> rasterises text in <paramref name="font"/> with.</summary>
    internal static TextRaster Resolve(PaintContext context, IFont font)
    {
        // Nearest for text drawn one to one: it lands on whole raster pixels, and nearest is what
        // keeps it from being resampled at all.
        if (context.Scale == 1f)
        {
            return new TextRaster(font, 1f, TextureFilter.Nearest);
        }

        IFont? scaled = font.GetScaled(context.Scale);
        return scaled != null ? new TextRaster(scaled, 1f, TextureFilter.Nearest) : new TextRaster(font, context.Scale, context.Filter);
    }
}
