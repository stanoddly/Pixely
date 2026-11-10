using System.Runtime.InteropServices;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Text;

internal class FontSystem: IFontSystem, IUpdatable
{
    // The cache owns the backing texture. Weak handles reuse its asset and track consumers that retain the Texture independently of the TextSpriteAsset.
    private readonly record struct CachedTextSprite(WeakReference<TextSpriteAsset> TextSprite, WeakReference<BorrowedTexture> BorrowedTexture, Texture BackingTexture);

    private readonly GpuMemorySystem _gpuMemorySystem;
    private readonly ContentSource _contentSource;
    private readonly List<Font> _fonts = new();
    private readonly Dictionary<(string path, ushort size, FontRasterizationMode rasterizationMode, FontHintingMode hintingMode, FontStyle style), Font> _fontCache = new();
    private readonly Dictionary<(string text, Font font), CachedTextSprite> _textSpriteCache = new();

    // Keyed by the loaded font they came from and their size in 64ths of a point, see Font.ToSizeIn64ths. Kept apart from _fonts
    // because each one is released with its loaded font, before the data they share is freed.
    private readonly Dictionary<(Font unscaled, int sizeIn64ths), Font> _scaledFonts = new();

    // Cleared after every Update, so the per-frame sweep allocates nothing.
    private readonly List<(string text, Font font)> _keysToRemove = new();

    private FontSystem(GpuMemorySystem gpuMemorySystem, ContentSource contentSource)
    {
        _gpuMemorySystem = gpuMemorySystem;
        _contentSource = contentSource;
    }

    public static FontSystem Create(GpuMemorySystem gpuMemorySystem, ContentSource contentSource)
    {
        if (!TTF_Init())
        {
            SdlError.Throw(nameof(TTF_Init));
        }

        return new FontSystem(gpuMemorySystem, contentSource);
    }

    public Font Load(
        string path,
        ushort size,
        FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended,
        FontHintingMode hintingMode = FontHintingMode.Normal,
        FontStyle style = FontStyle.Normal)
    {
        (string path, ushort size, FontRasterizationMode rasterizationMode, FontHintingMode hintingMode, FontStyle style) cacheKey =
            (path, size, rasterizationMode, hintingMode, style);
        if (_fontCache.TryGetValue(cacheKey, out Font? cachedFont))
        {
            return cachedFont;
        }

        ContentFile fontFile = _contentSource.GetFile(path);

        using Stream stream = fontFile.Open();
        int fontDataLength = (int)stream.Length;

        unsafe
        {
            byte* nativeFontData = (byte*)NativeMemory.Alloc((nuint)fontDataLength);
            Pointer<TTF_Font> ttfFont = default;
            try
            {
                stream.ReadExactly(new Span<byte>(nativeFontData, fontDataLength));

                Pointer<SDL_IOStream> sdlStream = SDL_IOFromConstMem((IntPtr)nativeFontData, (UIntPtr)fontDataLength);
                SdlError.ThrowOnNull(sdlStream, nameof(SDL_IOFromConstMem));

                ttfFont = TTF_OpenFontIO(sdlStream, true, size);
                SdlError.ThrowOnNull(ttfFont, nameof(TTF_OpenFontIO));

                ReadOnlySpan<byte> fontData = new(nativeFontData, fontDataLength);
                if (!TTF_FontIsScalable(ttfFont))
                {
                    SelectBitmapStrike(ttfFont, fontData, path, size);
                }

                TTF_SetFontHinting(ttfFont, ToSdlHintingMode(hintingMode));
                // FontStyle mirrors the values of TTF_FontStyleFlags.
                TTF_SetFontStyle(ttfFont, (TTF_FontStyleFlags)style);
            }
            catch
            {
                if (!ttfFont.IsNull)
                {
                    TTF_CloseFont(ttfFont);
                }

                NativeMemory.Free(nativeFontData);
                throw;
            }

            Font font = new Font(this, ttfFont, nativeFontData, path, size, rasterizationMode, hintingMode, style);
            _fonts.Add(font);
            _fontCache[cacheKey] = font;

            return font;
        }
    }

