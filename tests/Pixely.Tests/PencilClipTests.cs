using System.Numerics;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Pencuil;
using Pixely.Text;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public sealed class PencilClipTests
{
    [Test]
    public void WithClip_RectangleFullyInsideIsUnchanged()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 10);

        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
            pencil.Rectangle(20, 20, Colors.Red);
        }

        Assert.That(pencil._coloredRectangleInstructions.Single().Area, Is.EqualTo(new Rectangle(10, 10, 20, 20)));
    }

    [Test]
    public void WithClip_RectangleIsTrimmedToTheClipArea()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(80, 80);

        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
            pencil.Rectangle(40, 40, Colors.Red);
        }

        Assert.That(pencil._coloredRectangleInstructions.Single().Area, Is.EqualTo(new Rectangle(80, 80, 20, 20)));
    }

    [Test]
    public void WithClip_RectangleFullyOutsideIsNotEmitted()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(200, 200);

        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
            pencil.Rectangle(20, 20, Colors.Red);
        }

        Assert.That(pencil._coloredRectangleInstructions, Is.Empty);
    }

    [Test]
    public void WithClip_NestedScopesIntersect()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(0, 0);

        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        using (pencil.WithClip(new Rectangle(50, 0, 100, 100)))
        {
            pencil.Rectangle(200, 200, Colors.Red);
        }

        Assert.That(pencil._coloredRectangleInstructions.Single().Area, Is.EqualTo(new Rectangle(50, 0, 50, 100)));
    }

    [Test]
    public void WithClip_RestoresThePreviousClipOnDispose()
    {
        Pencil pencil = CreatePencil();

        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
        }

        Assert.That(pencil.CurrentClip, Is.Null);
    }

    [Test]
    public void WithClip_TextureUvsAreTrimmedProportionally()
    {
        Pencil pencil = CreatePencil();
        TestTexture texture = new TestTexture();

        using (pencil.WithClip(new Rectangle(0, 0, 50, 100)))
        {
            pencil.AddTexture(texture, new Rectangle(0, 0, 100, 100), new Vector4(0, 0, 1, 1), FColors.White);
        }

        TextureRegionInstruction instruction = pencil._textureRegionInstructions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Area, Is.EqualTo(new Rectangle(0, 0, 50, 100)));
            Assert.That(instruction.Uvs.X, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(instruction.Uvs.Z, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(instruction.Uvs.Y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(instruction.Uvs.W, Is.EqualTo(1.0f).Within(0.0001f));
        });
    }

    [Test]
    public void WithClip_TextureUvsAreTrimmedFromTheLeadingEdge()
    {
        Pencil pencil = CreatePencil();
        TestTexture texture = new TestTexture();

        using (pencil.WithClip(new Rectangle(25, 0, 100, 100)))
        {
            pencil.AddTexture(texture, new Rectangle(0, 0, 100, 100), new Vector4(0, 0, 1, 1), FColors.White);
        }

        TextureRegionInstruction instruction = pencil._textureRegionInstructions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Area, Is.EqualTo(new Rectangle(25, 0, 75, 100)));
            Assert.That(instruction.Uvs.X, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(instruction.Uvs.Z, Is.EqualTo(1.0f).Within(0.0001f));
        });
    }

    [Test]
    public void WithClip_FlippedTextureUvsKeepTheirOrientation()
    {
        Pencil pencil = CreatePencil();
        TestTexture texture = new TestTexture();

        using (pencil.WithClip(new Rectangle(0, 0, 50, 100)))
        {
            pencil.AddTexture(texture, new Rectangle(0, 0, 100, 100), new Vector4(1, 0, 0, 1), FColors.White);
        }

        TextureRegionInstruction instruction = pencil._textureRegionInstructions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Uvs.X, Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(instruction.Uvs.Z, Is.EqualTo(0.5f).Within(0.0001f));
        });
    }

    [Test]
    public void WithClip_ClickAreaOutsideTheClipDoesNotRegisterInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(150, 150));
        pencil.CursorJustReleased = true;
        pencil.MoveTo(140, 140);

        bool clicked;
        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
            clicked = pencil.ClickArea(40, 40);
        }

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
            Assert.That(pencil.IsOverInteractiveArea(new Vector2Int(150, 150)), Is.False);
        });
    }

    [Test]
    public void WithClip_ClickAreaIsClippedRatherThanDroppedWhenPartlyVisible()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(90, 90));
        pencil.CursorJustReleased = true;
        pencil.MoveTo(80, 80);

        bool clicked;
        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
            clicked = pencil.ClickArea(40, 40);
        }

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.True);
            Assert.That(pencil.IsOverInteractiveArea(new Vector2Int(110, 90)), Is.False);
        });
    }

    [Test]
    public void WithClip_HoverAreaOutsideTheClipDoesNotReportHover()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(150, 150));

        bool hovered;
        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
            hovered = pencil.HoverArea(new Rectangle(140, 140, 40, 40));
        }

        Assert.That(hovered, Is.False);
    }

    [Test]
    public void WithClip_ScrolledOutButtonDoesNotHoverPatch()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.MoveTo(0, 200);

        using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
        {
            pencil.Button("OK", null!, 80, 30);
        }

        pencil.MarkInstructionsCompleted();
        pencil.CycleInstructions();
        pencil.RenderDirty = false;

        pencil.UpdateCursor(new Vector2Int(5, 205));

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CompletedColoredRectangleInstructions, Is.Empty);
            Assert.That(pencil.RenderDirty, Is.False);
        });
    }

    [Test]
    public void ResetInteractionData_ClearsTheClip()
    {
        Pencil pencil = CreatePencil();
        pencil.CurrentClip = new Rectangle(0, 0, 10, 10);

        pencil.ResetInteractionData();

        Assert.That(pencil.CurrentClip, Is.Null);
    }

    private static Pencil CreatePencil()
    {
        return new Pencil(new TestFontSystem(), new TestClipboardService(), GuiStyles.Style);
    }

    private sealed class TestFontSystem : IFontSystem
    {
        private readonly TestTexture _texture = new TestTexture();

        public Font Load(string path, ushort size, FontRasterizationMode rasterizationMode = FontRasterizationMode.Blended, FontHintingMode hintingMode = FontHintingMode.Normal)
        {
            throw new AssertionException("Fonts should not be loaded by clip tests.");
        }

        public TextSpriteAsset CreateTextSprite(string text, Font font)
        {
            return new TextSpriteAsset(_texture, new ShortRectangle(0, 0, (ushort)(text.Length * 8), 10));
        }

        public ShortSize MeasureTextSprite(string text, Font font)
        {
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

        public string? GetText()
        {
            return null;
        }

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
