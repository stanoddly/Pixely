using Pixely.Gpu;
using Pixely.Testing;
using Pixely.Text;
using Pixely.Utilities;

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

    // A stub device's texture: a paint pass records it without dereferencing anything.
    private readonly Texture _texture = StubGpuDevice.Create().CreateTexture(new ShortSize(256, 256), TextureFormat.R8G8B8A8Unorm, TextureUsage.Sampler);

    public int SpriteCount { get; private set; }

    public ShortSize Measure(string text) => new((ushort)(text.Length * CharacterWidth), LineHeight);

    public TextSpriteAsset CreateTextSprite(string text)
    {
        SpriteCount++;
        ShortSize size = Measure(text);
        return new TextSpriteAsset(_texture, new ShortRectangle(0, 0, size.Width, size.Height));
    }
}