    Font? IFontSystem.GetScaled(Font font, float factor)
    {
        ArgumentNullException.ThrowIfNull(font);

        if (!float.IsFinite(factor) || factor <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The factor must be finite and greater than 0.");
        }

        if (font.Unscaled != null)
        {
            return font.GetScaled(factor);
        }

        int sizeIn64ths = Font.ToSizeIn64ths(font.Size, factor);
        if (sizeIn64ths == font.Size * 64)
        {
            return font;
        }

        if (_scaledFonts.TryGetValue((font, sizeIn64ths), out Font? cachedFont))
        {
            return cachedFont;
        }

        // A bitmap font's sizes are its strikes, so another size would land on whichever strike is nearest.
        if (!TTF_FontIsScalable(font.TtfFont))
        {
            return null;
        }

        // A copy shares the loaded font's data rather than reading the file again.
        Pointer<TTF_Font> ttfFont = TTF_CopyFont(font.TtfFont);
        SdlError.ThrowOnNull(ttfFont, nameof(TTF_CopyFont));

        try
        {
            SdlError.ThrowOnFalse(TTF_SetFontSize(ttfFont, sizeIn64ths / 64f), nameof(TTF_SetFontSize));
            TTF_SetFontHinting(ttfFont, ToSdlHintingMode(font.HintingMode));
            TTF_SetFontStyle(ttfFont, (TTF_FontStyleFlags)font.Style);
        }
        catch
        {
            TTF_CloseFont(ttfFont);
            throw;
        }

        Font scaledFont;
        unsafe
        {
            scaledFont = new Font(this, ttfFont, null, font.Path, font.Size, font.RasterizationMode, font.HintingMode, font.Style, font, sizeIn64ths / (64f * font.Size));
        }

        _scaledFonts[(font, sizeIn64ths)] = scaledFont;
        return scaledFont;
    }

    public TextSpriteAsset CreateTextSprite(string text, Font font)
    {
        (string text, Font font) cacheKey = (text, font);
        if (_textSpriteCache.TryGetValue(cacheKey, out CachedTextSprite cached))
        {
            if (cached.BorrowedTexture.TryGetTarget(out BorrowedTexture? cachedTexture) && !cachedTexture.IsDisposed)
            {
                if (cached.TextSprite.TryGetTarget(out TextSpriteAsset? cachedTextSprite))
                {
                    return cachedTextSprite;
                }

                ShortSize cachedSize = cachedTexture.Size;
                TextSpriteAsset textSprite = new(cachedTexture, new ShortRectangle(0, 0, cachedSize.Width, cachedSize.Height));
                cached.TextSprite.SetTarget(textSprite);
                return textSprite;
            }

            ReleaseCachedTextSprite(cacheKey, cached);
        }

        Pointer<SDL_Surface> surface = RenderTextToSurface(text, font);
        try
        {
            Texture backingTexture = CreateTextureFromSurface(surface);
            BorrowedTexture borrowedTexture = new BorrowedTexture(backingTexture);
            ShortSize size = borrowedTexture.Size;
            ShortRectangle imageRegion = new(0, 0, size.Width, size.Height);
            TextSpriteAsset textSprite = new(borrowedTexture, imageRegion);

            _textSpriteCache[cacheKey] = new CachedTextSprite(new WeakReference<TextSpriteAsset>(textSprite), new WeakReference<BorrowedTexture>(borrowedTexture), backingTexture);

            return textSprite;
        }
        finally
        {
            unsafe
            {
                SDL_DestroySurface(surface);
            }
        }
    }

    public ShortSize MeasureTextSprite(string text, Font font)
    {
        int width = 0;
        int height = 0;
        using NullTerminatedUtf8 utf8Text = new NullTerminatedUtf8(text);
        unsafe
        {
            TTF_GetStringSizeWrapped(font.TtfFont, utf8Text.Span, 0, 0, &width, &height);
        }

        return new ShortSize((ushort)width, (ushort)height);
    }

