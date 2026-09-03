using Pixely.PathFinding.Grids;

namespace Pixely.PathFinding.Tests.Grids;

public sealed class GridStepsTests
{
    [TestCase(GridConnectivity.FourWay, 4)]
    [TestCase(GridConnectivity.EightWay, 8)]
    [TestCase(GridConnectivity.EightWayNoCornerCutting, 8)]
    public void Enumerate_ProducesTheFullFanInTheGridInterior(GridConnectivity connectivity, int expectedDegree)
    {
        GridSteps<NoGridOverlay> steps = CreateSteps(5, 5, connectivity, []);
        GridStep[] buffer = new GridStep[steps.MaximumDegree];

        int count = steps.Enumerate(steps.Geometry.GetIndex(2, 2), 1, buffer);

        Assert.Multiple(() =>
        {
            Assert.That(steps.MaximumDegree, Is.EqualTo(expectedDegree));
            Assert.That(count, Is.EqualTo(expectedDegree));
            Assert.That(Positions(steps, buffer, count), Is.EquivalentTo(ExpectedFan(2, 2, connectivity)));
            Assert.That(buffer.Take(count).Count(step => step.Distance == GridStep.CardinalDistance), Is.EqualTo(4));
        });
    }

    [TestCase(GridConnectivity.FourWay, 2)]
    [TestCase(GridConnectivity.EightWay, 3)]
    [TestCase(GridConnectivity.EightWayNoCornerCutting, 3)]
    public void Enumerate_ClipsTheFanAtTheGridEdge(GridConnectivity connectivity, int expectedCount)
    {
        GridSteps<NoGridOverlay> steps = CreateSteps(5, 5, connectivity, []);
        GridStep[] buffer = new GridStep[steps.MaximumDegree];

        int count = steps.Enumerate(0, 1, buffer);

        Assert.That(count, Is.EqualTo(expectedCount));
    }

    [Test]
    public void Enumerate_KeepsDiagonalsWhoseCardinalsAreBlockedOnlyWhenCornersMayBeCut()
    {
        GridSteps<NoGridOverlay> cutting = CreateSteps(3, 3, GridConnectivity.EightWay, [(2, 1)]);
        GridSteps<NoGridOverlay> notCutting = CreateSteps(3, 3, GridConnectivity.EightWayNoCornerCutting, [(2, 1)]);
        GridStep[] buffer = new GridStep[8];

        int cuttingCount = cutting.Enumerate(cutting.Geometry.GetIndex(1, 1), 1, buffer);
        List<(int X, int Y)> cuttingPositions = Positions(cutting, buffer, cuttingCount);
        int notCuttingCount = notCutting.Enumerate(notCutting.Geometry.GetIndex(1, 1), 1, buffer);
        List<(int X, int Y)> notCuttingPositions = Positions(notCutting, buffer, notCuttingCount);

        Assert.Multiple(() =>
        {
            Assert.That(cuttingPositions, Does.Contain((2, 0)).And.Contain((2, 2)));
            Assert.That(notCuttingPositions, Does.Not.Contain((2, 0)).And.Not.Contain((2, 2)));
            Assert.That(notCuttingPositions, Does.Contain((0, 0)).And.Contain((0, 2)));
        });
    }

    [Test]
    public void Enumerate_AppliesTheCornerRuleToAnchorsBlockedOnlyByTheOverlay()
    {
        GridGeometry geometry = new GridGeometry(3, 3);
        ClearanceGrid clearance = new ClearanceGrid(geometry);
        clearance.Rebuild(new bool[geometry.NodeCount]);
        bool[] overlaid = new bool[geometry.NodeCount];
        overlaid[geometry.GetIndex(2, 1)] = true;
        GridSteps<MaskOverlay> steps = new GridSteps<MaskOverlay>(clearance, GridConnectivity.EightWayNoCornerCutting, new MaskOverlay(overlaid));
        GridStep[] buffer = new GridStep[steps.MaximumDegree];

        int count = steps.Enumerate(geometry.GetIndex(1, 1), 1, buffer);
        List<(int X, int Y)> positions = [.. buffer.Take(count).Select(step => geometry.GetPosition(step.Index))];

        Assert.Multiple(() =>
        {
            Assert.That(positions, Does.Not.Contain((2, 1)));
            Assert.That(positions, Does.Not.Contain((2, 0)).And.Not.Contain((2, 2)));
            Assert.That(positions, Does.Contain((0, 0)).And.Contain((1, 0)));
        });
    }

    [Test]
    public void Enumerate_AppliesTheCornerRuleToIntermediatesThatOnlyALargerAgentOverflows()
    {
        // The destination anchor (2, 2) fits a size-two agent, but the intermediate anchor (2, 1) covers the blocked tile.
        GridSteps<NoGridOverlay> cutting = CreateSteps(5, 5, GridConnectivity.EightWay, [(3, 1)]);
        GridSteps<NoGridOverlay> notCutting = CreateSteps(5, 5, GridConnectivity.EightWayNoCornerCutting, [(3, 1)]);
        GridStep[] buffer = new GridStep[8];

        int cuttingCount = cutting.Enumerate(cutting.Geometry.GetIndex(1, 1), 2, buffer);
        List<(int X, int Y)> cuttingPositions = Positions(cutting, buffer, cuttingCount);
        int notCuttingCount = notCutting.Enumerate(notCutting.Geometry.GetIndex(1, 1), 2, buffer);
        List<(int X, int Y)> notCuttingPositions = Positions(notCutting, buffer, notCuttingCount);

        Assert.Multiple(() =>
        {
            Assert.That(cuttingPositions, Does.Contain((2, 2)));
            Assert.That(cuttingPositions, Does.Not.Contain((2, 1)));
            Assert.That(notCuttingPositions, Does.Not.Contain((2, 2)));
            Assert.That(notCuttingPositions, Does.Contain((1, 2)).And.Contain((0, 2)));
        });
    }

