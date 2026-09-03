using Pixely.PathFinding.Grids;

namespace Pixely.PathFinding.Tests.Grids;

public sealed class UniformGridGraphTests
{
    [Test]
    public void FindPath_WalksAroundAWallAndAgreesBetweenDijkstraAndAStar()
    {
        UniformGridGraph<int, float, NoGridOverlay> graph = CreateGraph(5, 5, GridConnectivity.FourWay, 1, [(2, 0), (2, 1), (2, 2), (2, 3)]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        GridGeometry geometry = new GridGeometry(5, 5);
        List<int> dijkstraPath = new List<int>();
        List<int> aStarPath = new List<int>();

        PathResult dijkstraResult = search.FindPath(graph, geometry.GetIndex(0, 0), geometry.GetIndex(4, 0), dijkstraPath);
        PathResult aStarResult = search.FindPath(graph, geometry.GetIndex(0, 0), geometry.GetIndex(4, 0), aStarPath, graph.GetHeuristic());

        Assert.Multiple(() =>
        {
            Assert.That(dijkstraResult, Is.EqualTo(PathResult.Found));
            Assert.That(aStarResult, Is.EqualTo(PathResult.Found));
            Assert.That(dijkstraPath, Has.Count.EqualTo(12));
            Assert.That(aStarPath, Has.Count.EqualTo(dijkstraPath.Count));
            Assert.That(dijkstraPath[^1], Is.EqualTo(geometry.GetIndex(4, 0)));
            Assert.That(dijkstraPath.Select(index => geometry.GetPosition(index)), Does.Not.Contain((2, 0)));
        });
    }

    [Test]
    public void FindPath_TakesTheDiagonalWhenCornersMayBeCut()
    {
        GridGeometry geometry = new GridGeometry(4, 4);
        UniformGridGraph<int, float, NoGridOverlay> graph = CreateGraph(4, 4, GridConnectivity.EightWay, 1, []);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int>();

        PathResult result = search.FindPath(graph, 0, geometry.GetIndex(3, 3), path, graph.GetHeuristic());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new[] { geometry.GetIndex(1, 1), geometry.GetIndex(2, 2), geometry.GetIndex(3, 3) }));
        });
    }

    [Test]
    public void FindPath_RoutesAgentsOfDifferentSizesDifferentlyOnOneGrid()
    {
        // A wall across the grid with a gap one tile wide, which only a size-one agent can pass.
        (int X, int Y)[] blocked = [(0, 2), (1, 2), (3, 2), (4, 2)];
        GridGeometry geometry = new GridGeometry(5, 5);
        UniformGridGraph<int, float, NoGridOverlay> small = CreateGraph(5, 5, GridConnectivity.FourWay, 1, blocked);
        UniformGridGraph<int, float, NoGridOverlay> large = CreateGraph(5, 5, GridConnectivity.FourWay, 2, blocked);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> smallPath = new List<int>();
        List<int> largePath = new List<int>();
        int start = geometry.GetIndex(2, 0);
        int destination = geometry.GetIndex(2, 3);

        PathResult smallResult = search.FindPath(small, start, destination, smallPath, small.GetHeuristic());
        PathResult largeResult = search.FindPath(large, start, destination, largePath, large.GetHeuristic());

        Assert.Multiple(() =>
        {
            Assert.That(smallResult, Is.EqualTo(PathResult.Found));
            Assert.That(smallPath, Is.EqualTo(new[] { geometry.GetIndex(2, 1), geometry.GetIndex(2, 2), destination }));
            Assert.That(largeResult, Is.EqualTo(PathResult.NotFound));
            Assert.That(large.GetEdges(start, new PathEdge<int, float>[large.MaximumDegree]), Is.Positive);
        });
    }

    [Test]
    public void FindPath_ReportsNoPathWhenTheDestinationIsEnclosed()
    {
        GridGeometry geometry = new GridGeometry(4, 4);
        UniformGridGraph<int, float, NoGridOverlay> graph = CreateGraph(4, 4, GridConnectivity.EightWayNoCornerCutting, 1, [(2, 3), (3, 2), (2, 2)]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int>();

        PathResult result = search.FindPath(graph, 0, geometry.GetIndex(3, 3), path, graph.GetHeuristic());

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.NotFound));
            Assert.That(path, Is.Empty);
        });
    }

    [Test]
    public void FindPath_DoesNotAllocateAfterWarmUp()
    {
        GridGeometry geometry = new GridGeometry(32, 32);
        UniformGridGraph<int, float, NoGridOverlay> graph = CreateGraph(32, 32, GridConnectivity.EightWayNoCornerCutting, 2, [(8, 8), (8, 9), (9, 8), (20, 20)]);
        GridHeuristic<int, float> heuristic = graph.GetHeuristic();
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int>(64);
        int destination = geometry.GetIndex(30, 30);
        for (int warmUp = 0; warmUp < 4; warmUp++)
        {
            search.FindPath(graph, 0, destination, path, heuristic);
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 16; iteration++)
        {
            search.FindPath(graph, 0, destination, path, heuristic);
        }

        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Multiple(() =>
        {
            Assert.That(allocatedAfter - allocatedBefore, Is.Zero);
            Assert.That(path, Is.Not.Empty);
        });
    }

    [Test]
    public void Constructor_RejectsUnsupportedAgentSizesAndCosts()
    {
        GridSteps<NoGridOverlay> steps = GridStepsTests.CreateSteps(4, 4, GridConnectivity.EightWay, []);

        Assert.Multiple(() =>
        {
            Assert.That(() => new UniformGridGraph<int, float, NoGridOverlay>(steps, 0, 1f, 1f), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("agentSize"));
            Assert.That(() => new UniformGridGraph<int, float, NoGridOverlay>(steps, ClearanceGrid.MaximumAgentSize + 1, 1f, 1f), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("agentSize"));
            Assert.That(() => new UniformGridGraph<int, float, NoGridOverlay>(steps, 1, -1f, 1f), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("cardinalCost"));
            Assert.That(() => new UniformGridGraph<int, float, NoGridOverlay>(steps, 1, 1f, float.NaN), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("diagonalCost"));
        });
    }

    [Test]
    public void Constructor_AcceptsASingleCostForFourWayConnectivityOnly()
    {
        GridSteps<NoGridOverlay> fourWay = GridStepsTests.CreateSteps(4, 4, GridConnectivity.FourWay, []);
        GridSteps<NoGridOverlay> eightWay = GridStepsTests.CreateSteps(4, 4, GridConnectivity.EightWay, []);
        UniformGridGraph<int, float, NoGridOverlay> graph = new UniformGridGraph<int, float, NoGridOverlay>(fourWay, 1, 2f);
        GridGeometry geometry = fourWay.Geometry;

        Assert.Multiple(() =>
        {
            Assert.That(graph.GetHeuristic().EstimateCost(0, geometry.GetIndex(3, 2)), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(() => new UniformGridGraph<int, float, NoGridOverlay>(eightWay, 1, 2f), Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("steps"));
        });
    }

    private static UniformGridGraph<int, float, NoGridOverlay> CreateGraph(int width, int height, GridConnectivity connectivity, int agentSize, (int X, int Y)[] blockedPositions)
    {
        GridSteps<NoGridOverlay> steps = GridStepsTests.CreateSteps(width, height, connectivity, blockedPositions);
        return new UniformGridGraph<int, float, NoGridOverlay>(steps, agentSize, 1f, MathF.Sqrt(2f));
    }
}
