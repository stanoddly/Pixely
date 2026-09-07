using Pixely.Gpu;
using Pixely.Text;

namespace Pixely.Ui.Tests;

/// <summary>
/// A font with arithmetic instead of glyphs: every character is <see cref="CharacterWidth"/> wide
/// and every run <see cref="LineHeight"/> tall. Rasterising throws, so a layout pass that reaches
/// for a texture fails the test rather than needing a device.
/// </summary>
internal sealed class FixedWidthFont : IFont
{
    public const int CharacterWidth = 8;
    public const int LineHeight = 10;

    private readonly int _characterWidth;

    public FixedWidthFont(int characterWidth = CharacterWidth)
    {
        _characterWidth = characterWidth;
    }

    public int MeasureCount { get; private set; }

    public ShortSize Measure(string text)
    {
        MeasureCount++;
        return new ShortSize((ushort)(text.Length * _characterWidth), LineHeight);
    }

    public TextSpriteAsset CreateTextSprite(string text) =>
        throw new AssertionException($"Laying text out must not rasterise it, but \"{text}\" was rasterised.");
}
