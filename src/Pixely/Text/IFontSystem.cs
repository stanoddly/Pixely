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
    void ReleaseFont(Font font);
}
