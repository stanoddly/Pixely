using System.Collections.Frozen;
using System.Collections.Immutable;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Text;
using SDL;

namespace Pixely.Tests;

public sealed class FontSystemTests
{
    private const string FontPath = "fonts/GohuFont-Medium.ttf";
    private const ushort FontSize = 14;

    private FontSystem _fontSystem = null!;

    [SetUp]
    public void SetUp()
    {
        byte[] fontData = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "GohuFont-Medium.ttf"));
        Dictionary<string, ImmutableArray<ContentFile>> files = new() { ["fonts"] = [new ByteContentFile(FontPath, fontData)] };
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
            Assert.That(SDL3_ttf.TTF_GetFontStyle(font.TtfFont), Is.EqualTo(expected));
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
}
