using Pixely.Gpu;

namespace Pixely.Ui.Tests;

/// <summary>
/// A border is four rectangles the drawable places itself, so what is asserted here is the
/// geometry: that the edges stay disjoint, stay inside the bounds whatever thickness they are
/// given, and leave the interior the fill is handed.
/// </summary>
public class BorderDrawableTests
{
    private static readonly Color Border = new(255, 0, 0, 255);
    private static readonly Color Fill = new(0, 0, 255, 255);

    [Test]
    public void Paint_EmitsFourEdgesAroundTheFill()
    {
        UiRoot root = Run(new BorderDrawable(Border, new Thickness(2), new SolidDrawable(Fill)), 40, 30);

        Assert.Multiple(() =>
        {
            Assert.That(root.Instructions, Has.Count.EqualTo(5));
            Assert.That(root.Instructions[0].Area, Is.EqualTo(new Rectangle(0, 0, 40, 2)), "top");
            Assert.That(root.Instructions[1].Area, Is.EqualTo(new Rectangle(0, 28, 40, 2)), "bottom");
            Assert.That(root.Instructions[2].Area, Is.EqualTo(new Rectangle(0, 2, 2, 26)), "left");
            Assert.That(root.Instructions[3].Area, Is.EqualTo(new Rectangle(38, 2, 2, 26)), "right");
            Assert.That(root.Instructions[4].Area, Is.EqualTo(new Rectangle(2, 2, 36, 26)), "the fill, painted last");
            Assert.That(root.Instructions[4].Tint, Is.EqualTo((FColor)Fill));
        });
    }

    [Test]
    public void Paint_EdgesAreDisjointAndInsideTheBoundsForAnAsymmetricThickness()
    {
        UiRoot root = Run(new BorderDrawable(Border, new Thickness(1, 2, 3, 4), new SolidDrawable(Fill)), 40, 30);

        Assert.Multiple(() =>
        {
            Assert.That(root.Instructions[0].Area, Is.EqualTo(new Rectangle(0, 0, 40, 2)), "top");
            Assert.That(root.Instructions[1].Area, Is.EqualTo(new Rectangle(0, 26, 40, 4)), "bottom");
            Assert.That(root.Instructions[2].Area, Is.EqualTo(new Rectangle(0, 2, 1, 24)), "left, only between the horizontal edges");
            Assert.That(root.Instructions[3].Area, Is.EqualTo(new Rectangle(37, 2, 3, 24)), "right");
            Assert.That(root.Instructions[4].Area, Is.EqualTo(new Rectangle(1, 2, 36, 24)), "the fill");
        });
    }

    [Test]
    public void Paint_ClampsAThicknessWiderThanTheBounds()
    {
        // Nothing downstream would: Thickness.Deflate clamps the interior alone, and the context
        // clips against the current clip rather than these bounds.
        UiRoot root = Run(new BorderDrawable(Border, new Thickness(30), new SolidDrawable(Fill)), 20, 20);

        Assert.Multiple(() =>
        {
            Assert.That(root.Instructions, Has.Count.EqualTo(1),
                "the top edge takes the whole element and the three empty rectangles left over are dropped");
            Assert.That(root.Instructions[0].Area, Is.EqualTo(new Rectangle(0, 0, 20, 20)));
            Assert.That(root.Instructions[0].Tint, Is.EqualTo((FColor)Border),
                "an empty interior is not handed to the fill, which a nine-patch would paint corners into anyway");
        });
    }

    [Test]
    public void Paint_WithoutAFillEmitsTheEdgesAlone()
    {
        UiRoot root = Run(new BorderDrawable(Border, new Thickness(2)), 40, 30);

        Assert.That(root.Instructions, Has.Count.EqualTo(4));
    }

    [Test]
    public void Paint_SkipsAnEdgeWithNoThickness()
    {
        UiRoot root = Run(new BorderDrawable(Border, new Thickness(0, 2, 0, 0), new SolidDrawable(Fill)), 40, 30);

        Assert.Multiple(() =>
        {
            Assert.That(root.Instructions, Has.Count.EqualTo(2), "the top edge and the fill");
            Assert.That(root.Instructions[0].Area, Is.EqualTo(new Rectangle(0, 0, 40, 2)));
            Assert.That(root.Instructions[1].Area, Is.EqualTo(new Rectangle(0, 2, 40, 28)));
        });
    }

    [Test]
    public void Paint_ClipsAFillTooBigForTheInterior()
    {
        // A nine-patch narrower than its own insets emits its corners at their natural size, so an
        // interior it does not fit in has to be clipped rather than trusted.
        UiRoot root = Run(new BorderDrawable(Border, new Thickness(2), new OverflowingDrawable()), 40, 30);

        Assert.That(root.Instructions[^1].Clip, Is.EqualTo(new Rectangle(2, 2, 36, 26)));
    }

    [Test]
    public void Paint_PlacesTheEdgesAroundBoundsAwayFromTheOrigin()
    {
        UiRoot root = new();
        Column layer = new() { Padding = new Thickness(5, 7, 0, 0) };
        layer.Children.Add(new Column
        {
            Background = new BorderDrawable(Border, new Thickness(2)),
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(30)
        });
        root.AddLayer(layer);
        root.SetViewportSize(new Vector2Int(100, 100));
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.Instructions, Has.Count.EqualTo(4));
            Assert.That(root.Instructions[0].Area, Is.EqualTo(new Rectangle(5, 7, 40, 2)), "top");
            Assert.That(root.Instructions[1].Area, Is.EqualTo(new Rectangle(5, 35, 40, 2)), "bottom");
            Assert.That(root.Instructions[2].Area, Is.EqualTo(new Rectangle(5, 9, 2, 26)), "left");
            Assert.That(root.Instructions[3].Area, Is.EqualTo(new Rectangle(43, 9, 2, 26)), "right");
        });
    }

    [Test]
    public void Constructor_RejectsANegativeEdge()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BorderDrawable(Border, new Thickness(-1, 0, 0, 0)));
    }

    /// <summary>Paints well outside what it was given, the way an undersized nine-patch does.</summary>
    private sealed class OverflowingDrawable : Drawable
    {
        public override void Paint(PaintContext context, Rectangle bounds)
        {
            context.FillRectangle(new Rectangle(bounds.X - 10, bounds.Y - 10, bounds.Width + 20, bounds.Height + 20), Fill);
        }
    }

    private static UiRoot Run(Drawable background, int width, int height)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Background = background, Width = Sizing.Fixed(width), Height = Sizing.Fixed(height) });
        root.SetViewportSize(new Vector2Int(width, height));
        root.Update();
        return root;
    }
}
