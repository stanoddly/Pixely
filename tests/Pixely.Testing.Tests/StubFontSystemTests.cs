using Pixely.Gpu;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Testing.Tests;

public class StubFontSystemTests
{
    [Test]
    public void Load_SamePathSizeAndSettings_ReturnsTheSameFont()
    {
        using StubFontSystem fontSystem = new();

        Font first = fontSystem.Load("fonts/a.ttf", 20);
        Font second = fontSystem.Load("fonts/a.ttf", 20);
        Font underlined = fontSystem.Load("fonts/a.ttf", 20, style: FontStyle.Underline);
        Font larger = fontSystem.Load("fonts/a.ttf", 24);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(underlined, Is.Not.SameAs(first));
            Assert.That(larger, Is.Not.SameAs(first));
            Assert.That((first.Path, first.Size, underlined.Style), Is.EqualTo(("fonts/a.ttf", (ushort)20, FontStyle.Underline)));
        });
    }

    [TestCase("", 0, 0)]
    [TestCase("Score", 30, 11)]
    [TestCase("ab\ncde", 18, 22)]
    [TestCase("a\rb", 18, 11)]
    [TestCase("\U0001F600", 6, 11)]
    public void Measure_GivesEveryCharacterTheGlyphSize(string text, int width, int height)
    {
        using StubFontSystem fontSystem = new(glyphWidth: 6, lineHeight: 11);
        Font font = fontSystem.Load("fonts/a.ttf", 20);

        Assert.That(font.Measure(text), Is.EqualTo(new ShortSize((ushort)width, (ushort)height)));
    }

    [Test]
    public void CreateTextSprite_ReturnsASpriteOfTheMeasuredSizeOncePerTextAndFont()
    {
        using StubFontSystem fontSystem = new(glyphWidth: 6, lineHeight: 11);
        Font font = fontSystem.Load("fonts/a.ttf", 20);

        TextSpriteAsset first = font.CreateTextSprite("Score");
        TextSpriteAsset second = font.CreateTextSprite("Score");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(first.ImageRegion, Is.EqualTo(new ShortRectangle(0, 0, 30, 11)));
            Assert.That(first.Texture.Size, Is.EqualTo(new ShortSize(30, 11)));
            Assert.That(first.Texture.Format, Is.EqualTo(TextureFormat.R8G8B8A8UnormSrgb));
        });
    }

    [Test]
    public void CreateTextSprite_OfEmptyText_Throws()
    {
        using StubFontSystem fontSystem = new();
        Font font = fontSystem.Load("fonts/a.ttf", 20);

        Assert.Throws<PixelyException>(() => font.CreateTextSprite(""));
    }

    [Test]
    public void CreateTextSprite_WithTheTestsDevice_PutsABlankTextureOnIt()
    {
        using StubGpuDevice device = StubGpuDevice.Create();
        StubFontSystem fontSystem = new(glyphWidth: 1, lineHeight: 1, gpuDevice: device);
        TextSpriteAsset sprite = fontSystem.Load("fonts/a.ttf", 20).CreateTextSprite("ab");

        byte[] pixels = device.ReadTexture(sprite.Texture);
        fontSystem.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(pixels, Is.EqualTo(new byte[8]));
            // The test's device outlives the stub.
            Assert.That(device.CreateTexture(new ShortSize(1, 1), TextureFormat.R8G8B8A8Unorm, TextureUsage.Sampler).Size, Is.EqualTo(new ShortSize(1, 1)));
        });
    }

    // UiRoot.Update paints as well as lays out, so this also runs the label's CreateTextSprite.
    [Test]
    public void UiRootUpdate_WithALabelInTheStubsFont_LaysItOut()
    {
        using StubFontSystem fontSystem = new(glyphWidth: 6, lineHeight: 11);
        UiRoot root = new() { Style = new UiStyle(fontSystem.Load("fonts/a.ttf", 20)) };
        Label label = new("Score");
        root.AddLayer(new Column { Children = { label } });
        root.SetTargetSize(new Vector2Int(640, 360));

        root.Update();

        Assert.That((label.Bounds.Width, label.Bounds.Height), Is.EqualTo((30, 11)));
    }

    [Test]
    public void Dispose_OfAFont_RecordsItAndALaterLoadReturnsANewOne()
    {
        using StubFontSystem fontSystem = new();
        Font font = fontSystem.Load("fonts/a.ttf", 20);

        font.Dispose();
        Font reloaded = fontSystem.Load("fonts/a.ttf", 20);

        Assert.Multiple(() =>
        {
            Assert.That(fontSystem.ReleasedFonts, Is.EqualTo(new[] { font }));
            Assert.That(reloaded, Is.Not.SameAs(font));
        });
    }

    [Test]
    public void ReleaseFont_Twice_DoesNothingAndKeepsTheReloadedFont()
    {
        using StubFontSystem fontSystem = new();
        Font font = fontSystem.Load("fonts/a.ttf", 20);
        font.Dispose();
        Font reloaded = fontSystem.Load("fonts/a.ttf", 20);

        font.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(fontSystem.Load("fonts/a.ttf", 20), Is.SameAs(reloaded));
            Assert.That(fontSystem.ReleasedFonts, Is.EqualTo(new[] { font }));
        });
    }

    [Test]
    public void Measure_OfAReleasedFont_Throws()
    {
        using StubFontSystem fontSystem = new();
        Font font = fontSystem.Load("fonts/a.ttf", 20);
        font.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => font.Measure("Score"));
            Assert.Throws<ObjectDisposedException>(() => font.CreateTextSprite("Score"));
        });
    }

    [Test]
    public void Measure_OfAnotherFontSystemsFont_Throws()
    {
        using StubFontSystem fontSystem = new();
        using StubFontSystem other = new();
        Font font = other.Load("fonts/a.ttf", 20);

        Assert.Throws<InvalidOperationException>(() => fontSystem.MeasureTextSprite("Score", font));
    }

    [Test]
    public void ReleaseFont_OfAnotherFontSystemsFont_Throws()
    {
        using StubFontSystem fontSystem = new();
        using StubFontSystem other = new();
        Font font = other.Load("fonts/a.ttf", 20);

        Assert.Throws<InvalidOperationException>(() => fontSystem.ReleaseFont(font));
    }

    [Test]
    public void FontDispose_AfterTheStubIsDisposed_DoesNothing()
    {
        StubFontSystem fontSystem = new();
        Font font = fontSystem.Load("fonts/a.ttf", 20);
        fontSystem.Dispose();

        Assert.DoesNotThrow(font.Dispose);
    }

    [Test]
    public void Load_AfterDispose_Throws()
    {
        StubFontSystem fontSystem = new();
        fontSystem.Dispose();

        Assert.Throws<ObjectDisposedException>(() => fontSystem.Load("fonts/a.ttf", 20));
    }

    [Test]
    public void ReleasedFonts_IsACopy()
    {
        using StubFontSystem fontSystem = new();
        IReadOnlyList<Font> before = fontSystem.ReleasedFonts;

        fontSystem.Load("fonts/a.ttf", 20).Dispose();

        Assert.That(before, Is.Empty);
    }
}
