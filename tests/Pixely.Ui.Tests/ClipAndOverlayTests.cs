namespace Pixely.Ui.Tests;

public class ClipAndOverlayTests
{
    [Test]
    public void EffectiveClip_WithoutAClippingAncestor_IsTheInheritedClip()
    {
        MeasuredBox child = new(20, 20);
        Column root = new() { Children = { child } };

        Layout.Run(root, 100, 100);

        Assert.That(child.EffectiveClip, Is.EqualTo(new Rectangle(0, 0, 100, 100)));
    }

    [Test]
    public void EffectiveClip_IsInheritedThroughNonClippingElements()
    {
        MeasuredBox leaf = new(200, 200);
        Column inner = new() { Children = { leaf } };
        Column clipper = new()
        {
            ClipsContent = true,
            Width = Sizing.Fixed(50),
            Height = Sizing.Fixed(50),
            Children = { inner }
        };
        Column root = new() { Children = { clipper } };

        Layout.Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(clipper.EffectiveClip, Is.EqualTo(new Rectangle(0, 0, 50, 50)));
            Assert.That(inner.EffectiveClip, Is.EqualTo(new Rectangle(0, 0, 50, 50)));
            Assert.That(leaf.EffectiveClip, Is.EqualTo(new Rectangle(0, 0, 50, 50)),
                "the clip reaches a leaf that is itself larger than the clipping ancestor");
        });
    }

    [Test]
    public void EffectiveClip_NestedClippersIntersect()
    {
        MeasuredBox leaf = new(10, 10);
        Column innerClipper = new()
        {
            ClipsContent = true,
            Margin = new Thickness(20, 20, 0, 0),
            Width = Sizing.Fixed(60),
            Height = Sizing.Fixed(60),
            Children = { leaf }
        };
        Column outerClipper = new()
        {
            ClipsContent = true,
            Width = Sizing.Fixed(50),
            Height = Sizing.Fixed(50),
            Children = { innerClipper }
        };
        Column root = new() { Children = { outerClipper } };

        Layout.Run(root, 200, 200);

        // The inner clipper starts at (20, 20) and is 60 across, but the outer one ends at 50.
        Assert.That(leaf.EffectiveClip, Is.EqualTo(new Rectangle(20, 20, 30, 30)));
    }

    [Test]
    public void EffectiveClip_WithDisjointClippers_IsEmpty()
    {
        MeasuredBox leaf = new(10, 10);
        Column innerClipper = new()
        {
            ClipsContent = true,
            Margin = new Thickness(80, 80, 0, 0),
            Width = Sizing.Fixed(20),
            Height = Sizing.Fixed(20),
            Children = { leaf }
        };
        Column outerClipper = new()
        {
            ClipsContent = true,
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(40),
            Children = { innerClipper }
        };
        Column root = new() { Children = { outerClipper } };

        Layout.Run(root, 200, 200);

        Assert.Multiple(() =>
        {
            Assert.That(leaf.EffectiveClip.Width, Is.Zero);
            Assert.That(leaf.EffectiveClip.Height, Is.Zero);
        });
    }

    [Test]
    public void Arrange_RefreshesDescendantClipsWhenAnAncestorMoves()
    {
        MeasuredBox leaf = new(10, 10);
        Column clipper = new()
        {
            ClipsContent = true,
            Width = Sizing.Fixed(40),
            Height = Sizing.Fixed(40),
            Children = { leaf }
        };
        Column root = new() { Children = { clipper } };

        Layout.Run(root, 100, 100);
        root.Arrange(new Rectangle(30, 30, 100, 100), new Rectangle(0, 0, 200, 200));

        Assert.That(leaf.EffectiveClip, Is.EqualTo(new Rectangle(30, 30, 40, 40)),
            "descendants are re-arranged with the ancestor, so their clips follow it");
    }

    [Test]
    public void Overlay_SizesToTheLargestChild()
    {
        MeasuredBox small = new(20, 40);
        MeasuredBox wide = new(60, 10);
        Overlay root = new() { Children = { small, wide } };

        Vector2Int size = Layout.MeasureUnbounded(root);

        Assert.That(size, Is.EqualTo(new Vector2Int(60, 40)));
    }

    [Test]
    public void Overlay_PlacesEveryChildInTheSameSpace()
    {
        MeasuredBox first = new(20, 20);
        MeasuredBox second = new(20, 20) { HorizontalAlignment = Alignment.End };
        Overlay root = new() { Children = { first, second } };

        Layout.Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 0, 20, 20)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(80, 0, 20, 20)));
        });
    }

    [Test]
    public void Overlay_GrowFillsBothAxesWhenDefinite()
    {
        MeasuredBox child = new(10, 10) { Width = Sizing.Grow(), Height = Sizing.Grow() };
        Overlay root = new() { Children = { child } };

        Layout.Run(root, 100, 80);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 100, 80)));
    }
}
