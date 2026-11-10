using Pixely.Gpu;

namespace Pixely.Text;

/// <summary>
/// What a consumer needs of a font: how big a run of text is, and its rasterisation. Separate from
/// <see cref="Font"/> so that code laying text out — which only measures — can be exercised without
/// a device, and so that measuring during layout can be asserted not to rasterise.
/// </summary>
public interface IFont
{
    /// <summary>The size <paramref name="text"/> would rasterise to, without rasterising it.</summary>
    ShortSize Measure(string text);

    /// <summary>Rasterises <paramref name="text"/>. Needs a device; layout must not call it.</summary>
    TextSpriteAsset CreateTextSprite(string text);

    /// <summary>
    /// The same font at <paramref name="factor"/> times its size, which measures and rasterises in pixels of a target scaled by
    /// <paramref name="factor"/>. A UI that scales smoothly draws its text in it, so glyphs are rasterised at the size they are
    /// shown at rather than stretched. Null when the font comes only in the sizes it was made at, such as a bitmap font.
    /// </summary>
    IFont? GetScaled(float factor) => null;
}
