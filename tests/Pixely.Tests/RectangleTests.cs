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
}
