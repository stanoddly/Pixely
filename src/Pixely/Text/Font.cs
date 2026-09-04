using System.Runtime.InteropServices;
using Pixely.Utilities;
using SDL;

namespace Pixely.Text;

public class Font : IDisposable
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

    /// <summary>The system that loaded this font, and that rasterises text with it.</summary>
    public IFontSystem FontSystem => _fontSystem;

    public string Path { get; }
    public ushort Size { get; }
    public FontRasterizationMode RasterizationMode { get; }
    public FontHintingMode HintingMode { get; }

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
