using System.Collections.Frozen;
using System.Collections.Immutable;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Testing;
using Pixely.Text;

namespace Pixely.Tests;

public sealed class TextSpriteAssetTests
{
    private const string FontPath = "fonts/GohuFont-Medium.ttf";

    private GpuDevice _gpuDevice = null!;
    private FontSystem _fontSystem = null!;

    [SetUp]
    public void SetUp()
    {
        byte[] fontData = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "GohuFont-Medium.ttf"));
        Dictionary<string, ImmutableArray<ContentFile>> files = new() { ["fonts"] = [new ByteContentFile(FontPath, fontData)] };
        Dictionary<string, ImmutableArray<string>> directories = new() { [""] = ["fonts"] };
        DictionaryContentSource contentSource = new(files.ToFrozenDictionary(), directories.ToFrozenDictionary());

        _gpuDevice = StubGpuDevice.Create();
        _fontSystem = FontSystem.Create(new GpuMemorySystem(_gpuDevice), contentSource);
    }

    [TearDown]
    public void TearDown()
    {
        _fontSystem.Dispose();
        _gpuDevice.Dispose();
    }

    [Test]
    public void Type_DoesNotExposeResourceOwnership()
    {
        Assert.That(typeof(IDisposable).IsAssignableFrom(typeof(TextSpriteAsset)), Is.False);
    }

    [Test]
    public void TextureDispose_LeavesTheSharedTextSpritesValid()
    {
        Font font = _fontSystem.Load(FontPath, 14);
        TextSpriteAsset first = font.CreateTextSprite("Hello");

        first.Texture.Dispose();
        TextSpriteAsset second = font.CreateTextSprite("Hello");

        Assert.Multiple(() =>
        {
            Assert.That(first.Texture.IsDisposed, Is.False);
            Assert.That(second.Texture.IsDisposed, Is.False);
        });
    }

    [Test]
    public void FontSystemDispose_InvalidatesEveryTextSprite()
    {
        Font font = _fontSystem.Load(FontPath, 14);
        TextSpriteAsset hello = font.CreateTextSprite("Hello");
        TextSpriteAsset world = font.CreateTextSprite("World");

        _fontSystem.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(hello.Texture.IsDisposed, Is.True);
            Assert.That(world.Texture.IsDisposed, Is.True);
        });
    }
}
