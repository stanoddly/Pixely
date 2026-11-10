using System.Collections.Immutable;
using System.Text;
using Pixely.Gpu;
using Pixely.Text;

namespace Pixely.Testing;

/// <summary>
/// A font system for a game's tests, with no font file and no GPU. Every character measures the same: a line is as wide as its
/// characters times the glyph width and as high as the line height, whatever the font and size. A text sprite is a blank texture
/// of the measured size on a stub device, the test's or one of its own, so a label lays out and paints as in the game.
/// </summary>
public sealed class StubFontSystem : IFontSystem
{
    private readonly record struct TextSprite(TextSpriteAsset Asset, BorrowedTexture BorrowedTexture, Texture BackingTexture);

    private readonly ushort _glyphWidth;
    private readonly ushort _lineHeight;
    private readonly StubGpuDevice _gpuDevice;
    private readonly bool _ownsGpuDevice;
    private readonly Dictionary<FontKey, Font> _fonts = new();
    private readonly Dictionary<(string Text, Font Font), TextSprite> _textSprites = new();
    private readonly Dictionary<(Font Unscaled, int SizeIn64ths), Font> _scaledFonts = new();
    private readonly List<Font> _releasedFonts = new();
    private bool _disposed;

    /// <param name="gpuDevice">
    /// The device that holds the text sprites' textures, so a test that reads a bound texture back reads it from its own device.
    /// Without one, the stub creates and disposes a device of its own.
    /// </param>
    public StubFontSystem(ushort glyphWidth = 8, ushort lineHeight = 16, StubGpuDevice? gpuDevice = null)
    {
        _glyphWidth = glyphWidth;
        _lineHeight = lineHeight;
        _gpuDevice = gpuDevice ?? StubGpuDevice.Create();
        _ownsGpuDevice = gpuDevice == null;
    }

    /// <summary>
    /// The fonts released so far, oldest first, through <see cref="ReleaseFont"/> or <see cref="Font.Dispose"/>.
    /// </summary>
    public IReadOnlyList<Font> ReleasedFonts => _releasedFonts.ToArray();

    /// <summary>
    /// Returns the font loaded before with the same path, size and settings, as Pixely's font system does, or a new one. The path
    /// is never read. The arguments are checked as there, but not against a font file, and none of them changes how text measures.
    /// </summary>
    public Font Load(string path, float size, FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended, FontHintingMode hintingMode = FontHintingMode.Normal,
        FontStyle style = FontStyle.Normal, IReadOnlyList<FontVariation>? variations = null, IReadOnlyList<FontFeature>? features = null, float letterSpacing = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        (FontKey key, ImmutableArray<FontVariation> normalizedVariations, ImmutableArray<FontFeature> normalizedFeatures) =
            FontKey.Create(path, size, rasterizationMode, hintingMode, style, variations, features, letterSpacing);
        if (!_fonts.TryGetValue(key, out Font? font))
        {
            unsafe
            {
                font = new Font(this, default, null, key, normalizedVariations, normalizedFeatures);
            }

            _fonts[key] = font;
        }

        return font;
    }

    /// <summary>
    /// Returns the sprite created before for the same text and font, or a new one on a blank texture of the text's measured size, in
    /// the format of Pixely's font system. As there, the sprite's texture is borrowed, so disposing it does nothing, and empty text
    /// throws <see cref="PixelyException"/>, as SDL_ttf cannot rasterize it.
    /// </summary>
    public TextSpriteAsset CreateTextSprite(string text, Font font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfNotLoaded(font);
        if (text.Length == 0)
        {
            throw new PixelyException("Text has zero width, so Pixely's font system cannot rasterize it.");
        }

        if (!_textSprites.TryGetValue((text, font), out TextSprite textSprite))
        {
            ShortSize size = Measure(text, font.Scale);
            // A texture has at least one pixel; text of only line breaks measures 0 wide.
            Texture backingTexture = _gpuDevice.CreateTexture(new ShortSize(Math.Max(size.Width, (ushort)1), Math.Max(size.Height, (ushort)1)), TextureFormat.R8G8B8A8UnormSrgb,
                TextureUsage.Sampler);
            BorrowedTexture borrowedTexture = new(backingTexture);
            textSprite = new TextSprite(new TextSpriteAsset(borrowedTexture, new ShortRectangle(0, 0, size.Width, size.Height)), borrowedTexture, backingTexture);
            _textSprites[(text, font)] = textSprite;
        }

        return textSprite.Asset;
    }