    private Pointer<SDL_Surface> RenderTextToSurface(string text, Font font)
    {
        SrgbColor usedColor = SrgbColors.White;
        using NullTerminatedUtf8 utf8Text = new NullTerminatedUtf8(text);
        unsafe
        {
            SDL_Color white = (SDL_Color)usedColor;
            // length is in bytes, length=0 means till '\0'
            // TODO: allow changing wrap_width
            Pointer<SDL_Surface> surface = font.RasterizationMode switch
            {
                FontRasterizationMode.Solid => TTF_RenderText_Solid_Wrapped(font.TtfFont, utf8Text.Span, 0, white, 0),
                FontRasterizationMode.Lcd => TTF_RenderText_LCD_Wrapped(font.TtfFont, utf8Text.Span, 0, white, default, 0),
                _ => TTF_RenderText_Blended_Wrapped(font.TtfFont, utf8Text.Span, 0, white, 0)
            };
            string renderFunctionName = font.RasterizationMode switch
            {
                FontRasterizationMode.Solid => nameof(TTF_RenderText_Solid_Wrapped),
                FontRasterizationMode.Lcd => nameof(TTF_RenderText_LCD_Wrapped),
                _ => nameof(TTF_RenderText_Blended_Wrapped)
            };
            SdlError.ThrowOnNull(surface, renderFunctionName);

            return new Pointer<SDL_Surface>(surface);
        }
    }

    // SDL_ttf treats the size of a bitmap-only face as an index into FreeType's strike list, clamped to its bounds, so every
    // requested size would silently land on some strike. Resolve the pixel size to the strike index from the font file instead.
    // Only TrueType and OpenType files carry a strike table Pixely can read; a bitmap PCF, BDF, FON or WOFF font is rejected rather than rendered at an unverified size.
    // SDL_ttf rejects a size of zero but truncates the index from a float, so index + 0.5 reaches every strike, including the first.
    private static unsafe void SelectBitmapStrike(Pointer<TTF_Font> ttfFont, ReadOnlySpan<byte> fontData, string path, ushort size)
    {
        IReadOnlyList<int> strikePixelSizes = SfntBitmapStrikes.ReadPixelSizes(fontData);
        int strikeIndex = -1;
        for (int index = 0; index < strikePixelSizes.Count; index++)
        {
            if (strikePixelSizes[index] == size)
            {
                strikeIndex = index;
                break;
            }
        }

        if (strikePixelSizes.Count == 0)
        {
            throw new PixelyException($"Font '{path}' is a bitmap font whose strike sizes Pixely cannot read (only TrueType and OpenType bitmap strikes are supported), so the size cannot be verified.");
        }

        if (strikeIndex < 0)
        {
            throw new PixelyException($"Font '{path}' is a bitmap font without a {size} px strike. Available sizes: {string.Join(", ", strikePixelSizes)}.");
        }

        SdlError.ThrowOnFalse(TTF_SetFontSize(ttfFont, strikeIndex + 0.5f), nameof(TTF_SetFontSize));
    }

    private static TTF_HintingFlags ToSdlHintingMode(FontHintingMode hintingMode)
    {
        return hintingMode switch
        {
            FontHintingMode.Light => TTF_HintingFlags.TTF_HINTING_LIGHT,
            FontHintingMode.Mono => TTF_HintingFlags.TTF_HINTING_MONO,
            FontHintingMode.None => TTF_HintingFlags.TTF_HINTING_NONE,
            FontHintingMode.LightSubpixel => TTF_HintingFlags.TTF_HINTING_LIGHT_SUBPIXEL,
            _ => TTF_HintingFlags.TTF_HINTING_NORMAL
        };
    }

