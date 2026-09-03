using System.Numerics;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Pencuil;
using Pixely.Text;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public sealed class PencilScrollTests
{
    private const int BarLength = 400;
    private const int ContentExtent = 1000;
    private const int ViewportExtent = 200;

    [Test]
    public void ScrollBar_DrawsTrackAndThumbAndRegistersAScrollArea()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.MoveTo(0, 0);
        int offset = 0;

        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        int thickness = GuiStyles.Style.ScrollBarThickness;
        Assert.Multiple(() =>
        {
            Assert.That(pencil._coloredRectangleInstructions[0].Area, Is.EqualTo(new Rectangle(0, 0, thickness, BarLength)));
            Assert.That(pencil._coloredRectangleInstructions[1].Area, Is.EqualTo(new Rectangle(0, 0, thickness, 80)));
            Assert.That(pencil.IsOverScrollArea(new Vector2Int(5, 100)), Is.True);
            Assert.That(pencil.IsOverScrollArea(new Vector2Int(thickness, 100)), Is.False);
        });
    }

    [Test]
    public void ScrollBar_NonScrollableContentDrawsTrackWithoutThumb()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(0, 0);
        int offset = 0;

        pencil.ScrollBar(1, ref offset, 100, ViewportExtent, BarLength);

        Assert.That(pencil._coloredRectangleInstructions, Has.Count.EqualTo(1));
    }

    [Test]
    public void ScrollBar_OccupiesItsRectangleInTheLayout()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);
        int offset = 0;

        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(GuiStyles.Style.ScrollBarThickness, BarLength)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 20 + BarLength)));
        });
    }

    [Test]
    public void ScrollBar_ClampsAnOutOfRangeOffset()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(0, 0);
        int offset = 5000;

        bool changed = pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.Multiple(() =>
        {
            Assert.That(offset, Is.EqualTo(800));
            Assert.That(changed, Is.True);
        });
    }

    [Test]
    public void ScrollBar_PressOnTheThumbTakesCaptureWithoutMovingTheOffset()
    {
        Pencil pencil = CreatePencil();
        int offset = 400;
        PressAt(pencil, new Vector2Int(5, 190));

        pencil.MoveTo(0, 0);
        bool changed = pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.Multiple(() =>
        {
            Assert.That(pencil.IsCapturedBy(1), Is.True);
            Assert.That(offset, Is.EqualTo(400));
            Assert.That(changed, Is.False);
        });
    }

    [Test]
    public void ScrollBar_DraggingTheThumbMovesTheOffset()
    {
        Pencil pencil = CreatePencil();
        int offset = 400;
        PressAt(pencil, new Vector2Int(5, 190));
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        pencil.CursorJustPressed = false;
        pencil.UpdateCursor(new Vector2Int(5, 270));
        pencil.ResetInteractionData();
        pencil.MoveTo(0, 0);
        bool changed = pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            // The cursor moved 80px of the 320px travel, which is 200 of the 800 scrollable pixels
            Assert.That(offset, Is.EqualTo(600).Within(1));
        });
    }

    [Test]
    public void ScrollBar_DragKeepsTheGrabPointUnderTheCursor()
    {
        Pencil pencil = CreatePencil();
        int offset = 0;
        // Grab the thumb 60px below its start, so the thumb must stay 60px below the cursor
        PressAt(pencil, new Vector2Int(5, 60));
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        pencil.CursorJustPressed = false;
        pencil.UpdateCursor(new Vector2Int(5, 60));
        pencil.ResetInteractionData();
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CaptureGrabOffset, Is.EqualTo(60));
            Assert.That(offset, Is.Zero);
        });
    }

    [Test]
    public void ScrollBar_ReleasingTheButtonEndsTheDrag()
    {
        Pencil pencil = CreatePencil();
        int offset = 400;
        PressAt(pencil, new Vector2Int(5, 190));
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        pencil.SetCursorPressed(false);

        Assert.That(pencil.HasCapture, Is.False);
    }

    [Test]
    public void ScrollBar_PressOnTheTrackBelowTheThumbPagesForward()
    {
        Pencil pencil = CreatePencil();
        int offset = 0;
        PressAt(pencil, new Vector2Int(5, 350));

        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.Multiple(() =>
        {
            Assert.That(offset, Is.EqualTo(ViewportExtent));
            Assert.That(pencil.HasCapture, Is.False);
        });
    }

    [Test]
    public void ScrollBar_PressOnTheTrackAboveTheThumbPagesBack()
    {
        Pencil pencil = CreatePencil();
        int offset = 800;
        PressAt(pencil, new Vector2Int(5, 10));

        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.That(offset, Is.EqualTo(600));
    }

    [Test]
    public void ScrollBar_WheelOverTheBarScrollsTowardsTheContentEnd()
    {
        Pencil pencil = CreatePencil();
        int offset = 0;
        pencil.UpdateCursor(new Vector2Int(5, 100));
        pencil.AddWheelDelta(new Vector2(0, -1));

        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.That(offset, Is.EqualTo(GuiStyles.Style.ScrollStep));
    }

    [Test]
    public void ScrollBar_WheelIsConsumedSoOverlappingAreasDoNotScrollTwice()
    {
        Pencil pencil = CreatePencil();
        int first = 0;
        int second = 0;
        pencil.UpdateCursor(new Vector2Int(5, 100));
        pencil.AddWheelDelta(new Vector2(0, -1));

        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref first, ContentExtent, ViewportExtent, BarLength);
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(2, ref second, ContentExtent, ViewportExtent, BarLength);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(GuiStyles.Style.ScrollStep));
            Assert.That(second, Is.Zero);
        });
    }

    [Test]
    public void ScrollBar_WheelAwayFromTheBarIsIgnored()
    {
        Pencil pencil = CreatePencil();
        int offset = 0;
        pencil.UpdateCursor(new Vector2Int(500, 100));
        pencil.AddWheelDelta(new Vector2(0, -1));

        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        Assert.That(offset, Is.Zero);
    }

    [Test]
    public void ScrollBar_HorizontalUsesTheHorizontalWheelAxis()
    {
        Pencil pencil = CreatePencil();
        int offset = 0;
        pencil.UpdateCursor(new Vector2Int(100, 5));
        pencil.AddWheelDelta(new Vector2(1, 0));

        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength, Orientation.Horizontal);

        Assert.That(offset, Is.EqualTo(GuiStyles.Style.ScrollStep));
    }

    [Test]
    public void ScrollBar_HorizontalPlacesTheThumbAlongTheXAxis()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.MoveTo(0, 0);
        int offset = 800;

        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength, Orientation.Horizontal);

        Assert.That(pencil._coloredRectangleInstructions[1].Area, Is.EqualTo(new Rectangle(320, 0, 80, GuiStyles.Style.ScrollBarThickness)));
    }

    [Test]
    public void ScrollView_OffsetsAndClipsItsContent()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.MoveTo(0, 0);
        int offset = 20;

        using (pencil.ScrollView(1, 100, 200, ref offset, 1000))
        {
            pencil.Rectangle(50, 40, Colors.Red);
            pencil.Rectangle(50, 40, Colors.Blue);
        }

        Assert.Multiple(() =>
        {
            // The first row starts 20px above the viewport, so only its bottom 20px survive
            Assert.That(pencil._coloredRectangleInstructions[0].Area, Is.EqualTo(new Rectangle(0, 0, 50, 20)));
            Assert.That(pencil._coloredRectangleInstructions[1].Area, Is.EqualTo(new Rectangle(0, 20, 50, 40)));
        });
    }

    [Test]
    public void ScrollView_ReservesTheBarAlongTheTrailingEdge()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.MoveTo(0, 0);
        int offset = 0;

        using (pencil.ScrollView(1, 100, 200, ref offset, 1000))
        {
        }

        int thickness = GuiStyles.Style.ScrollBarThickness;
        Assert.That(pencil._coloredRectangleInstructions[0].Area, Is.EqualTo(new Rectangle(100 - thickness, 0, thickness, 200)));
    }

    [Test]
    public void ScrollView_DrawsTheBarAboveTheContent()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(-10, -10));
        pencil.MoveTo(0, 0);
        int offset = 0;

        using (pencil.ScrollView(1, 100, 200, ref offset, 1000))
        {
            pencil.Rectangle(100, 40, Colors.Red);
        }

        int contentDepth = pencil._coloredRectangleInstructions[0].Depth;
        int barDepth = pencil._coloredRectangleInstructions[1].Depth;
        Assert.That(barDepth, Is.GreaterThan(contentDepth));
    }

    [Test]
    public void ScrollView_RestoresTheLayoutAndClipAfterTheScope()
    {
        Pencil pencil = CreatePencil();
        pencil.MoveTo(10, 20);
        pencil.CurrentDirection = LayoutDirection.Bottom;
        int offset = 0;

        using (pencil.ScrollView(1, 100, 200, ref offset, 1000))
        {
            pencil.Rectangle(50, 40, Colors.Red);
        }

        Assert.Multiple(() =>
        {
            Assert.That(pencil.CurrentClip, Is.Null);
            Assert.That(pencil.CurrentDirection, Is.EqualTo(LayoutDirection.Bottom));
            Assert.That(pencil.CurrentSize, Is.EqualTo(new Vector2Int(100, 200)));
            Assert.That(pencil.CurrentPosition, Is.EqualTo(new Vector2Int(10, 220)));
        });
    }

    [Test]
    public void ScrollView_WheelOverTheContentScrollsAndWritesBackTheOffset()
    {
        Pencil pencil = CreatePencil();
        int offset = 0;
        pencil.UpdateCursor(new Vector2Int(20, 100));
        pencil.AddWheelDelta(new Vector2(0, -1));
        pencil.MoveTo(0, 0);

        // The content area must be registered as a scroll area by a prior frame for the
        // system-level wheel routing, but the widget resolves the delta on its own
        using (pencil.ScrollView(1, 100, 200, ref offset, 1000))
        {
        }

        Assert.That(offset, Is.EqualTo(GuiStyles.Style.ScrollStep));
    }

    [Test]
    public void ScrollView_ContentScrolledOutOfViewIsNotClickable()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(20, 300));
        pencil.CursorJustReleased = true;
        pencil.MoveTo(0, 0);
        int offset = 0;
        bool clicked;

        using (pencil.ScrollView(1, 100, 200, ref offset, 1000))
        {
            pencil.MoveTo(0, 300);
            clicked = pencil.ClickArea(50, 40);
        }

        Assert.Multiple(() =>
        {
            Assert.That(clicked, Is.False);
            Assert.That(pencil.IsOverInteractiveArea(new Vector2Int(20, 300)), Is.False);
        });
    }

    [Test]
    public void ScrollView_NonScrollableContentLeavesTheOffsetAtZero()
    {
        Pencil pencil = CreatePencil();
        pencil.UpdateCursor(new Vector2Int(20, 100));
        pencil.AddWheelDelta(new Vector2(0, -1));
        pencil.MoveTo(0, 0);
        int offset = 0;

        using (pencil.ScrollView(1, 100, 200, ref offset, 50))
        {
        }

        Assert.That(offset, Is.Zero);
    }

    [Test]
    public void FinishBuild_ReleasesCaptureHeldByAControlThatIsNoLongerBuilt()
    {
        Pencil pencil = CreatePencil();
        int offset = 400;
        PressAt(pencil, new Vector2Int(5, 190));
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);

        pencil.CapturedControlSeenThisFrame = false;
        pencil.FinishBuild();

        Assert.That(pencil.HasCapture, Is.False);
    }

    [Test]
    public void UpdateCursor_WhileCapturedKeepsTrackingOutsideTheWindow()
    {
        Pencil pencil = CreatePencil();
        int offset = 400;
        PressAt(pencil, new Vector2Int(5, 190));
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(null);

        Assert.Multiple(() =>
        {
            Assert.That(pencil.IsCursorInWindow, Is.True);
            Assert.That(pencil.CursorPosition, Is.EqualTo(new Vector2Int(5, 190)));
            Assert.That(pencil.HasCapture, Is.True);
            Assert.That(pencil.NeedsUpdate, Is.True);
        });
    }

    [Test]
    public void UpdateCursor_WhileCapturedInvalidatesEveryMotion()
    {
        Pencil pencil = CreatePencil();
        int offset = 400;
        PressAt(pencil, new Vector2Int(5, 190));
        pencil.MoveTo(0, 0);
        pencil.ScrollBar(1, ref offset, ContentExtent, ViewportExtent, BarLength);
        pencil.NeedsUpdate = false;

        pencil.UpdateCursor(new Vector2Int(5, 191));

        Assert.That(pencil.NeedsUpdate, Is.True);
    }

    private static void PressAt(Pencil pencil, Vector2Int position)
    {
        pencil.UpdateCursor(position);
        pencil.SetCursorPressed(true);
        pencil.CursorJustPressed = true;
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
            throw new AssertionException("Fonts should not be loaded by scroll tests.");
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
