using Pixely.PathFinding.Grids;

namespace Pixely.PathFinding.Tests.Grids;

public sealed class ClearanceGridTests
{
    [Test]
    public void Rebuild_MeasuresTheSquareTowardIncreasingCoordinates()
    {
        GridGeometry geometry = new GridGeometry(3, 3);
        ClearanceGrid grid = new ClearanceGrid(geometry);
        bool[] blocked = new bool[geometry.NodeCount];
        blocked[geometry.GetIndex(2, 2)] = true;

        grid.Rebuild(blocked);

        Assert.That(grid.Clearances.ToArray(), Is.EqualTo(new byte[] { 2, 2, 1, 2, 1, 1, 1, 1, 0 }));
    }

    [Test]
    public void Fits_AgreesWithAnExhaustiveScanOverRandomizedLayouts()
    {
        Random random = new Random(463);
        for (int trial = 0; trial < 40; trial++)
        {
            int width = random.Next(1, 13);
            int height = random.Next(1, 13);
            GridGeometry geometry = new GridGeometry(width, height);
            bool[] blocked = new bool[geometry.NodeCount];
            for (int index = 0; index < blocked.Length; index++)
            {
                blocked[index] = random.Next(100) < 25;
            }

            ClearanceGrid grid = new ClearanceGrid(geometry);
            grid.Rebuild(blocked);

            int largestSize = Math.Min(ClearanceGrid.MaximumAgentSize, Math.Min(width, height));
            for (int index = 0; index < geometry.NodeCount; index++)
            {
                for (int agentSize = 1; agentSize <= largestSize; agentSize++)
                {
                    Assert.That(grid.Fits(index, agentSize), Is.EqualTo(ScanFits(geometry, blocked, index, agentSize)), $"{width}x{height} trial {trial}, index {index}, size {agentSize}");
                }
            }
        }
    }

    [Test]
    public void Fits_RejectsAgentsLargerThanTheGrid()
    {
        GridGeometry geometry = new GridGeometry(4, 3);
        ClearanceGrid grid = new ClearanceGrid(geometry);
        grid.Rebuild(new bool[geometry.NodeCount]);

        Assert.Multiple(() =>
        {
            Assert.That(grid.Fits(0, 3), Is.True);
            Assert.That(grid.Fits(0, 4), Is.False);
            Assert.That(grid.Fits(0, ClearanceGrid.MaximumAgentSize), Is.False);
        });
    }

    [Test]
    public void Fits_SaturatesAtTheMaximumAgentSize()
    {
        GridGeometry geometry = new GridGeometry(300, 300);
        ClearanceGrid grid = new ClearanceGrid(geometry);
        grid.Rebuild(new bool[geometry.NodeCount]);

        Assert.Multiple(() =>
        {
            Assert.That(grid.GetClearance(0), Is.EqualTo(ClearanceGrid.MaximumAgentSize));
            Assert.That(grid.Fits(0, ClearanceGrid.MaximumAgentSize), Is.True);
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(ClearanceGrid.MaximumAgentSize + 1)]
    public void Fits_RejectsUnsupportedAgentSizes(int agentSize)
    {
        ClearanceGrid grid = new ClearanceGrid(new GridGeometry(4, 4));

        Assert.That(() => grid.Fits(0, agentSize), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("agentSize"));
    }

    [Test]
    public void Rebuild_RejectsShortBuffersAndReusesItsOwn()
    {
        GridGeometry geometry = new GridGeometry(4, 4);
        ClearanceGrid grid = new ClearanceGrid(geometry);
        bool[] blocked = new bool[geometry.NodeCount];
        grid.Rebuild(blocked);
        blocked[5] = true;

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        grid.Rebuild(blocked);
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Multiple(() =>
        {
            Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
            Assert.That(grid.GetClearance(5), Is.Zero);
            Assert.That(() => grid.Rebuild(new bool[geometry.NodeCount - 1]), Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("blocked"));
        });
    }

    private static bool ScanFits(GridGeometry geometry, bool[] blocked, int index, int agentSize)
    {
        (int originX, int originY) = geometry.GetPosition(index);
        for (int y = originY; y < originY + agentSize; y++)
        {
            for (int x = originX; x < originX + agentSize; x++)
            {
                if (!geometry.TryGetIndex(x, y, out int scanned) || blocked[scanned])
                {
                    return false;
                }
            }
        }

        return true;
    }
}
