using Pixely.Gpu;
using Pixely.Input;
using Pixely.Pencuil;
using Pixely.Text;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public class PencilTextTests
{
    [Test]
    public void Text_WithEmptyString_DoesNotCallFontSystemOrChangeLayoutState()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);
        pencil.CurrentSize = new Vector2Int(30, 40);
        pencil.CurrentGap = 5;

        Assert.DoesNotThrow(() => pencil.Text("", null!, Colors.White));

        Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 20)));
        Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(30, 40)));
        Assert.That(pencil.CurrentGap, Is.EqualTo(5));
    }

    [Test]
    public void MeasureText_WithEmptyString_ReturnsZeroAndDoesNotCallFontSystem()
    {
        Pencil pencil = CreatePencil();

        Vector2Int size = pencil.MeasureText("", null!);

        Assert.That(size, Is.EqualTo(default(Vector2Int)));
    }

    [Test]
    public void MeasureText_WithText_ReturnsFontSystemMeasurement()
    {
        MeasuringFontSystem fontSystem = new MeasuringFontSystem();
        Pencil pencil = CreatePencil(fontSystem);

        Vector2Int size = pencil.MeasureText("abc", null!);

        Assert.Multiple(() =>
        {
            Assert.That(size, Is.EqualTo(new Vector2Int(24, 10)));
            Assert.That(fontSystem.MeasureCallCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Text_WithTextSpriteAsset_DrawsWithoutCallingFontSystem()
    {
        Pencil pencil = CreatePencil();
        TestTexture texture = new TestTexture();
        TextSpriteAsset text = new TextSpriteAsset(texture, new ShortRectangle(0, 0, 24, 10));
        pencil.MoveTo(10, 20);

        pencil.Text(text, Colors.White);

        TextureRegionInstruction instruction = pencil._textureRegionInstructions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Texture, Is.SameAs(texture));
            Assert.That(instruction.Area, Is.EqualTo(new Rectangle(10, 20, 24, 10)));
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(24, 10)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 30)));
        });
    }

    [Test]
    public void Text_WithEmptyTextSpriteAsset_DoesNotChangeLayoutState()
    {
        Pencil pencil = CreatePencil();
        TextSpriteAsset text = new TextSpriteAsset(new TestTexture(), default);
        pencil.MoveTo(10, 20);
        pencil.CurrentSize = new Vector2Int(30, 40);
        pencil.CurrentGap = 5;

        pencil.Text(text, Colors.White);

        Assert.Multiple(() =>
        {
            Assert.That(pencil._textureRegionInstructions, Is.Empty);
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 20)));
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(30, 40)));
            Assert.That(pencil.CurrentGap, Is.EqualTo(5));
        });
    }

    private static Pencil CreatePencil()
    {
        return CreatePencil(new ThrowingFontSystem());
    }

    private static Pencil CreatePencil(IFontSystem fontSystem) => new Pencil(fontSystem, new TestClipboardService(), GuiStyles.Style);

    private sealed class ThrowingFontSystem : IFontSystem
    {
        public Font Load(
            string path,
            ushort size,
            FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended,
            FontHintingMode hintingMode = FontHintingMode.Normal) =>
            throw new AssertionException("Font system should not be called.");

        public TextSpriteAsset CreateTextSprite(string text, Font font) => throw new AssertionException("Font system should not be called.");

        public ShortSize MeasureTextSprite(string text, Font font) => throw new AssertionException("Font system should not be called.");

        public void ReleaseFont(Font font) => throw new AssertionException("Font system should not be called.");

        public void Dispose()
        {
        }
    }

    private sealed class MeasuringFontSystem : IFontSystem
    {
        internal int MeasureCallCount { get; private set; }

        public Font Load(string path, ushort size, FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended, FontHintingMode hintingMode = FontHintingMode.Normal)
        {
            throw new AssertionException("Font loading is not expected.");
        }

        public TextSpriteAsset CreateTextSprite(string text, Font font)
        {
            throw new AssertionException("Text sprite creation is not expected.");
        }

        public ShortSize MeasureTextSprite(string text, Font font)
        {
            MeasureCallCount++;
            return new ShortSize((ushort)(text.Length * 8), 10);
        }

        public void ReleaseFont(Font font)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestClipboardService : IClipboardService
    {
        public bool HasText => false;

        public string? GetText() => null;

        public void SetText(string text)
        {
        }
    }

    private sealed class TestTexture : Texture
    {
        internal TestTexture()
            : base(Pointer<SDL_GPUTexture>.Null, new ShortSize(256, 256), TextureFormat.R8G8B8A8Unorm, 0)
        {
        }

        public override void Dispose()
        {
        }
    }
}