    [Test]
    public void Enumerate_AppliesTheCornerRuleToLargerAgentsBlockedOnlyByTheOverlay()
    {
        GridGeometry geometry = new GridGeometry(5, 5);
        ClearanceGrid clearance = new ClearanceGrid(geometry);
        clearance.Rebuild(new bool[geometry.NodeCount]);
        bool[] overlaid = new bool[geometry.NodeCount];
        overlaid[geometry.GetIndex(2, 1)] = true;
        GridSteps<MaskOverlay> steps = new GridSteps<MaskOverlay>(clearance, GridConnectivity.EightWayNoCornerCutting, new MaskOverlay(overlaid));
        GridStep[] buffer = new GridStep[steps.MaximumDegree];

        int count = steps.Enumerate(geometry.GetIndex(1, 1), 2, buffer);
        List<(int X, int Y)> positions = [.. buffer.Take(count).Select(step => geometry.GetPosition(step.Index))];

        Assert.Multiple(() =>
        {
            Assert.That(positions, Does.Not.Contain((2, 1)));
            Assert.That(positions, Does.Not.Contain((2, 2)).And.Not.Contain((2, 0)));
            Assert.That(positions, Does.Contain((1, 2)).And.Contain((0, 0)));
        });
    }

    [Test]
    public void Enumerate_ServesAgentsOfDifferentSizesFromOneGrid()
    {
        GridSteps<NoGridOverlay> steps = CreateSteps(3, 3, GridConnectivity.EightWayNoCornerCutting, [(2, 2)]);
        GridStep[] buffer = new GridStep[steps.MaximumDegree];

        int smallCount = steps.Enumerate(0, 1, buffer);
        List<(int X, int Y)> smallPositions = Positions(steps, buffer, smallCount);
        int largeCount = steps.Enumerate(0, 2, buffer);
        List<(int X, int Y)> largePositions = Positions(steps, buffer, largeCount);

        Assert.Multiple(() =>
        {
            Assert.That(smallPositions, Is.EquivalentTo(new[] { (1, 0), (0, 1), (1, 1) }));
            Assert.That(largePositions, Is.EquivalentTo(new[] { (1, 0), (0, 1) }));
        });
    }

    [Test]
    public void Enumerate_RejectsUnsupportedAgentSizesAndShortBuffers()
    {
        GridSteps<NoGridOverlay> steps = CreateSteps(3, 3, GridConnectivity.EightWay, []);
        GridStep[] buffer = new GridStep[steps.MaximumDegree];

        Assert.Multiple(() =>
        {
            Assert.That(() => steps.Enumerate(0, 0, buffer), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("agentSize"));
            Assert.That(() => steps.Enumerate(0, ClearanceGrid.MaximumAgentSize + 1, buffer), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("agentSize"));
            Assert.That(() => steps.Enumerate(0, 1, new GridStep[7]), Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("steps"));
        });
    }

    [Test]
    public void Constructor_RejectsUndefinedConnectivity()
    {
        ClearanceGrid clearance = new ClearanceGrid(new GridGeometry(3, 3));

        Assert.Multiple(() =>
        {
            Assert.That(() => new GridSteps<NoGridOverlay>(clearance, (GridConnectivity)7, default), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new GridSteps<NoGridOverlay>(null!, GridConnectivity.EightWay, default), Throws.ArgumentNullException);
        });
    }

    internal static GridSteps<NoGridOverlay> CreateSteps(int width, int height, GridConnectivity connectivity, (int X, int Y)[] blockedPositions)
    {
        GridGeometry geometry = new GridGeometry(width, height);
        ClearanceGrid clearance = new ClearanceGrid(geometry);
        bool[] blocked = new bool[geometry.NodeCount];
        foreach ((int x, int y) in blockedPositions)
        {
            blocked[geometry.GetIndex(x, y)] = true;
        }

        clearance.Rebuild(blocked);
        return new GridSteps<NoGridOverlay>(clearance, connectivity, default);
    }

    private static List<(int X, int Y)> Positions<TOverlay>(GridSteps<TOverlay> steps, GridStep[] buffer, int count) where TOverlay : struct, IGridOverlay
    {
        GridGeometry geometry = steps.Geometry;
        return [.. buffer.Take(count).Select(step => geometry.GetPosition(step.Index))];
    }

    private static List<(int X, int Y)> ExpectedFan(int x, int y, GridConnectivity connectivity)
    {
        List<(int X, int Y)> fan = [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)];
        if (connectivity != GridConnectivity.FourWay)
        {
            fan.AddRange([(x + 1, y + 1), (x + 1, y - 1), (x - 1, y + 1), (x - 1, y - 1)]);
        }

        return fan;
    }

    private readonly struct MaskOverlay : IGridOverlay
    {
        private readonly bool[] _blocked;

        internal MaskOverlay(bool[] blocked)
        {
            _blocked = blocked;
        }

        public bool IsBlocked(int index)
        {
            return _blocked[index];
        }
    }
}
