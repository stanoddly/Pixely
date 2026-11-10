using Pixely.Gpu;

namespace Pixely.Text;

public interface IFontSystem: IDisposable
{
    Font Load(string path, float size, FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended, FontHintingMode hintingMode = FontHintingMode.Normal,
        FontStyle style = FontStyle.Normal, IReadOnlyList<FontVariation>? variations = null, IReadOnlyList<FontFeature>? features = null, float letterSpacing = 0);
    TextSpriteAsset CreateTextSprite(string text, Font font);
    ShortSize MeasureTextSprite(string text, Font font);
    void ReleaseFont(Font font);
}
