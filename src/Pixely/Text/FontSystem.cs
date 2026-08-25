using System.Runtime.InteropServices;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Text;

internal class FontSystem: IFontSystem, IUpdatable
{
    // The cache owns the backing texture. Weak handles reuse its asset and track consumers that retain the Texture independently of the TextSpriteAsset.
    private readonly record struct CachedTextSprite(WeakReference<TextSpriteAsset> TextSprite, WeakReference<BorrowedTexture> BorrowedTexture, Texture BackingTexture);

    private readonly GpuMemorySystem _gpuMemorySystem;
    private readonly ContentSource _contentSource;
    private readonly List<Font> _fonts = new();
    private readonly Dictionary<(string path, ushort size, FontRasterizationMode rasterizationMode, FontHintingMode hintingMode), Font> _fontCache = new();
    private readonly Dictionary<(string text, Font font), CachedTextSprite> _textSpriteCache = new();

    private FontSystem(GpuMemorySystem gpuMemorySystem, ContentSource contentSource)
    {
        _gpuMemorySystem = gpuMemorySystem;
        _contentSource = contentSource;
    }

    public static FontSystem Create(GpuMemorySystem gpuMemorySystem, ContentSource contentSource)
    {
        if (!SDL3_ttf.TTF_Init())
        {
            SdlError.Throw(nameof(SDL3_ttf.TTF_Init));
        }

        return new FontSystem(gpuMemorySystem, contentSource);
    }

    public Font Load(
        string path,
        ushort size,
        FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended,
        FontHintingMode hintingMode = FontHintingMode.Normal)
    {
        (string path, ushort size, FontRasterizationMode rasterizationMode, FontHintingMode hintingMode) cacheKey =
            (path, size, rasterizationMode, hintingMode);
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
            stream.ReadExactly(new Span<byte>(nativeFontData, fontDataLength));

            Pointer<SDL_IOStream> sdlStream = SDL3.SDL_IOFromConstMem((IntPtr)nativeFontData, (UIntPtr)fontDataLength);
            SdlError.ThrowOnNull(sdlStream, nameof(SDL3.SDL_IOFromConstMem));

            Pointer<TTF_Font> ttfFont = SDL3_ttf.TTF_OpenFontIO(sdlStream, true, size);
            SdlError.ThrowOnNull(ttfFont, nameof(SDL3_ttf.TTF_OpenFontIO));

            SDL3_ttf.TTF_SetFontHinting(ttfFont, ToSdlHintingMode(hintingMode));

            Font font = new Font(this, ttfFont, nativeFontData, path, size, rasterizationMode, hintingMode);
            _fonts.Add(font);
            _fontCache[cacheKey] = font;

            return font;
        }
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
                SDL3.SDL_DestroySurface(surface);
            }
        }
    }

    public ShortSize MeasureTextSprite(string text, Font font)
    {
        int width = 0;
        int height = 0;
        unsafe
        {
            SDL3_ttf.TTF_GetStringSizeWrapped(font.TtfFont, text, 0, 0, &width, &height);
        }

        return new ShortSize((ushort)width, (ushort)height);
    }

    private Pointer<SDL_Surface> RenderTextToSurface(string text, Font font)
    {
        Color usedColor = Colors.White;
        unsafe
        {
            SDL_Color white = (SDL_Color)usedColor;
            // length is in bytes, length=0 means till '\0'
            // TODO: allow changing wrap_width
            Pointer<SDL_Surface> surface = font.RasterizationMode switch
            {
                FontRasterizationMode.Solid => SDL3_ttf.TTF_RenderText_Solid_Wrapped(font.TtfFont, text, 0, white, 0),
                FontRasterizationMode.Lcd => SDL3_ttf.TTF_RenderText_LCD_Wrapped(font.TtfFont, text, 0, white, default, 0),
                _ => SDL3_ttf.TTF_RenderText_Blended_Wrapped(font.TtfFont, text, 0, white, 0)
            };
            string renderFunctionName = font.RasterizationMode switch
            {
                FontRasterizationMode.Solid => nameof(SDL3_ttf.TTF_RenderText_Solid_Wrapped),
                FontRasterizationMode.Lcd => nameof(SDL3_ttf.TTF_RenderText_LCD_Wrapped),
                _ => nameof(SDL3_ttf.TTF_RenderText_Blended_Wrapped)
            };
            SdlError.ThrowOnNull(surface, renderFunctionName);

            return new Pointer<SDL_Surface>(surface);
        }
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
            Pointer<SDL_Surface> convertedSurface = SDL3.SDL_ConvertSurface(surface, SDL_PixelFormat.SDL_PIXELFORMAT_ABGR8888);
            SdlError.ThrowOnNull(convertedSurface, nameof(SDL3.SDL_ConvertSurface));

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
                SDL3.SDL_DestroySurface(convertedSurface);
            }
        }
    }

    public void ReleaseTextSprite(TextSpriteAsset textSprite)
    {
        (string text, Font font)? keyToRemove = null;
        foreach (KeyValuePair<(string text, Font font), CachedTextSprite> cacheEntry in _textSpriteCache)
        {
            if (cacheEntry.Value.BorrowedTexture.TryGetTarget(out BorrowedTexture? borrowedTexture) && ReferenceEquals(borrowedTexture, textSprite.Texture))
            {
                keyToRemove = cacheEntry.Key;
                break;
            }
        }

        if (keyToRemove is (string text, Font font) key && _textSpriteCache.TryGetValue(key, out CachedTextSprite cached))
        {
            ReleaseCachedTextSprite(key, cached);
            return;
        }

        textSprite.Dispose();
    }

    public void Update()
    {
        List<(string text, Font font)> keysToRemove = new();
        foreach (KeyValuePair<(string text, Font font), CachedTextSprite> cacheEntry in _textSpriteCache)
        {
            if (!cacheEntry.Value.BorrowedTexture.TryGetTarget(out BorrowedTexture? borrowedTexture) || borrowedTexture.IsDisposed)
            {
                keysToRemove.Add(cacheEntry.Key);
            }
        }

        foreach ((string text, Font font) key in keysToRemove)
        {
            if (_textSpriteCache.TryGetValue(key, out CachedTextSprite cached))
            {
                ReleaseCachedTextSprite(key, cached);
            }
        }
    }

    private void ReleaseCachedTextSprite((string text, Font font) key, CachedTextSprite cached)
    {
        _textSpriteCache.Remove(key);
        // Invalidate a surviving borrowed handle before releasing the native texture it refers to.
        if (cached.BorrowedTexture.TryGetTarget(out BorrowedTexture? borrowedTexture))
        {
            borrowedTexture.Dispose();
        }
        cached.BackingTexture.Dispose();
    }

    public void ReleaseFont(Font font)
    {
        _fonts.Remove(font);
        _fontCache.Remove((font.Path, font.Size, font.RasterizationMode, font.HintingMode));

        unsafe
        {
            SDL3_ttf.TTF_CloseFont(font.TtfFont);
        }

        font.FreeFontData();
    }

    public void Dispose()
    {
        foreach (CachedTextSprite cached in _textSpriteCache.Values)
        {
            if (cached.BorrowedTexture.TryGetTarget(out BorrowedTexture? borrowedTexture))
            {
                borrowedTexture.Dispose();
            }
            cached.BackingTexture.Dispose();
        }
        _textSpriteCache.Clear();

        List<Font> fonts = new(_fonts);
        foreach (Font font in fonts)
        {
            ReleaseFont(font);
        }

        SDL3_ttf.TTF_Quit();
    }
}
