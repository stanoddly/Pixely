using System.Collections.Frozen;
using System.Collections.Immutable;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Text;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Tests;

public sealed class FontSystemTests
{
    private const string FontPath = "fonts/GohuFont-Medium.ttf";
    private const ushort FontSize = 14;
    private const string ScalableFontPath = "fonts/Scalable.ttf";

    private FontSystem _fontSystem = null!;
    private bool _hasScalableFont;

    [SetUp]
    public void SetUp()
    {
        byte[] fontData = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "GohuFont-Medium.ttf"));

        // GohuFont is a bitmap font, so scaling needs an outline font, which the repository does not ship.
        string scalablePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "Scalable.ttf");
        _hasScalableFont = File.Exists(scalablePath);
        byte[] scalableFontData = _hasScalableFont ? File.ReadAllBytes(scalablePath) : [];
        Dictionary<string, ImmutableArray<ContentFile>> files = new() { ["fonts"] = [new ByteContentFile(FontPath, fontData), new ByteContentFile(ScalableFontPath, scalableFontData)] };
        Dictionary<string, ImmutableArray<string>> directories = new() { [""] = ["fonts"] };
        DictionaryContentSource contentSource = new(files.ToFrozenDictionary(), directories.ToFrozenDictionary());

        // Loading and measuring never touch the device.
        _fontSystem = FontSystem.Create(new GpuMemorySystem(null!), contentSource);
    }

    [TearDown]
    public void TearDown()
    {
        _fontSystem.Dispose();
    }

    [Test]
    public void Load_DifferentStyles_ReturnsSeparateFonts()
    {
        Font normal = _fontSystem.Load(FontPath, FontSize);
        Font underlined = _fontSystem.Load(FontPath, FontSize, style: FontStyle.Underline);

        Assert.Multiple(() =>
        {
            Assert.That(underlined, Is.Not.SameAs(normal));
            Assert.That(normal.Style, Is.EqualTo(FontStyle.Normal));
            Assert.That(underlined.Style, Is.EqualTo(FontStyle.Underline));
            Assert.That(_fontSystem.Load(FontPath, FontSize, style: FontStyle.Underline), Is.SameAs(underlined));
        });
    }

    [TestCase(FontStyle.Normal, TTF_FontStyleFlags.TTF_STYLE_NORMAL)]
    [TestCase(FontStyle.Bold, TTF_FontStyleFlags.TTF_STYLE_BOLD)]
    [TestCase(FontStyle.Italic, TTF_FontStyleFlags.TTF_STYLE_ITALIC)]
    [TestCase(FontStyle.Underline, TTF_FontStyleFlags.TTF_STYLE_UNDERLINE)]
    [TestCase(FontStyle.Strikethrough, TTF_FontStyleFlags.TTF_STYLE_STRIKETHROUGH)]
    public void Load_Style_IsAppliedToTheFont(FontStyle style, TTF_FontStyleFlags expected)
    {
        Font font = _fontSystem.Load(FontPath, FontSize, style: style);

        unsafe
        {
            Assert.That(TTF_GetFontStyle(font.TtfFont), Is.EqualTo(expected));
        }
    }

    [Test]
    public void Measure_Bold_IsWiderThanNormal()
    {
        Font normal = _fontSystem.Load(FontPath, FontSize);
        Font bold = _fontSystem.Load(FontPath, FontSize, style: FontStyle.Bold);

        Assert.That(bold.Measure("Hello").Width, Is.GreaterThan(normal.Measure("Hello").Width));
    }

    [Test]
    public void ReleaseFont_StyledFont_IsLoadedAgainAfterwards()
    {
        Font underlined = _fontSystem.Load(FontPath, FontSize, style: FontStyle.Underline);

        underlined.Dispose();

        Assert.That(_fontSystem.Load(FontPath, FontSize, style: FontStyle.Underline), Is.Not.SameAs(underlined));
    }

    [Test]
    public void GetScaled_BitmapFont_ReturnsNull()
    {
        Font font = _fontSystem.Load(FontPath, FontSize);

        Assert.Multiple(() =>
        {
            Assert.That(font.GetScaled(2f), Is.Null, "a bitmap font comes only in its strike sizes");
            Assert.That(font.GetScaled(1f), Is.SameAs(font), "but it is at its own size");
        });
    }

    [Test]
    public void GetScaled_ScalableFont_MeasuresAtTheScaledSize()
    {
        Font font = LoadScalable();

        Font scaled = font.GetScaled(2f)!;
        ShortSize size = font.Measure("Hello");
        ShortSize scaledSize = scaled.Measure("Hello");

        Assert.Multiple(() =>
        {
            Assert.That(scaled.Size, Is.EqualTo(FontSize), "the size it was loaded at");
            Assert.That(scaled.Scale, Is.EqualTo(2f));
            Assert.That(scaledSize.Width, Is.InRange(size.Width * 2 - 2, size.Width * 2 + 2));
            Assert.That(scaledSize.Height, Is.InRange(size.Height * 2 - 2, size.Height * 2 + 2));
        });
    }

    [Test]
    public void GetScaled_FractionalFactor_IsNotRoundedToAWholeSize()
    {
        Font font = LoadScalable();

        unsafe
        {
            float pointSize = TTF_GetFontSize(font.GetScaled(1.5f)!.TtfFont);
            Assert.That(pointSize, Is.EqualTo(FontSize * 1.5f).Within(0.01f));
        }
    }

    [Test]
    public void GetScaled_IsCachedPerFactor_AndComposes()
    {
        Font font = LoadScalable();
        Font scaled = font.GetScaled(2f)!;

        Assert.Multiple(() =>
        {
            Assert.That(font.GetScaled(2f), Is.SameAs(scaled));
            Assert.That(font.GetScaled(1f), Is.SameAs(font));
            Assert.That(scaled.GetScaled(0.5f), Is.SameAs(font), "a scaled font scaled back is the loaded one");
            Assert.That(font.GetScaled(4f), Is.SameAs(scaled.GetScaled(2f)), "factors multiply");
        });
    }

    [Test]
    public void GetScaled_KeepsTheStyle()
    {
        Font font = LoadScalable(FontStyle.Underline);

        unsafe
        {
            Assert.That(TTF_GetFontStyle(font.GetScaled(2f)!.TtfFont), Is.EqualTo(TTF_FontStyleFlags.TTF_STYLE_UNDERLINE));
        }
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void GetScaled_AFactorThatIsNotPositive_Throws(float factor)
    {
        Font font = LoadScalable();

        Assert.That(() => font.GetScaled(factor), Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ReleaseFont_LoadedFont_ReleasesItsScaledFonts()
    {
        Font font = LoadScalable();
        Font scaled = font.GetScaled(2f)!;

        font.Dispose();
        Font reloaded = LoadScalable();

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.GetScaled(2f), Is.Not.SameAs(scaled));
            Assert.That(() => scaled.Dispose(), Throws.Nothing, "released already, so nothing is closed twice");
        });
    }

    private Font LoadScalable(FontStyle style = FontStyle.Normal)
    {
        Assume.That(_hasScalableFont, Is.True, "Needs an outline font copied to Fonts/Scalable.ttf in the test output.");
        return _fontSystem.Load(ScalableFontPath, FontSize, style: style);
    }

    [Test]
    public void ReleaseFont_ScaledFont_IsScaledAgainAfterwards()
    {
        Font font = LoadScalable();
        Font scaled = font.GetScaled(2f)!;

        scaled.Dispose();
        scaled.Dispose();

        Assert.That(font.GetScaled(2f), Is.Not.SameAs(scaled));
    }
}