    private Texture CreateTextureFromSurface(Pointer<SDL_Surface> surface)
    {
        unsafe
        {
            Pointer<SDL_Surface> convertedSurface = SDL_ConvertSurface(surface, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888);
            SdlError.ThrowOnNull(convertedSurface, nameof(SDL_ConvertSurface));

            try
            {
                SDL_Surface* sdlSurface = convertedSurface;
                ShortSize size = new ShortSize((ushort)sdlSurface->w, (ushort)sdlSurface->h);
                int pitch = sdlSurface->pitch;
                byte* pixelData = (byte*)sdlSurface->pixels;
                int width = sdlSurface->w;
                int height = sdlSurface->h;
                byte[] data = new byte[width * height * 4];

                fixed (byte* dataPtr = data)
                {
                    byte* dst = dataPtr;
                    for (int y = 0; y < height; y++)
                    {
                        byte* src = pixelData + (y * pitch);
                        Buffer.MemoryCopy(src, dst, width * 4, width * 4);
                        dst += width * 4;
                    }
                }

                RawImage image = new RawImage(data, size, PixelFormat.Rgba8888);
                Texture texture = _gpuMemorySystem.CreateTexture(image);

                return texture;
            }
            finally
            {
                SDL_DestroySurface(convertedSurface);
            }
        }
    }

    public int UpdateOrder => UpdateOrders.Maintenance;

    public void Update()
    {
        foreach (KeyValuePair<(string text, Font font), CachedTextSprite> cacheEntry in _textSpriteCache)
        {
            if (!cacheEntry.Value.BorrowedTexture.TryGetTarget(out BorrowedTexture? borrowedTexture) || borrowedTexture.IsDisposed)
            {
                _keysToRemove.Add(cacheEntry.Key);
            }
        }

        foreach ((string text, Font font) key in _keysToRemove)
        {
            if (_textSpriteCache.TryGetValue(key, out CachedTextSprite cached))
            {
                ReleaseCachedTextSprite(key, cached);
            }
        }

        _keysToRemove.Clear();
    }

    private void ReleaseCachedTextSprite((string text, Font font) key, CachedTextSprite cached)
    {
        _textSpriteCache.Remove(key);
        cached.BackingTexture.Dispose();
    }

    public void ReleaseFont(Font font)
    {
        if (font.Unscaled != null)
        {
            ReleaseScaledFont(font);
            return;
        }

        // Before the loaded font, whose data they read.
        List<Font> scaledFonts = new();
        foreach (Font scaledFont in _scaledFonts.Values)
        {
            if (ReferenceEquals(scaledFont.Unscaled, font))
            {
                scaledFonts.Add(scaledFont);
            }
        }

        foreach (Font scaledFont in scaledFonts)
        {
            ReleaseScaledFont(scaledFont);
        }

        _fonts.Remove(font);
        _fontCache.Remove((font.Path, font.Size, font.RasterizationMode, font.HintingMode, font.Style));

        unsafe
        {
            TTF_CloseFont(font.TtfFont);
        }

        font.FreeFontData();
    }

    // Only the font in the cache is closed, so disposing a scaled font twice, or after its loaded font, closes nothing twice.
    private void ReleaseScaledFont(Font font)
    {
        (Font unscaled, int sizeIn64ths) key = (font.Unscaled!, Font.ToSizeIn64ths(font.Size, font.Scale));
        if (!_scaledFonts.TryGetValue(key, out Font? cached) || !ReferenceEquals(cached, font))
        {
            return;
        }

        _scaledFonts.Remove(key);

        unsafe
        {
            TTF_CloseFont(font.TtfFont);
        }
    }

    public void Dispose()
    {
        foreach (CachedTextSprite cached in _textSpriteCache.Values)
        {
            if (cached.BorrowedTexture.TryGetTarget(out BorrowedTexture? borrowedTexture))
            {
                borrowedTexture.Invalidate();
            }
            cached.BackingTexture.Dispose();
        }
        _textSpriteCache.Clear();

        List<Font> fonts = new(_fonts);
        foreach (Font font in fonts)
        {
            ReleaseFont(font);
        }

        TTF_Quit();
    }
}
