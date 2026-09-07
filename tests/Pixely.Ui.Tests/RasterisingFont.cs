using Pixely.Gpu;
using Pixely.Text;
using Pixely.Utilities;
using SDL;

namespace Pixely.Ui.Tests;

/// <summary>
/// A <see cref="FixedWidthFont"/> that also rasterises, for the tests that have to paint text
/// rather than only lay it out. Kept apart from <see cref="FixedWidthFont"/> so that the tests
/// which must not rasterise still fail loudly when they do.
/// </summary>
internal sealed class RasterisingFont : IFont
{
    public const int CharacterWidth = FixedWidthFont.CharacterWidth;
    public const int LineHeight = FixedWidthFont.LineHeight;

    private readonly TestTexture _texture = new();

    public int SpriteCount { get; private set; }

    public ShortSize Measure(string text) => new((ushort)(text.Length * CharacterWidth), LineHeight);

    public TextSpriteAsset CreateTextSprite(string text)
    {
        SpriteCount++;
        ShortSize size = Measure(text);
        return new TextSpriteAsset(_texture, new ShortRectangle(0, 0, size.Width, size.Height));
    }

    /// <summary>A texture with no native handle: nothing in a paint pass dereferences it.</summary>
    private sealed class TestTexture : Texture
    {
        internal TestTexture() : base(Pointer<SDL_GPUTexture>.Null, new ShortSize(256, 256), TextureFormat.R8G8B8A8Unorm, 0)
        {
        }

        public override void Dispose()
        {
        }
    }
}