    /// <summary>
    /// Empty text measures 0 x 0. Otherwise each <c>\n</c> starts a line, and the text is as wide as its longest line. A character
    /// is a Unicode scalar value, so an emoji is one. A size over 65535 pixels throws <see cref="OverflowException"/>.
    /// </summary>
    public ShortSize MeasureTextSprite(string text, Font font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ThrowIfNotLoaded(font);
        return Measure(text, font.Scale);
    }

    /// <summary>
    /// Returns the font scaled before to the same size, or a new one, as Pixely's font system does, which <see cref="Font.GetScaled"/>
    /// reaches. A scaled font measures its glyph width and line height times its <see cref="Font.Scale"/>, each rounded half away
    /// from zero, and is released with its loaded font.
    /// </summary>
    Font? IFontSystem.GetScaled(Font font, float factor)
    {
        ThrowIfNotLoaded(font);
        if (!float.IsFinite(factor) || factor <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The factor must be finite and greater than 0.");
        }

        if (font.Unscaled != null)
        {
            return font.GetScaled(factor);
        }

        int sizeIn64ths = Font.ToSizeIn64ths(font.Size, factor);
        if (sizeIn64ths == Font.ToSizeIn64ths(font.Size, 1))
        {
            return font;
        }

        if (!_scaledFonts.TryGetValue((font, sizeIn64ths), out Font? scaledFont))
        {
            unsafe
            {
                scaledFont = new Font(this, default, null, font.Key, font.Variations, font.Features, font, sizeIn64ths / (64f * font.Size));
            }

            _scaledFonts[(font, sizeIn64ths)] = scaledFont;
        }

        return scaledFont;
    }

    /// <summary>
    /// Records the font in <see cref="ReleasedFonts"/>, and a later <see cref="Load"/> with its path, size and settings returns a new
    /// font, as in Pixely's font system. A font released already, or released after the stub is disposed, is left as it is, so a
    /// second <see cref="Font.Dispose"/> does nothing. Releasing a loaded font releases its scaled fonts first, as there.
    /// </summary>
    public void ReleaseFont(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (_disposed || _releasedFonts.Contains(font))
        {
            return;
        }

        if (!IsLoaded(font))
        {
            throw new InvalidOperationException($"The font {font.Path} at {font.Size} was not loaded by this {nameof(StubFontSystem)}.");
        }

        if (font.Unscaled != null)
        {
            _scaledFonts.Remove((font.Unscaled, Font.ToSizeIn64ths(font.Size, font.Scale)));
            _releasedFonts.Add(font);
            return;
        }

        foreach (Font scaledFont in _scaledFonts.Values.Where(scaledFont => ReferenceEquals(scaledFont.Unscaled, font)).ToArray())
        {
            ReleaseFont(scaledFont);
        }

        _fonts.Remove(font.Key);
        _releasedFonts.Add(font);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TextSprite textSprite in _textSprites.Values)
        {
            textSprite.BorrowedTexture.Invalidate();
            textSprite.BackingTexture.Dispose();
        }

        _textSprites.Clear();
        if (_ownsGpuDevice)
        {
            _gpuDevice.Dispose();
        }
    }

    private bool IsLoaded(Font font)
    {
        if (font.Unscaled != null)
        {
            return _scaledFonts.TryGetValue((font.Unscaled, Font.ToSizeIn64ths(font.Size, font.Scale)), out Font? scaled) && ReferenceEquals(scaled, font);
        }

        return _fonts.TryGetValue(font.Key, out Font? loaded) && ReferenceEquals(loaded, font);
    }

    // A released font's SDL font is closed in Pixely's font system, so using it there would crash the game.
    private void ThrowIfNotLoaded(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_releasedFonts.Contains(font))
        {
            throw new ObjectDisposedException(nameof(Font), $"The font {font.Path} at {font.Size} was released.");
        }

        if (!IsLoaded(font))
        {
            throw new InvalidOperationException($"The font {font.Path} at {font.Size} was not loaded by this {nameof(StubFontSystem)}.");
        }
    }

    private ShortSize Measure(string text, float scale)
    {
        if (text.Length == 0)
        {
            return new ShortSize(0, 0);
        }

        int longestLine = 0;
        int lineLength = 0;
        int lines = 1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                lines++;
                lineLength = 0;
                continue;
            }

            lineLength++;
            longestLine = Math.Max(longestLine, lineLength);
        }

        // Half away from zero, as the UI rounds edges, so a test's arithmetic is the same rule throughout.
        int glyphWidth = (int)Math.Round(_glyphWidth * (double)scale, MidpointRounding.AwayFromZero);
        int lineHeight = (int)Math.Round(_lineHeight * (double)scale, MidpointRounding.AwayFromZero);
        return new ShortSize(checked((ushort)(longestLine * glyphWidth)), checked((ushort)(lines * lineHeight)));
    }
}
