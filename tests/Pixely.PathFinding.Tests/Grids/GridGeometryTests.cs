using Pixely.PathFinding.Grids;

namespace Pixely.PathFinding.Tests.Grids;

public sealed class GridGeometryTests
{
    [Test]
    public void GetIndex_MapsRowMajorAndRoundTrips()
    {
        GridGeometry geometry = new GridGeometry(4, 3);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.NodeCount, Is.EqualTo(12));
            Assert.That(geometry.GetIndex(0, 0), Is.Zero);
            Assert.That(geometry.GetIndex(3, 0), Is.EqualTo(3));
            Assert.That(geometry.GetIndex(0, 1), Is.EqualTo(4));
            Assert.That(geometry.GetIndex(3, 2), Is.EqualTo(11));
        });

        for (int index = 0; index < geometry.NodeCount; index++)
        {
            (int x, int y) = geometry.GetPosition(index);
            Assert.That(geometry.GetIndex(x, y), Is.EqualTo(index));
        }
    }

    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(4, 0)]
    [TestCase(0, 3)]
    public void TryGetIndex_RejectsPositionsOutsideTheGrid(int x, int y)
    {
        GridGeometry geometry = new GridGeometry(4, 3);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.Contains(x, y), Is.False);
            Assert.That(geometry.TryGetIndex(x, y, out int index), Is.False);
            Assert.That(index, Is.EqualTo(-1));
            Assert.That(() => geometry.GetIndex(x, y), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void GetPosition_RejectsIndexesOutsideTheGrid()
    {
        GridGeometry geometry = new GridGeometry(4, 3);

        Assert.Multiple(() =>
        {
            Assert.That(() => geometry.GetPosition(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => geometry.GetPosition(12), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 1)]
    public void Constructor_RejectsNonPositiveExtents(int width, int height)
    {
        Assert.That(() => new GridGeometry(width, height), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Constructor_RejectsNodeCountsBeyondTheIntegerRange()
    {
        Assert.That(() => new GridGeometry(int.MaxValue, 2), Throws.TypeOf<OverflowException>());
    }
}
