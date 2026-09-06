namespace Pixely.Ui.Tests;

/// <summary>
/// Measure runs exactly once per element per constraint. These tests pin down what that costs,
/// so the behaviour is a decision on record rather than something discovered later.
/// </summary>
public class SinglePassTradeoffTests
{
    [Test]
    public void FitContainer_WithMinimumConstraint_LeavesTheExtraSpaceUnused()
    {
        MeasuredBox child = new(10, 20) { Height = Sizing.Grow() };
        Column root = new() { Children = { child } };

        // The column fits its content at 20 tall, then the minimum raises it to 100. The child
        // was measured on an indefinite axis, so its Grow degraded to Fit and it stays at 20.
        // Filling it would need a second grow pass, which is the thing this design refuses.
        Vector2Int size = Layout.Measure(root, new Constraints(0, 100, 200, Constraints.Unbounded));

        Assert.Multiple(() =>
        {
            Assert.That(size.Y, Is.EqualTo(100));
            Assert.That(child.DesiredSize.Y, Is.EqualTo(20));
            Assert.That(child.MeasureCount, Is.EqualTo(1), "measure runs once; there is no second pass");
        });
    }

    [Test]
    public void FitContainer_WithIntrinsicContentLargerThanChildren_LeavesTheExtraSpaceUnused()
    {
        MeasuredBox child = new(10, 10) { Height = Sizing.Grow() };
        MeasuredBox root = new(0, 80) { Children = { child } };

        Vector2Int size = Layout.MeasureUnbounded(root);

        Assert.Multiple(() =>
        {
            Assert.That(size.Y, Is.EqualTo(80), "the element's own content sets its height");
            Assert.That(child.DesiredSize.Y, Is.EqualTo(10), "the child does not grow into it");
        });
    }

    [Test]
    public void DefiniteContainer_IsTheWayToMakeGrowFill()
    {
        MeasuredBox child = new(10, 20) { Height = Sizing.Grow() };
        Column container = new() { Height = Sizing.Fixed(100), Children = { child } };
        Column root = new() { Children = { container } };

        Layout.Run(root, 200, 200);

        Assert.That(child.Bounds.Height, Is.EqualTo(100),
            "giving the container a definite height is what makes Grow meaningful");
    }

    [Test]
    public void GrowChild_IsMeasuredOnceWithItsFinalExtent()
    {
        MeasuredBox growing = new(10, 10) { Height = Sizing.Grow() };
        Column root = new() { Children = { growing } };

        Layout.Run(root, 100, 200);

        Assert.Multiple(() =>
        {
            Assert.That(growing.MeasureCount, Is.EqualTo(1));
            Assert.That(growing.LastConstraints.IsHeightDefinite, Is.True);
            Assert.That(growing.Bounds.Height, Is.EqualTo(200));
        });
    }

    [Test]
    public void Arrange_NeverTriggersAMeasure()
    {
        MeasuredBox child = new(10, 10) { Height = Sizing.Grow(), HorizontalAlignment = Alignment.Stretch };
        Column root = new() { Children = { child } };

        root.Measure(Constraints.Tight(new Vector2Int(100, 100)));
        int afterMeasure = child.MeasureCount;
        root.Arrange(new Rectangle(0, 0, 100, 100), new Rectangle(0, 0, 100, 100));

        Assert.That(child.MeasureCount, Is.EqualTo(afterMeasure));
    }

    [Test]
    public void SingleChildElement_RejectsASecondChild()
    {
        SingleChildElement parent = new();
        parent.Children.Add(new MeasuredBox(10, 10));

        Assert.Throws<InvalidOperationException>(() => parent.Children.Add(new MeasuredBox(10, 10)));
    }

    private sealed class SingleChildElement : Element
    {
        protected override int MaxChildCount => 1;
    }
}
