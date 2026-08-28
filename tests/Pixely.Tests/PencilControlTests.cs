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
        pencil.UpdateCursor(new Vector2Int(15, 25));

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
    public void ClickArea_RegistersClickWithoutDrawingOrHoverDependency()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);
        pencil.UpdateCursor(new Vector2Int(15, 25));

        bool clicked = pencil.ClickArea(30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
            Assert.That(pencil._coloredRectangleInstructions, Is.Empty);
            Assert.That(pencil._textureRegionInstructions, Is.Empty);
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.True);
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(30, 40)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 60)));
        });
    }

    [Test]
    public void ClickArea_Disabled_DoesNotRegisterInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(5, 5));
        pencil.CursorJustReleased = true;

        bool clicked = pencil.ClickArea(10, 10, enabled: false);

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.False);
        });
    }

    [Test]
    public void HoverArea_ReportsHoverWithoutRegisteringClickInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(5, 5));

        bool hovered = pencil.HoverArea(new Rectangle(0, 0, 10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(hovered, Is.True);
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.False);
        });
    }

    [Test]
    public void HoverRectangle_DrawsHoverBoundColorWithoutRegisteringInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);

        pencil.HoverRectangle(30, 40, Colors.Red, Colors.Blue);

        ColoredRectangleInstruction instruction = pencil._coloredRectangleInstructions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(instruction.Color, Is.EqualTo(Colors.Red));
            Assert.That(pencil.IsOverInteractiveArea(new Vector2Int(15, 25)), Is.False);
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 60)));
        });
    }

    [Test]
    public void Button_ContentSized_DrawsBorderBackgroundAndCenteredText()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);

        bool clicked = pencil.Button("OK", null!);

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
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

        bool clicked = pencil.Button(text);

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
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
        pencil.UpdateCursor(new Vector2Int(15, 25));

        bool clicked = pencil.Button("OK", null!, 80, 30);

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
            Assert.That(pencil._coloredRectangleInstructions[^1].Color, Is.EqualTo(GuiStyles.Style.ActiveColor));
            Assert.That(pencil._textureRegionInstructions.Single().Tint, Is.EqualTo((FColor)GuiStyles.Style.ActiveTextColor));
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(80, 30)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 50)));
        });
    }

    [Test]
    public void Button_Disabled_DrawsDisabledPresentationWithoutRegisteringInteraction()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(5, 5));
        pencil.CursorJustReleased = true;

        bool clicked = pencil.Button("OK", null!, enabled: false);

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
            Assert.That(pencil.IsOverInteractiveArea(pencil.CursorPosition), Is.False);
            Assert.That(pencil._coloredRectangleInstructions[^1].Color, Is.EqualTo(GuiStyles.Style.Background));
            Assert.That(pencil._textureRegionInstructions.Single().Tint, Is.EqualTo((FColor)GuiStyles.Style.InactiveColor));
        });
    }

    [Test]
    public void Button_ReleasedOverArea_ReturnsClicked()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(5, 5));
        pencil.CursorJustReleased = true;

        bool clicked = pencil.Button("OK", null!);

        Assert.That(clicked, Is.True);
    }

    [Test]
    public void UpdateCursor_MotionOutsideHoverAreas_DoesNotInvalidate()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-20, -20));
        pencil.HoverArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(-10, -10));

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CursorPosition, Is.EqualTo(new Vector2Int(-10, -10)));
            Assert.That(pencil.NeedsUpdate, Is.False);
        });
    }

    [Test]
    public void UpdateCursor_EnteringHoverArea_Invalidates()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.HoverArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 5));

        Assert.That(pencil.NeedsUpdate, Is.True);
    }

    [Test]
    public void UpdateCursor_MotionWithinHoverArea_DoesNotInvalidate()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(2, 2));
        pencil.HoverArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 5));

        Assert.That(pencil.NeedsUpdate, Is.False);
    }

    [Test]
    public void UpdateCursor_LeavingHoverArea_Invalidates()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(5, 5));
        pencil.HoverArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(20, 20));

        Assert.That(pencil.NeedsUpdate, Is.True);
    }

    [Test]
    public void UpdateCursor_MovingBetweenHoverAreas_Invalidates()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(5, 5));
        pencil.HoverArea(new Rectangle(0, 0, 10, 10));
        pencil.HoverArea(new Rectangle(20, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(25, 5));

        Assert.That(pencil.NeedsUpdate, Is.True);
    }

    [Test]
    public void UpdateCursor_EnteringClickArea_DoesNotInvalidate()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.ClickArea(new Rectangle(0, 0, 10, 10));
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CursorPosition, Is.EqualTo(new Vector2Int(5, 5)));
            Assert.That(pencil.NeedsUpdate, Is.False);
        });
    }

    [Test]
    public void UpdateCursor_HoverRectangleChangesOnlyRenderInstructionsOnTransitions()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.HoverRectangle(10, 10, Colors.Red, Colors.Blue);
        CompleteInstructions(pencil);

        pencil.UpdateCursor(new Vector2Int(5, 5));
        bool changedOnEnter = pencil.InstructionsChanged;
        Color enteredColor = pencil.CompletedColoredRectangleInstructions.Single().Color;
        pencil.InstructionsChanged = false;
        pencil.UpdateCursor(new Vector2Int(7, 7));
        bool changedWithinArea = pencil.InstructionsChanged;
        pencil.UpdateCursor(new Vector2Int(20, 20));

        Assert.Multiple(() =>
        {
            Assert.That(pencil.NeedsUpdate, Is.False);
            Assert.That(changedOnEnter, Is.True);
            Assert.That(enteredColor, Is.EqualTo(Colors.Blue));
            Assert.That(changedWithinArea, Is.False);
            Assert.That(pencil.InstructionsChanged, Is.True);
            Assert.That(pencil.CompletedColoredRectangleInstructions.Single().Color, Is.EqualTo(Colors.Red));
        });
    }

    [Test]
    public void UpdateCursor_ButtonHoverChangesColorAndTextTintWithoutRebuild()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.Button("OK", null!, 80, 30);
        CompleteInstructions(pencil);

        pencil.UpdateCursor(new Vector2Int(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(pencil.NeedsUpdate, Is.False);
            Assert.That(pencil.InstructionsChanged, Is.True);
            Assert.That(pencil.CompletedColoredRectangleInstructions[^1].Color, Is.EqualTo(GuiStyles.Style.ActiveColor));
            Assert.That(pencil.CompletedTextureRegionInstructions.Single().Tint, Is.EqualTo((FColor)GuiStyles.Style.ActiveTextColor));
        });
    }

    [Test]
    public void ResetInteractionData_RemovesHoverInstructionPatches()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.HoverRectangle(10, 10, Colors.Red, Colors.Blue);
        CompleteInstructions(pencil);

        pencil.ResetInteractionData();
        pencil.MoveTo(0, 0);
        pencil.Rectangle(10, 10, Colors.White);
        pencil.MarkInstructionsCompleted();
        pencil.CycleInstructions();
        pencil.InstructionsChanged = false;

        pencil.UpdateCursor(new Vector2Int(5, 5));

        Assert.Multiple(() =>
        {
            Assert.That(pencil.InstructionsChanged, Is.False);
            Assert.That(pencil.CompletedColoredRectangleInstructions.Single().Color, Is.EqualTo(Colors.White));
        });
    }

    private static void CompleteInstructions(Pencil pencil)
    {
        pencil.MarkInstructionsCompleted();
        pencil.CycleInstructions();
        pencil.NeedsUpdate = false;
        pencil.InstructionsChanged = false;
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
