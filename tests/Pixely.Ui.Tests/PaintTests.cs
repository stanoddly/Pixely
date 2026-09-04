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
            Background = Drawable.Solid(Red),
            Children =
            {
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Blue) }
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
        Column root = new() { Background = Drawable.Solid(Red) };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions[0].Texture, Is.Null,
            "a null texture is what lets the renderer keep everything on one pipeline");
    }

    [Test]
    public void Paint_BackgroundIsNotClippedByTheElementsOwnClip()
    {
        ClipBorder root = new()
        {
            Background = Drawable.Solid(Red),
            Content = new MeasuredBox(10, 10)
        };

        UiRoot uiRoot = Run(root, 100, 100);

        // ClipsContent clips content and children, not the element itself.
        Assert.That(uiRoot.Instructions[0].Clip, Is.EqualTo(new Rectangle(0, 0, 100, 100)));
    }

    [Test]
    public void Paint_ChildrenAreClippedByAClippingAncestor()
    {
        MeasuredBox child = new(200, 200) { Background = Drawable.Solid(Blue) };
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
        MeasuredBox leaf = new(200, 200) { Background = Drawable.Solid(Blue) };
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
            Content = new MeasuredBox(10, 10) { Background = Drawable.Solid(Blue) }
        };
        MeasuredBox sibling = new(10, 10) { Background = Drawable.Solid(Red) };
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
            Background = Drawable.Solid(Blue)
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
            Background = Drawable.Solid(Red),
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
        MeasuredBox hidden = new(10, 10) { IsVisible = false, Background = Drawable.Solid(Blue) };
        Column root = new() { Children = { hidden } };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Instructions, Is.Empty);
    }

    [Test]
    public void PaintContext_UnbalancedCustomDrawable_DoesNotLeakItsClip()
    {
        MeasuredBox sibling = new(10, 10) { Background = Drawable.Solid(Red) };
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
        uiRoot.SetViewportSize(new Vector2Int(width, height));
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
