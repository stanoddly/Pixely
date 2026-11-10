using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Text;

public class Font : IFont, IDisposable
{
    // The font system that loaded it: Pixely's own, or the stub of Pixely.Testing, whose fonts have no SDL font.
    private readonly IFontSystem _fontSystem;
    private readonly Pointer<TTF_Font> _ttfFont;
    private readonly unsafe byte* _fontData;

    internal unsafe Font(IFontSystem fontSystem, Pointer<TTF_Font> ttfFont, byte* fontData, FontKey key, ImmutableArray<FontVariation> variations, ImmutableArray<FontFeature> features)
    {
        _fontSystem = fontSystem;
        _ttfFont = ttfFont;
        _fontData = fontData;
        Key = key;
        Variations = variations;
        Features = features;
    }

    internal Pointer<TTF_Font> TtfFont => _ttfFont;
    internal FontKey Key { get; }
    public string Path => Key.Path;
    public float Size => Key.Size;
    public FontRasterizationMode RasterizationMode => Key.RasterizationMode;
    public FontHintingMode HintingMode => Key.HintingMode;
    public FontStyle Style => Key.Style;
    public IReadOnlyList<FontVariation> Variations { get; }
    public IReadOnlyList<FontFeature> Features { get; }
    public float LetterSpacing => Key.LetterSpacing;

    /// <summary>
    /// Rasterises <paramref name="text"/> with this font. Results are cached by the font system,
    /// so repeating the same text is cheap.
    /// </summary>
    public TextSpriteAsset CreateTextSprite(string text) => _fontSystem.CreateTextSprite(text, this);

    /// <summary>
    /// The size <paramref name="text"/> would rasterise to, without rasterising it: no surface is
    /// rendered and no texture is uploaded, so this works without a device.
    /// </summary>
    public ShortSize Measure(string text) => _fontSystem.MeasureTextSprite(text, this);

    internal unsafe void FreeFontData()
    {
        if (_fontData != null)
        {
            NativeMemory.Free(_fontData);
        }
    }

    public void Dispose()
    {
        _fontSystem.ReleaseFont(this);
    }
}
