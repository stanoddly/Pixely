using Pixely.Gpu;
using Pixely.Input;
using Pixely.Pencuil;
using Pixely.Text;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public sealed class PencilControlTests
{
    [Test]
    public void Rectangle_DrawsWithoutRegisteringInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);
        pencil.CursorPosition = new Vector2Int(15, 25);

        pencil.Rectangle(30, 40, Colors.Red);

        Assert.Multiple(() =>
        {
            Assert.That(pencil._coloredRectangleInstructions, Is.EqualTo(new[]
            {
                new ColoredRectangleInstruction(0, new Rectangle(10, 20, 30, 40), Colors.Red)
            }));
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.False);
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(30, 40)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 60)));
        });
    }

    [Test]
    public void HitArea_RegistersInteractionWithoutDrawing()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);
        pencil.CursorPosition = new Vector2Int(15, 25);

        CursorState state = pencil.HitArea(30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(CursorState.Hovered));
            Assert.That(pencil._coloredRectangleInstructions, Is.Empty);
            Assert.That(pencil._textureRegionInstructions, Is.Empty);
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.True);
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(30, 40)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 60)));
        });
    }

    [Test]
    public void HitArea_Disabled_DoesNotRegisterInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(5, 5);
        pencil.CursorJustReleased = true;

        CursorState state = pencil.HitArea(10, 10, enabled: false);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(CursorState.None));
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.False);
        });
    }

    [Test]
    public void Panel_DrawsAndRegistersInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(5, 5);

        CursorState state = pencil.Panel(10, 10, Colors.Red);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(CursorState.Hovered));
            Assert.That(pencil._coloredRectangleInstructions, Has.Count.EqualTo(1));
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.True);
        });
    }

    [Test]
    public void Button_ContentSized_DrawsBorderBackgroundAndCenteredText()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);

        CursorState state = pencil.Button("OK", null!);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(CursorState.None));
            Assert.That(pencil._coloredRectangleInstructions, Is.EqualTo(new[]
            {
                new ColoredRectangleInstruction(0, new Rectangle(10, 20, 26, 20), GuiStyles.Style.InactiveColor),
                new ColoredRectangleInstruction(1, new Rectangle(12, 22, 22, 16), GuiStyles.Style.Background)
            }));
            Assert.That(pencil._textureRegionInstructions.Select(instruction => (instruction.Area, instruction.Tint)), Is.EqualTo(new[]
            {
                (new Rectangle(15, 25, 16, 10), (FColor)GuiStyles.Style.TextColor)
            }));
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(26, 20)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 40)));
        });
    }

    [Test]
    public void Button_TextSpriteAsset_ReusesProvidedTextureAndSize()
    {
        Pencil pencil = CreatePencil();
        TestTexture texture = new TestTexture();
        TextSpriteAsset text = new TextSpriteAsset(texture, new ShortRectangle(0, 0, 16, 10));
        pencil.MoveTo(10, 20);

        CursorState state = pencil.Button(text);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(CursorState.None));
            Assert.That(pencil._textureRegionInstructions.Single().Texture, Is.SameAs(texture));
            Assert.That(pencil._textureRegionInstructions.Single().Area, Is.EqualTo(new Rectangle(15, 25, 16, 10)));
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(26, 20)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 40)));
        });
    }

    [Test]
    public void Button_FixedSize_UsesHoverPresentation()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);
        pencil.CursorPosition = new Vector2Int(15, 25);

        CursorState state = pencil.Button("OK", null!, 80, 30);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(CursorState.Hovered));
            Assert.That(pencil._coloredRectangleInstructions, Is.EqualTo(new[]
            {
                new ColoredRectangleInstruction(0, new Rectangle(10, 20, 80, 30), GuiStyles.Style.InactiveColor),
                new ColoredRectangleInstruction(1, new Rectangle(12, 22, 76, 26), GuiStyles.Style.ActiveColor)
            }));
            Assert.That(pencil._textureRegionInstructions.Select(instruction => (instruction.Area, instruction.Tint)), Is.EqualTo(new[]
            {
                (new Rectangle(42, 30, 16, 10), (FColor)GuiStyles.Style.ActiveTextColor)
            }));
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(80, 30)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 50)));
        });
    }

    [Test]
    public void Button_Disabled_DrawsDisabledPresentationWithoutRegisteringInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(5, 5);
        pencil.CursorJustReleased = true;

        CursorState state = pencil.Button("OK", null!, enabled: false);

        Assert.Multiple(() =>
        {
            Assert.That(state, Is.EqualTo(CursorState.None));
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.False);
            Assert.That(pencil._coloredRectangleInstructions[^1].Color, Is.EqualTo(GuiStyles.Style.Background));
            Assert.That(pencil._textureRegionInstructions.Single().Tint, Is.EqualTo((FColor)GuiStyles.Style.InactiveColor));
        });
    }

    [Test]
    public void Button_ReleasedOverArea_ReturnsClicked()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(5, 5);
        pencil.CursorJustReleased = true;

        CursorState state = pencil.Button("OK", null!);

        Assert.That(state, Is.EqualTo(CursorState.Clicked));
    }

    [Test]
    public void UpdateCursor_MotionOutsideHitAreas_DoesNotInvalidate()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(-20, -20);
        pencil.HitArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(-10, -10), pressed: false);

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CursorPosition, Is.EqualTo(new Vector2Int(-10, -10)));
            Assert.That(pencil.NeedsUpdate, Is.False);
        });
    }

    [Test]
    public void UpdateCursor_EnteringHitArea_Invalidates()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(-10, -10);
        pencil.HitArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 5), pressed: false);

        Assert.That(pencil.NeedsUpdate, Is.True);
    }

    [Test]
    public void UpdateCursor_MotionWithinHitArea_DoesNotInvalidate()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(2, 2);
        pencil.HitArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 5), pressed: false);

        Assert.That(pencil.NeedsUpdate, Is.False);
    }

    [Test]
    public void UpdateCursor_LeavingHitArea_Invalidates()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(5, 5);
        pencil.HitArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(20, 20), pressed: false);

        Assert.That(pencil.NeedsUpdate, Is.True);
    }

    [Test]
    public void UpdateCursor_MovingBetweenHitAreas_Invalidates()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(5, 5);
        pencil.HitArea(new Rectangle(0, 0, 10, 10));
        pencil.HitArea(new Rectangle(20, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(25, 5), pressed: false);

        Assert.That(pencil.NeedsUpdate, Is.True);
    }

    [Test]
    public void UpdateCursor_ContinuousHoverTest_InvalidatesMotionWithinAndOutOfArea()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(2, 2);
        pencil.AddHoverTest(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 5), pressed: false);
        bool invalidatedWithinArea = pencil.NeedsUpdate;
        pencil.NeedsUpdate = false;
        pencil.UpdateCursor(new Vector2Int(20, 20), pressed: false);

        Assert.Multiple(() =>
        {
            Assert.That(invalidatedWithinArea, Is.True);
            Assert.That(pencil.NeedsUpdate, Is.True);
        });
    }

    [Test]
    public void UpdateCursor_PressedStateChange_Invalidates()
    {
        Pencil pencil = CreatePencil();
        pencil.CursorPosition = new Vector2Int(5, 5);
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 5), pressed: true);

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CursorPressed, Is.True);
            Assert.That(pencil.NeedsUpdate, Is.True);
        });
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
            throw new AssertionException("Fonts should not be loaded by control tests.");
        }

        public TextSpriteAsset CreateTextSprite(string text, Font font)
        {
            return new TextSpriteAsset(_texture, new ShortRectangle(0, 0, (ushort)(text.Length * 8), 10));
        }

        public ShortSize MeasureTextSprite(string text, Font font)
        {
            throw new AssertionException("Buttons should use the created text sprite size.");
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
