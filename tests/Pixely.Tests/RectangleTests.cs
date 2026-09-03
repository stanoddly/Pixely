namespace Pixely.Tests;

public sealed class RectangleTests
{
    [Test]
    public void Intersects_CoversExactlyThePaintedPixels()
    {
        Rectangle rectangle = new Rectangle(10, 20, 30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(rectangle.Intersects(new Vector2Int(10, 20)), Is.True);
            Assert.That(rectangle.Intersects(new Vector2Int(39, 59)), Is.True);
            Assert.That(rectangle.Intersects(new Vector2Int(9, 20)), Is.False);
            Assert.That(rectangle.Intersects(new Vector2Int(10, 19)), Is.False);
            Assert.That(rectangle.Intersects(new Vector2Int(40, 59)), Is.False);
            Assert.That(rectangle.Intersects(new Vector2Int(39, 60)), Is.False);
        });
    }

    [Test]
    public void Intersects_AdjacentRectanglesDoNotShareAPixel()
    {
        Rectangle left = new Rectangle(0, 0, 200, 400);
        Rectangle right = new Rectangle(200, 0, 10, 400);
        Vector2Int seam = new Vector2Int(200, 10);

        Assert.Multiple(() =>
        {
            Assert.That(left.Intersects(seam), Is.False);
            Assert.That(right.Intersects(seam), Is.True);
        });
    }

    [Test]
    public void Intersects_EmptyRectangleContainsNothing()
    {
        Rectangle rectangle = new Rectangle(5, 5, 0, 0);

        Assert.That(rectangle.Intersects(new Vector2Int(5, 5)), Is.False);
    }

    [Test]
    public void ShortRectangle_Intersects_CoversExactlyThePaintedPixels()
    {
        ShortRectangle rectangle = new ShortRectangle(10, 20, 30, 40);

        Assert.Multiple(() =>
        {
            Assert.That(rectangle.Intersects(new ShortVector2(39, 59)), Is.True);
            Assert.That(rectangle.Intersects(new ShortVector2(40, 59)), Is.False);
            Assert.That(rectangle.Intersects(new ShortVector2(39, 60)), Is.False);
        });
    }

    [Test]
    public void Intersect_ReturnsOverlap()
    {
        Rectangle result = new Rectangle(0, 0, 100, 100).Intersect(new Rectangle(50, 60, 100, 100));

        Assert.That(result, Is.EqualTo(new Rectangle(50, 60, 50, 40)));
    }

    [Test]
    public void Intersect_DisjointRectanglesProduceEmptyResult()
    {
        Rectangle result = new Rectangle(0, 0, 10, 10).Intersect(new Rectangle(20, 20, 10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.True);
            Assert.That(result.Width, Is.Zero);
            Assert.That(result.Height, Is.Zero);
        });
    }

    [Test]
    public void Intersect_TouchingEdgesProduceEmptyResult()
    {
        Rectangle result = new Rectangle(0, 0, 10, 10).Intersect(new Rectangle(10, 0, 10, 10));

        Assert.That(result.IsEmpty, Is.True);
    }
}
