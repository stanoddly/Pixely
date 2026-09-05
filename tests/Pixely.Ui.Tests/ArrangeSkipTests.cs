using Pixely.Gpu;

namespace Pixely.Ui.Tests;

/// <summary>
/// Arrange runs only where something actually moved. A hover swapping a background is the common
/// case: it repaints, and re-placing the whole tree to produce the bounds it already has would be
/// the dominant cost of moving the mouse.
/// </summary>
public class ArrangeSkipTests
{
    [Test]
    public void APaintOnlyInvalidation_DoesNotRearrange()
    {
        MeasuredBox box = new(20, 20);
        Column root = new() { Children = { box } };
        Layout.Run(root, 100, 100);
        int arranges = box.ArrangeCount;

        box.Background = new SolidDrawable(new Color(255, 0, 0, 255));
        Layout.Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(box.ArrangeCount, Is.EqualTo(arranges));
            Assert.That(box.IsPaintDirty, Is.True, "it still has to repaint");
        });
    }

    [Test]
    public void ArrangingAtTheSameBounds_DoesNotRearrange()
    {
        MeasuredBox box = new(20, 20);
        Column root = new() { Children = { box } };
        Layout.Run(root, 100, 100);
        int arranges = box.ArrangeCount;

        Layout.Run(root, 100, 100);

        Assert.That(box.ArrangeCount, Is.EqualTo(arranges));
    }

    [Test]
    public void ArrangingAtDifferentBounds_Rearranges()
    {
        MeasuredBox box = new(20, 20) { Width = Sizing.Grow() };
        Column root = new() { Children = { box } };
        Layout.Run(root, 100, 100);
        int arranges = box.ArrangeCount;

        Layout.Run(root, 60, 100);

        Assert.Multiple(() =>
        {
            Assert.That(box.Bounds, Is.EqualTo(new Rectangle(0, 0, 60, 20)));
            Assert.That(box.ArrangeCount, Is.EqualTo(arranges + 1));
        });
    }

    [Test]
    public void AnArrangeInvalidation_RearrangesEvenWhereNothingMoves()
    {
        MeasuredBox box = new(20, 20);
        Column root = new() { Children = { box } };
        Layout.Run(root, 100, 100);
        int arranges = box.ArrangeCount;

        box.InvalidateArrange();
        Layout.Run(root, 100, 100);

        Assert.That(box.ArrangeCount, Is.EqualTo(arranges + 1));
    }

    /// <summary>
    /// The clip is an input the element did not choose, so it cannot be inferred from the bounds:
    /// a clipping ancestor can shrink without moving anything below it.
    /// </summary>
    [Test]
    public void ANarrowedInheritedClip_RearrangesADescendantThatDidNotMove()
    {
        MeasuredBox box = new(20, 20);
        Column root = new() { Children = { box } };
        Layout.Measure(root, Constraints.Tight(new Vector2Int(100, 100)));
        root.Arrange(new Rectangle(0, 0, 100, 100), new Rectangle(0, 0, 100, 100));
        int arranges = box.ArrangeCount;

        root.Arrange(new Rectangle(0, 0, 100, 100), new Rectangle(0, 0, 40, 100));

        Assert.Multiple(() =>
        {
            Assert.That(box.Bounds, Is.EqualTo(new Rectangle(0, 0, 20, 20)), "nothing moved");
            Assert.That(box.EffectiveClip, Is.EqualTo(new Rectangle(0, 0, 40, 100)));
            Assert.That(box.ArrangeCount, Is.EqualTo(arranges + 1));
        });
    }

    /// <summary>
    /// What an element laid itself out for was its place in the tree it was in, so joining another
    /// one has to invalidate it even where the new parent hands it the bounds it already had.
    /// </summary>
    [Test]
    public void ReparentingToAParentThatPlacesItIdentically_StillRearranges()
    {
        MeasuredBox box = new(20, 20);
        Column first = new() { Children = { box } };
        Column second = new();
        Overlay root = new() { Children = { first, second } };
        Layout.Run(root, 100, 100);
        int arranges = box.ArrangeCount;
        Rectangle bounds = box.Bounds;

        first.Children.Remove(box);
        second.Children.Add(box);
        Layout.Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(box.Bounds, Is.EqualTo(bounds), "the new parent places it where the old one did");
            Assert.That(box.ArrangeCount, Is.EqualTo(arranges + 1));
        });
    }

    [Test]
    public void ReparentingASubtree_InvalidatesItsDescendantsToo()
    {
        MeasuredBox leaf = new(20, 20);
        Column branch = new() { Children = { new Column { Children = { leaf } } } };
        Column first = new() { Children = { branch } };
        Column second = new();
        Overlay root = new() { Children = { first, second } };
        Layout.Run(root, 100, 100);
        int measures = leaf.MeasureCount;
        int arranges = leaf.ArrangeCount;

        first.Children.Remove(branch);
        second.Children.Add(branch);
        Layout.Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(leaf.MeasureCount, Is.EqualTo(measures + 1));
            Assert.That(leaf.ArrangeCount, Is.EqualTo(arranges + 1));
        });
    }

    /// <summary>
    /// A layer carries no parent to be invalidated through, so the root has to do it itself — and
    /// the same viewport size would otherwise leave every cached measurement looking current.
    /// </summary>
    [Test]
    public void AddingALayerThatWasLaidOutUnderAnotherRoot_LaysItOutAgain()
    {
        MeasuredBox leaf = new(20, 20);
        Column layer = new() { Children = { leaf } };
        UiRoot first = new();
        first.AddLayer(layer);
        first.SetViewportSize(new Vector2Int(320, 240));
        first.Update();
        int measures = leaf.MeasureCount;
        int arranges = leaf.ArrangeCount;

        first.RemoveLayer(layer);
        UiRoot second = new();
        second.AddLayer(layer);
        second.SetViewportSize(new Vector2Int(320, 240));
        second.Update();

        Assert.Multiple(() =>
        {
            Assert.That(leaf.MeasureCount, Is.EqualTo(measures + 1));
            Assert.That(leaf.ArrangeCount, Is.EqualTo(arranges + 1));
        });
    }

    /// <summary>
    /// The style is what a label resolves its font from, so replacing it changes a measurement that
    /// nothing on the label itself was told about.
    /// </summary>
    [Test]
    public void ReplacingTheStyle_ReachesElementsBelowTheLayer()
    {
        MeasuredBox leaf = new(20, 20);
        UiRoot root = new();
        root.AddLayer(new Column { Children = { new Column { Children = { leaf } } } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        int measures = leaf.MeasureCount;

        root.Style = new UiStyle();
        root.Update();

        Assert.That(leaf.MeasureCount, Is.EqualTo(measures + 1));
    }

    [Test]
    public void HoveringAButton_RepaintsWithoutRearrangingTheTree()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        MeasuredBox sibling = new(30, 30);
        UiRoot root = new();
        root.AddLayer(new Column { Children = { button, sibling } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        int arranges = sibling.ArrangeCount;

        root.PointerMoved(new Vector2Int(10, 10));
        bool repainted = root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(button.VisualState, Is.EqualTo(VisualState.Hovered));
            Assert.That(repainted, Is.True);
            Assert.That(sibling.ArrangeCount, Is.EqualTo(arranges), "a hover moves nothing");
        });
    }
}
