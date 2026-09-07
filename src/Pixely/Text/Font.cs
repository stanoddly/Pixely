using System.Runtime.InteropServices;
using Pixely.Utilities;
using SDL;

namespace Pixely.Text;

public class Font : IFont, IDisposable
{
    private FontSystem _fontSystem;
    private readonly Pointer<TTF_Font> _ttfFont;
    private readonly unsafe byte* _fontData;

    internal unsafe Font(
        FontSystem fontSystem,
        Pointer<TTF_Font> ttfFont,
        byte* fontData,
        string path,
        ushort size,
        FontRasterizationMode rasterizationMode,
        FontHintingMode hintingMode)
    {
        _fontSystem = fontSystem;
        _ttfFont = ttfFont;
        _fontData = fontData;
        Path = path;
        Size = size;
        RasterizationMode = rasterizationMode;
        HintingMode = hintingMode;
    }

    internal Pointer<TTF_Font> TtfFont => _ttfFont;
    public string Path { get; }
    public ushort Size { get; }
    public FontRasterizationMode RasterizationMode { get; }
    public FontHintingMode HintingMode { get; }

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
