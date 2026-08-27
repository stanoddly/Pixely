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

    private static Pencil CreatePencil()
    {
        return new Pencil(new ThrowingFontSystem(), new TestClipboardService(), GuiStyles.Style);
    }

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
