namespace Pixely.Tests;

public class RectangleTests
{
    private static readonly Rectangle Target = new(0, 0, 800, 600);

    [Test]
    public void Intersect_WithRectangleInsideTarget_ReturnsItUnchanged()
    {
        Assert.That(new Rectangle(10, 20, 100, 50).Intersect(Target), Is.EqualTo(new Rectangle(10, 20, 100, 50)));
    }

    [Test]
    public void Intersect_WithRectangleCoveringWholeTarget_ReturnsTarget()
    {
        Assert.That(new Rectangle(0, 0, 800, 600).Intersect(Target), Is.EqualTo(Target));
    }

    [Test]
    public void Intersect_WithNegativeOrigin_ClipsToTarget()
    {
        Assert.That(new Rectangle(-10, -20, 100, 100).Intersect(Target), Is.EqualTo(new Rectangle(0, 0, 90, 80)));
    }

    [Test]
    public void Intersect_ExceedingTargetWidthByOne_ClipsToTarget()
    {
        Assert.That(new Rectangle(700, 0, 101, 10).Intersect(Target), Is.EqualTo(new Rectangle(700, 0, 100, 10)));
    }

    [Test]
    public void Intersect_ExceedingTargetHeightByOne_ClipsToTarget()
    {
        Assert.That(new Rectangle(0, 500, 10, 101).Intersect(Target), Is.EqualTo(new Rectangle(0, 500, 10, 100)));
    }

    [Test]
    public void Intersect_WithOverflowingExtent_ClipsToTargetWithoutWrapping()
    {
        Assert.That(new Rectangle(1, 0, int.MaxValue, 10).Intersect(Target), Is.EqualTo(new Rectangle(1, 0, 799, 10)));
    }

    [Test]
    public void Intersect_WithRectangleFullyOutsideTarget_ReturnsEmpty()
    {
        Assert.That(new Rectangle(900, 0, 10, 10).Intersect(Target), Is.EqualTo(default(Rectangle)));
        Assert.That(new Rectangle(-100, 0, 50, 10).Intersect(Target), Is.EqualTo(default(Rectangle)));
    }

    [Test]
    public void Intersect_WithTouchingEdges_ReturnsEmpty()
    {
        Assert.That(new Rectangle(800, 0, 10, 10).Intersect(Target), Is.EqualTo(default(Rectangle)));
    }

    [Test]
    public void Intersect_IsCommutative()
    {
        Rectangle first = new(10, 20, 100, 50);
        Rectangle second = new(50, 0, 100, 100);

        Assert.That(first.Intersect(second), Is.EqualTo(second.Intersect(first)));
    }

    [Test]
    public void Contains_TakesTheTopAndLeftEdgesButNotTheBottomAndRight()
    {
        Rectangle rectangle = new(10, 20, 30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(rectangle.Contains(new Vector2Int(10, 20)), Is.True);
            Assert.That(rectangle.Contains(new Vector2Int(39, 59)), Is.True);
            Assert.That(rectangle.Contains(new Vector2Int(40, 30)), Is.False);
            Assert.That(rectangle.Contains(new Vector2Int(20, 60)), Is.False);
            Assert.That(rectangle.Contains(new Vector2Int(9, 30)), Is.False);
            Assert.That(rectangle.Contains(new Vector2Int(20, 19)), Is.False);
        });
    }

    [Test]
    public void Contains_LeavesNoPixelToTwoAdjacentRectangles()
    {
        Rectangle left = new(0, 0, 20, 10);
        Rectangle right = new(20, 0, 20, 10);
        Vector2Int shared = new(20, 5);

        Assert.Multiple(() =>
        {
            Assert.That(left.Contains(shared), Is.False);
            Assert.That(right.Contains(shared), Is.True);
            Assert.That(left.Intersects(shared) && right.Intersects(shared), Is.True, "Intersects is inclusive, which is why hit testing does not use it");
        });
    }

    [Test]
    public void Contains_WithAnExtentPastIntMaxValue_DoesNotWrap()
    {
        Rectangle rectangle = new(int.MaxValue - 10, 0, 100, 100);

        Assert.That(rectangle.Contains(new Vector2Int(int.MaxValue - 5, 5)), Is.True);
    }

    [Test]
    public void Contains_WithAnEmptyRectangle_IsAlwaysFalse()
    {
        Assert.That(new Rectangle(10, 10, 0, 0).Contains(new Vector2Int(10, 10)), Is.False);
    }
}
