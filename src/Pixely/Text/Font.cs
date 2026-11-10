using System.Diagnostics;
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

    // A scaled font passes the loaded font it came from and its size in 64ths of a pixel; a loaded font passes neither.
    internal unsafe Font(IFontSystem fontSystem, Pointer<TTF_Font> ttfFont, byte* fontData, FontKey key, IReadOnlyList<FontVariation> variations,
        IReadOnlyList<FontFeature> features, Font? unscaled = null, int scaledSizeIn64ths = 0)
    {
        Debug.Assert((unscaled == null) == (scaledSizeIn64ths == 0), "A scaled font passes both the loaded font and its size, a loaded font neither.");
        _fontSystem = fontSystem;
        _ttfFont = ttfFont;
        _fontData = fontData;
        Key = key;
        Variations = variations;
        Features = features;
        Unscaled = unscaled;
        SizeIn64ths = unscaled == null ? ToSizeIn64ths(key.Size, 1) : scaledSizeIn64ths;
        Scale = unscaled == null ? 1f : ToScale(key.Size, scaledSizeIn64ths);
    }

    internal Pointer<TTF_Font> TtfFont => _ttfFont;
    internal FontKey Key { get; }
    public string Path => Key.Path;

    /// <summary>The size the font was loaded at. A scaled font keeps it, and rasterises at it times <see cref="Scale"/>.</summary>
    public float Size => Key.Size;

    public FontRasterizationMode RasterizationMode => Key.RasterizationMode;
    public FontHintingMode HintingMode => Key.HintingMode;
    public FontStyle Style => Key.Style;
    public IReadOnlyList<FontVariation> Variations { get; }
    public IReadOnlyList<FontFeature> Features { get; }

    /// <summary>The letter spacing the font was loaded with. A scaled font keeps it, and spaces its characters by it times <see cref="Scale"/>.</summary>
    public float LetterSpacing => Key.LetterSpacing;

    /// <summary>
    /// How many times <see cref="Size"/> this font rasterises at: 1 for a loaded font, and for one from <see cref="GetScaled"/> the
    /// factor rounded to the 64th of a pixel its size was rounded to.
    /// </summary>
    public float Scale { get; }

    /// <summary>The loaded font a scaled one came from, or null for a loaded font.</summary>
    internal Font? Unscaled { get; }

    /// <summary>
    /// The size this font rasterises at, in 64ths of a pixel, which scaled fonts are cached by. Kept rather than computed again from
    /// <see cref="Size"/> and <see cref="Scale"/>, which a float cannot hold exactly for every size.
    /// </summary>
    internal int SizeIn64ths { get; }

    /// <summary>
    /// This font at <paramref name="factor"/> times its size, as <see cref="IFont.GetScaled"/> describes. The size is rounded to
    /// the 64th of a pixel FreeType keeps it in, raised to 1 pixel when smaller, and must be at most 65535 pixels. The scaled font shares the loaded font's data,
    /// is cached per rounded size, and is released with the loaded font, so the caller does not dispose it. Scaling a scaled font
    /// multiplies its <see cref="Scale"/> by the factor, and a factor that lands on the loaded size returns the loaded font. Null
    /// for a bitmap font, which comes only in its strike sizes.
    /// </summary>
    public Font? GetScaled(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The factor must be finite and greater than 0.");
        }

        Font unscaled = Unscaled ?? this;
        float scale = Scale * factor;
        return scale == 1f ? unscaled : _fontSystem.GetScaled(unscaled, scale);
    }

    IFont? IFont.GetScaled(float factor) => GetScaled(factor);

    /// <summary>
    /// The size <paramref name="size"/> times <paramref name="scale"/> rasterises at, in the 64ths of a pixel FreeType keeps a
    /// size in, rounded half away from zero as SDL_ttf rounds it, and at least 1 pixel, as FreeType draws a smaller size at 1 pixel. Scaled
    /// fonts are cached by it, because two factors that land on the same 64th are the same face, and caching by the factor itself
    /// would keep a face for every frame of a continuous zoom.
    /// </summary>
    internal static int ToSizeIn64ths(float size, double scale) => Math.Max(64, (int)Math.Round(size * scale * 64.0, MidpointRounding.AwayFromZero));

    // The scale of a font loaded at size and rasterised at sizeIn64ths, which its letter spacing is multiplied by too.
    internal static float ToScale(float size, int sizeIn64ths) => sizeIn64ths / (64f * size);

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
