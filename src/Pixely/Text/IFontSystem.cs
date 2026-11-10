using Pixely.Gpu;

namespace Pixely.Text;

public interface IFontSystem: IDisposable
{
    Font Load(
        string path,
        ushort size,
        FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended,
        FontHintingMode hintingMode = FontHintingMode.Normal,
        FontStyle style = FontStyle.Normal);
    TextSpriteAsset CreateTextSprite(string text, Font font);
    ShortSize MeasureTextSprite(string text, Font font);

    /// <summary>
    /// <paramref name="font"/> at <paramref name="factor"/> times its size, see <see cref="Font.GetScaled"/>. Null when this
    /// font system cannot rasterise the font at another size. Internal, because only Pixely creates a <see cref="Font"/>.
    /// </summary>
    internal Font? GetScaled(Font font, float factor) => null;

    void ReleaseFont(Font font);
}
