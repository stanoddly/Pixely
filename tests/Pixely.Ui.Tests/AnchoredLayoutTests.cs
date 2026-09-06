namespace Pixely.Ui.Tests;

/// <summary>
/// Anchoring is what a popup needs and an offset cannot give it: a placement decided after the
/// element has been measured, so it can be kept on screen, and one that costs no measure to change.
/// </summary>
public class AnchoredLayoutTests
{
    [Test]
    public void AChildAnchoredInsideTheViewport_SitsAtItsAnchor()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(100, 50);
        Element host = Anchored(child);

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(100, 50, 40, 20)));
    }

    [Test]
    public void ACentredPivot_PutsTheAnchorThroughTheMiddle()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(100, 50);
        Element host = Anchored(child, new AnchoredLayout(Alignment.Start, Alignment.Center));

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(100, 40, 40, 20)),
            "the anchor names a point on the element's own centre line, which needs its height to resolve");
    }

    [Test]
    public void AnEndPivot_PutsTheAnchorOnTheTrailingEdge()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(100, 50);
        Element host = Anchored(child, new AnchoredLayout(Alignment.End, Alignment.End));

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(60, 30, 40, 20)));
    }

    [Test]
    public void AnOffset_MovesTheChildAfterThePivot()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(100, 50);
        child.Offset = new Vector2Int(18, 0);
        Element host = Anchored(child, new AnchoredLayout(Alignment.Start, Alignment.Center));

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(118, 40, 40, 20)));
    }

    [TestCase(400, 10, 280, 10, TestName = "past the right edge")]
    [TestCase(-40, 10, 0, 10, TestName = "past the left edge")]
    [TestCase(10, 300, 10, 220, TestName = "past the bottom edge")]
    [TestCase(10, -40, 10, 0, TestName = "past the top edge")]
    public void AnAnchorOutsideTheViewport_IsBroughtBackIn(int anchorX, int anchorY, int expectedX, int expectedY)
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(anchorX, anchorY);
        Element host = Anchored(child);

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(expectedX, expectedY, 40, 20)));
    }

    [Test]
    public void AChildLargerThanTheViewport_PinsToTheLeadingEdge()
    {
        MeasuredBox child = Sized(400, 300);
        child.Anchor = new Vector2Int(100, 100);
        Element host = Anchored(child);

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(0, 0, 400, 300)),
            "there is no position that fits, and clamping between crossed bounds would throw");
    }

    [Test]
    public void AMarginIsKeptClearWhenClamping()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(400, 10);
        child.Margin = new Thickness(6);
        Element host = Anchored(child);

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(274, 16, 40, 20)),
            "clamping the border box instead would push the margin off screen and call it on screen");
    }

    [Test]
    public void AResizedViewport_ReclampsWithoutRemeasuring()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(300, 10);
        Element host = Anchored(child);
        Layout.Run(host, 320, 240);
        int measures = child.MeasureCount;

        Layout.Run(host, 200, 240);

        Assert.Multiple(() =>
        {
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(160, 10, 40, 20)));
            Assert.That(child.MeasureCount, Is.EqualTo(measures), "the child's size did not change, so nothing had to ask it again");
        });
    }

    [Test]
    public void AnAnchoredChild_DoesNotStretchAHostThatFitsItsChildren()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(200, 100);
        Element host = Anchored(child);

        Vector2Int size = Layout.MeasureUnbounded(host);

        Assert.That(size, Is.EqualTo(new Vector2Int(40, 20)),
            "an anchor places an element without resizing what holds it, exactly as an offset does");
    }

    [Test]
    public void ChangingAnAnchor_ArrangesWithoutMeasuringOrDisturbingASibling()
    {
        MeasuredBox moving = Sized(40, 20);
        MeasuredBox still = Sized(40, 20);
        still.Anchor = new Vector2Int(10, 10);
        Element host = new() { Layout = AnchoredLayout.TopLeft, Children = { moving, still } };
        Layout.Run(host, 320, 240);
        int measures = moving.MeasureCount;
        int stillArranges = still.ArrangeCount;

        moving.Anchor = new Vector2Int(60, 60);
        Layout.Run(host, 320, 240);

        Assert.Multiple(() =>
        {
            Assert.That(moving.Bounds, Is.EqualTo(new Rectangle(60, 60, 40, 20)));
            Assert.That(moving.MeasureCount, Is.EqualTo(measures), "an anchor is not an input to measure");
            Assert.That(still.ArrangeCount, Is.EqualTo(stillArranges), "and moving one child does not rearrange another");
        });
    }

    [Test]
    public void MarginsLargerThanTheChild_DoNotPlaceItPastTheFarEdge()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(1000, 1000);
        child.Margin = new Thickness(-30, -30, -30, -30);
        Element host = Anchored(child);

        Layout.Run(host, 320, 240);

        // The margin box would be 20 wide by 40 short of nothing; treated as an extent it is negative,
        // and clamping between crossed bounds would let the anchor through unchanged.
        Assert.Multiple(() =>
        {
            Assert.That(child.Bounds.X, Is.LessThanOrEqualTo(320));
            Assert.That(child.Bounds.Y, Is.LessThanOrEqualTo(240));
        });
    }

    [Test]
    public void AStretchPivot_ReadsAsStart()
    {
        MeasuredBox child = Sized(40, 20);
        child.Anchor = new Vector2Int(100, 50);
        Element host = Anchored(child, new AnchoredLayout(Alignment.Stretch, Alignment.Stretch));

        Layout.Run(host, 320, 240);

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(100, 50, 40, 20)),
            "there is no extent to fill against a point");
    }

    private static MeasuredBox Sized(int width, int height) =>
        new() { Width = Sizing.Fixed(width), Height = Sizing.Fixed(height) };

    private static Element Anchored(Element child, AnchoredLayout? layout = null) =>
        new() { Layout = layout ?? AnchoredLayout.TopLeft, Children = { child } };
}
