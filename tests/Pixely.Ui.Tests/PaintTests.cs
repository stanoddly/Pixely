using Pixely.Gpu;

namespace Pixely.Ui.Tests;

public class PaintTests
{
    private static readonly Color Red = new(255, 0, 0, 255);
    private static readonly Color Blue = new(0, 0, 255, 255);

    [Test]
    public void Paint_EmitsBackgroundsInTreeOrder()
    {
        Column root = new()
        {
            Background = new SolidDrawable(Red),
            Children =
            {
                new MeasuredBox(10, 10) { Background = new SolidDrawable(Blue) }
            }
        };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(uiRoot.Instructions, Has.Count.EqualTo(2));
            Assert.That(uiRoot.Instructions[0].Tint, Is.EqualTo((FColor)Red), "the parent paints first");
            Assert.That(uiRoot.Instructions[1].Tint, Is.EqualTo((FColor)Blue));
        });
    }

    [Test]
    public void Paint_SolidFillHasNoTexture()
    {
        Column root = new() { Background = new SolidDrawable(Red) };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions[0].Texture, Is.Null,
            "a null texture is what lets the renderer keep everything on one pipeline");
    }

    [Test]
    public void Paint_BackgroundIsNotClippedByTheElementsOwnClip()
    {
        ClipBorder root = new()
        {
            Background = new SolidDrawable(Red),
            Content = new MeasuredBox(10, 10)
        };

        UiRoot uiRoot = Run(root, 100, 100);

        // ClipsContent clips content and children, not the element itself.
        Assert.That(uiRoot.Instructions[0].Clip, Is.EqualTo(new Rectangle(0, 0, 100, 100)));
    }

    [Test]
    public void Paint_ChildrenAreClippedByAClippingAncestor()
    {
        MeasuredBox child = new(200, 200) { Background = new SolidDrawable(Blue) };
        ClipBorder clipper = new()
        {
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(40),
            Content = child
        };
        Column root = new() { Children = { clipper } };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions[0].Clip, Is.EqualTo(new Rectangle(0, 0, 40, 40)));
    }

    [Test]
    public void Paint_NestedClipsIntersect()
    {
        MeasuredBox leaf = new(200, 200) { Background = new SolidDrawable(Blue) };
        ClipBorder inner = new()
        {
            Margin = new Thickness(20, 20, 0, 0),
            Width = Sizing.Fixed(60),
            Height = Sizing.Fixed(60),
            Content = leaf
        };
        ClipBorder outer = new()
        {
            Width = Sizing.Fixed(50),
            Height = Sizing.Fixed(50),
            Content = inner
        };
        Column root = new() { Children = { outer } };

        UiRoot uiRoot = Run(root, 200, 200);

        Assert.That(uiRoot.Instructions[0].Clip, Is.EqualTo(new Rectangle(20, 20, 30, 30)));
    }

    [Test]
    public void Paint_ClipIsRestoredAfterAClippingSubtree()
    {
        ClipBorder clipper = new()
        {
            Width = Sizing.Fixed(30),
            Height = Sizing.Fixed(30),
            Content = new MeasuredBox(10, 10) { Background = new SolidDrawable(Blue) }
        };
        MeasuredBox sibling = new(10, 10) { Background = new SolidDrawable(Red) };
        Column root = new() { Children = { clipper, sibling } };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions[^1].Clip, Is.EqualTo(new Rectangle(0, 0, 100, 100)),
            "the sibling after a clipping subtree is not still clipped by it");
    }

    [Test]
    public void Paint_DropsQuadsOutsideTheirClip()
    {
        MeasuredBox offscreen = new(10, 10)
        {
            Margin = new Thickness(500, 0, 0, 0),
            Background = new SolidDrawable(Blue)
        };
        ClipBorder clipper = new()
        {
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(40),
            Content = offscreen
        };
        Column root = new() { Children = { clipper } };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions, Is.Empty, "a fully clipped quad costs a draw call for nothing");
    }

    [Test]
    public void Paint_DropsEmptyQuads()
    {
        Column root = new()
        {
            Background = new SolidDrawable(Red),
            Width = Sizing.Fixed(0),
            Height = Sizing.Fixed(0)
        };
        Column host = new() { Children = { root } };

        UiRoot uiRoot = Run(host, 100, 100);

        Assert.That(uiRoot.Instructions, Is.Empty);
    }

    [Test]
    public void Paint_InvisibleSubtreeIsSkipped()
    {
        MeasuredBox hidden = new(10, 10) { IsVisible = false, Background = new SolidDrawable(Blue) };
        Column root = new() { Children = { hidden } };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions, Is.Empty);
    }

    [Test]
    public void Paint_LabelScrolledOutOfView_IsNotRasterised()
    {
        RasterisingFont font = new();
        ScrollView view = new() { Height = Sizing.Fixed(RasterisingFont.LineHeight), Children = { new Label(font, "first"), new Label(font, "second") } };
        view.ScrollOffset = new Vector2Int(0, RasterisingFont.LineHeight);

        Run(view, 100, RasterisingFont.LineHeight);

        Assert.That(font.SpriteCount, Is.EqualTo(1), "only the label scrolled into the viewport paints");
    }

    [Test]
    public void Paint_LabelOverflowingItsBoundsIntoTheClip_IsStillRasterised()
    {
        RasterisingFont font = new();
        Label label = new(font, "wide text") { Width = Sizing.Fixed(1), Margin = new Thickness(-20, 0, 0, 0) };
        Column root = new() { Children = { label } };

        Run(root, 100, 100);

        Assert.That(font.SpriteCount, Is.EqualTo(1), "the sprite starts left of the clip but reaches into it");
    }

    [Test]
    public void Paint_EmptyLabelWithoutAFont_PaintsNothingRatherThanThrowing()
    {
        Column root = new() { Children = { new Label() } };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions, Is.Empty);
    }

    [Test]
    public void Paint_LabelWhoseContentGrewSinceItWasMeasured_IsNotCulledByItsOldSize()
    {
        RasterisingFont font = new();
        Label label = new(font, "a") { Margin = new Thickness(-RasterisingFont.CharacterWidth, 0, 0, 0) };
        Column root = new() { Children = { label } };
        Rectangle viewport = new(0, 0, 100, 100);

        // Laid out one character wide, which sits wholly left of the viewport; the content then
        // grows before paint, as a callback run between the two passes could make it.
        Layout.Run(root, 100, 100);
        label.Content = "ab";
        PaintContext context = new();
        context.Reset(viewport);
        root.Paint(context);

        Assert.That(font.SpriteCount, Is.EqualTo(1));
    }

    [Test]
    public void Paint_TextBoxScrolledOutOfView_IsNotRasterised()
    {
        RasterisingFont font = new();
        int rowHeight = RasterisingFont.LineHeight + 4;
        TextBox first = new(font) { Text = "first", Height = Sizing.Fixed(rowHeight) };
        TextBox second = new(font) { Text = "second", Height = Sizing.Fixed(rowHeight) };
        ScrollView view = new() { Height = Sizing.Fixed(rowHeight), Children = { first, second } };
        view.ScrollOffset = new Vector2Int(0, rowHeight);

        Run(view, 100, rowHeight);

        Assert.That(font.SpriteCount, Is.EqualTo(1), "only the field scrolled into the viewport paints");
    }

    [Test]
    public void PaintContext_UnbalancedCustomDrawable_DoesNotLeakItsClip()
    {
        MeasuredBox sibling = new(10, 10) { Background = new SolidDrawable(Red) };
        Column root = new()
        {
            Background = new LeakyDrawable(),
            Children = { sibling }
        };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions[^1].Clip, Is.EqualTo(new Rectangle(0, 0, 100, 100)),
            "a drawable that pushes a clip and never disposes it must not affect anything after it");
    }

    [Test]
    public void ClipScope_DisposedTwice_PopsOnlyOnce()
    {
        PaintContext context = new();
        context.Reset(new Rectangle(0, 0, 100, 100));

        ClipScope scope = context.PushClip(new Rectangle(0, 0, 50, 50));
        int depthWhilePushed = context.ClipDepth;
        scope.Dispose();
        scope.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(depthWhilePushed, Is.EqualTo(2));
            Assert.That(context.ClipDepth, Is.EqualTo(1), "the second dispose must not pop the viewport clip");
        });
    }

    [Test]
    public void ClipScope_DisposedOutOfOrder_DoesNotPopAnAncestor()
    {
        PaintContext context = new();
        context.Reset(new Rectangle(0, 0, 100, 100));

        ClipScope outer = context.PushClip(new Rectangle(0, 0, 80, 80));
        ClipScope inner = context.PushClip(new Rectangle(0, 0, 40, 40));

        outer.Dispose();

        Assert.That(context.CurrentClip, Is.EqualTo(new Rectangle(0, 0, 40, 40)),
            "disposing the outer scope while the inner one is on top must do nothing");

        inner.Dispose();
        outer.Dispose();
    }

    private static UiRoot Run(Element root, int width, int height)
    {
        UiRoot uiRoot = new();
        uiRoot.AddLayer(root);
        uiRoot.SetTargetSize(new Vector2Int(width, height));
        uiRoot.Update();
        return uiRoot;
    }

    private sealed class LeakyDrawable : Drawable
    {
        public override void Paint(PaintContext context, Rectangle bounds)
        {
            context.PushClip(new Rectangle(0, 0, 5, 5));
            context.FillRectangle(bounds, Blue);
        }
    }
}
