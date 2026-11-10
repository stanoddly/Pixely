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
    private const string VariableFontPath = "fonts/Cantarell-VF.otf";

    private FontSystem _fontSystem = null!;

    [SetUp]
    public void SetUp()
    {
        string fontDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts");
        byte[] fontData = File.ReadAllBytes(Path.Combine(fontDirectory, "GohuFont-Medium.ttf"));
        byte[] variableFontData = File.ReadAllBytes(Path.Combine(fontDirectory, "Cantarell-VF.otf"));
        Dictionary<string, ImmutableArray<ContentFile>> files = new() { ["fonts"] = [new ByteContentFile(FontPath, fontData), new ByteContentFile(VariableFontPath, variableFontData)] };
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
    public void Measure_FractionalSize_DiffersFromTheNextWholeSize()
    {
        Font fractional = _fontSystem.Load(VariableFontPath, 14.67f);
        Font whole = _fontSystem.Load(VariableFontPath, 15);

        Assert.Multiple(() =>
        {
            Assert.That(fractional.Size, Is.EqualTo(14.67f));
            Assert.That(fractional.Measure("The quick brown fox jumps").Width, Is.LessThan(whole.Measure("The quick brown fox jumps").Width));
        });
    }

    [Test]
    public void Measure_TabularFigures_MeasuresEveryDigitTheSame()
    {
        Font proportional = _fontSystem.Load(VariableFontPath, 14.67f);
        Font tabular = _fontSystem.Load(VariableFontPath, 14.67f, features: [new FontFeature("tnum")]);

        Assert.Multiple(() =>
        {
            Assert.That(proportional.Measure("1111.11").Width, Is.LessThan(proportional.Measure("8888.88").Width));
            Assert.That(tabular.Measure("1111.11").Width, Is.EqualTo(tabular.Measure("8888.88").Width));
            Assert.That(tabular.Features, Is.EqualTo(new[] { new FontFeature("tnum") }));
        });
    }

    [Test]
    public void Load_WeightVariation_RendersHeavierGlyphsFromOneFile()
    {
        Font light = _fontSystem.Load(VariableFontPath, 24, variations: [new FontVariation("wght", 200)]);
        Font semibold = _fontSystem.Load(VariableFontPath, 24, variations: [new FontVariation("wght", 600)]);

        unsafe
        {
            Assert.Multiple(() =>
            {
                Assert.That(TTF_GetFontWeight(light.TtfFont), Is.EqualTo(200));
                Assert.That(TTF_GetFontWeight(semibold.TtfFont), Is.EqualTo(600));
                Assert.That(Coverage(light, "Hello"), Is.LessThan(Coverage(semibold, "Hello")));
                Assert.That(light.Variations, Is.EqualTo(new[] { new FontVariation("wght", 200) }));
            });
        }
    }

    [Test]
    public void Load_VariationOfAnAxisTheFontLacks_Throws()
    {
        PixelyException exception = Assert.Throws<PixelyException>(() => _fontSystem.Load(VariableFontPath, 14, variations: [new FontVariation("wdth", 90)]));

        Assert.That(exception.Message, Does.Contain(VariableFontPath).And.Contain("'wdth'"));
    }

    [Test]
    public void Load_VariationOfAFontWithoutAxes_Throws()
    {
        Assert.Throws<PixelyException>(() => _fontSystem.Load(FontPath, FontSize, variations: [new FontVariation("wght", 600)]));
    }

    [Test]
    public void Measure_LetterSpacing_AddsTheSpacingAfterEachCharacter()
    {
        Font normal = _fontSystem.Load(VariableFontPath, 14.67f);
        Font tracked = _fontSystem.Load(VariableFontPath, 14.67f, letterSpacing: 2);
        Font tight = _fontSystem.Load(VariableFontPath, 14.67f, letterSpacing: -0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(tracked.Measure("Hello").Width, Is.EqualTo(normal.Measure("Hello").Width + 10));
            Assert.That(tight.Measure("Hello").Width, Is.LessThan(normal.Measure("Hello").Width));
            Assert.That(tracked.LetterSpacing, Is.EqualTo(2f));
        });
    }

    [Test]
    public void Load_DifferentTypography_ReturnsSeparateFonts()
    {
        Font plain = _fontSystem.Load(VariableFontPath, 14);
        Font tabular = _fontSystem.Load(VariableFontPath, 14, features: [new FontFeature("tnum")]);
        Font semibold = _fontSystem.Load(VariableFontPath, 14, variations: [new FontVariation("wght", 600)]);
        Font tracked = _fontSystem.Load(VariableFontPath, 14, letterSpacing: 1);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { plain, tabular, semibold, tracked }, Is.Unique);
            Assert.That(_fontSystem.Load(VariableFontPath, 14, features: [new FontFeature("tnum")]), Is.SameAs(tabular));
            Assert.That(_fontSystem.Load(VariableFontPath, 14, variations: [new FontVariation("wght", 600)]), Is.SameAs(semibold));
            Assert.That(_fontSystem.Load(VariableFontPath, 14, features: []), Is.SameAs(plain));
        });
    }

    [Test]
    public void Load_ListsInAnotherOrderOrWithAnOverriddenTag_ReturnsTheSameFont()
    {
        Font font = _fontSystem.Load(VariableFontPath, 14, variations: [new FontVariation("wght", 600)], features: [new FontFeature("tnum"), new FontFeature("zero")]);

        Assert.Multiple(() =>
        {
            Assert.That(_fontSystem.Load(VariableFontPath, 14, variations: [new FontVariation("wght", 200), new FontVariation("wght", 600)],
                features: [new FontFeature("zero"), new FontFeature("tnum")]), Is.SameAs(font));
            Assert.That(font.Features, Is.EqualTo(new[] { new FontFeature("tnum"), new FontFeature("zero") }));
            Assert.That(font.Variations, Is.EqualTo(new[] { new FontVariation("wght", 600) }));
        });
    }

    [Test]
    public void ReleaseFont_FontWithTypography_IsLoadedAgainAfterwards()
    {
        Font tabular = _fontSystem.Load(VariableFontPath, 14, features: [new FontFeature("tnum")]);

        tabular.Dispose();

        Assert.That(_fontSystem.Load(VariableFontPath, 14, features: [new FontFeature("tnum")]), Is.Not.SameAs(tabular));
    }

    [Test]
    public void Load_BitmapFontAtAFractionalSize_ThrowsWithTheAvailableSizes()
    {
        PixelyException exception = Assert.Throws<PixelyException>(() => _fontSystem.Load(FontPath, 14.5f));

        Assert.That(exception.Message, Does.Contain("14.5 px").And.Contain("14"));
    }

    [TestCase(0.5f)]
    [TestCase(-1f)]
    [TestCase(65536f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Load_InvalidSize_Throws(float size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _fontSystem.Load(VariableFontPath, size));
    }

    [TestCase(float.NaN)]
    [TestCase(40000f)]
    public void Load_InvalidLetterSpacing_Throws(float letterSpacing)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _fontSystem.Load(VariableFontPath, 14, letterSpacing: letterSpacing));
    }

    [TestCase("tnu")]
    [TestCase("tnum,liga")]
    [TestCase("tn=m")]
    [TestCase("")]
    public void Load_InvalidFeatureTag_Throws(string tag)
    {
        Assert.Throws<ArgumentException>(() => _fontSystem.Load(VariableFontPath, 14, features: [new FontFeature(tag)]));
    }

    [Test]
    public void Load_InvalidTypographyValues_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _fontSystem.Load(VariableFontPath, 14, features: [new FontFeature("salt", -1)]));
            Assert.Throws<ArgumentException>(() => _fontSystem.Load(VariableFontPath, 14, variations: [new FontVariation("wght", float.NaN)]));
            Assert.Throws<ArgumentException>(() => _fontSystem.Load(VariableFontPath, 14, variations: [new FontVariation(null!, 600)]));
        });
    }

    // The alpha of every pixel of the rendered text, summed: a heavier weight covers more.
    private static unsafe long Coverage(Font font, string text)
    {
        using NullTerminatedUtf8 utf8Text = new(text);
        SDL_Surface* surface = TTF_RenderText_Blended(font.TtfFont, utf8Text.Span, 0, new SDL_Color { r = 255, g = 255, b = 255, a = 255 });
        Assert.That(surface != null, Is.True);
        try
        {
            long sum = 0;
            for (int y = 0; y < surface->h; y++)
            {
                uint* row = (uint*)((byte*)surface->pixels + y * surface->pitch);
                for (int x = 0; x < surface->w; x++)
                {
                    sum += row[x] >> 24;
                }
            }

            return sum;
        }
        finally
        {
            SDL_DestroySurface(surface);
        }
    }
}
