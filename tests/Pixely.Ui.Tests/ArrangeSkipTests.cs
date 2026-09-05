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
