using Pixely.PathFinding;

namespace Pixely.PathFinding.Tests;

public class PathFinderTests
{
    [Test]
    public void FindPath_ReturnsLowestCostPath()
    {
        TestMap map = new TestMap(new Dictionary<int, (int Position, float Cost)[]>
        {
            [0] = [(1, 10), (2, 1)],
            [1] = [(3, 1)],
            [2] = [(1, 1)],
            [3] = []
        });
        PathFinder<int> pathFinder = new PathFinder<int>(map, new ZeroHeuristic());
        List<(int Position, float Cost)> path = new List<(int Position, float Cost)>();

        PathResult result = pathFinder.FindPath(0, 3, path);

        Assert.That(result, Is.EqualTo(PathResult.Found));
        Assert.That(path, Is.EqualTo(new[] { (2, 1f), (1, 2f), (3, 3f) }));
    }

    [Test]
    public void ExpandArea_ReturnsReachablePointsAndCosts()
    {
        TestMap map = new TestMap(new Dictionary<int, (int Position, float Cost)[]>
        {
            [0] = [(1, 2), (2, 5)],
            [1] = [(0, 2), (2, 2)],
            [2] = [(0, 5), (1, 2), (3, 2)],
            [3] = [(2, 2)]
        });
        PathFinder<int> pathFinder = new PathFinder<int>(map, new ZeroHeuristic());

        AreaResult<int> result = pathFinder.ExpandArea(0, 4);

        Assert.That(result.Costs, Has.Count.EqualTo(3));
        Assert.That(result.Costs[0], Is.EqualTo(0));
        Assert.That(result.Costs[1], Is.EqualTo(2));
        Assert.That(result.Costs[2], Is.EqualTo(4));
        Assert.That(result.Costs, Does.Not.ContainKey(3));
    }

    [Test]
    public void SearchesReuseNeighborCollection()
    {
        TrackingMap pathMap = new TrackingMap(8);
        PathFinder<int> pathFinder = new PathFinder<int>(pathMap, new ZeroHeuristic());
        List<(int Position, float Cost)> path = new List<(int Position, float Cost)>();

        pathFinder.FindPath(0, 7, path);

        Assert.That(pathMap.UsedOneNeighborCollection, Is.True);

        TrackingMap areaMap = new TrackingMap(8);
        PathFinder<int> areaPathFinder = new PathFinder<int>(areaMap, new ZeroHeuristic());

        areaPathFinder.ExpandArea(0, 7);

        Assert.That(areaMap.UsedOneNeighborCollection, Is.True);
    }

    [Test]
    public void ExpandArea_IncludesBoundaryEdgeFromStart()
    {
        TestMap map = new TestMap(new Dictionary<int, (int Position, float Cost)[]>
        {
            [0] = [(1, 2f)],
            [1] = [(0, 2f)]
        });
        PathFinder<int> pathFinder = new PathFinder<int>(map, new ZeroHeuristic());

        AreaResult<int> result = pathFinder.ExpandArea(0, 1f);

        Assert.That(result.Edges, Does.Contain(new AreaEdge<int>(0, 1)));
    }

    [Test]
    public void FindPath_ClearsExistingResult()
    {
        TestMap map = new TestMap(new Dictionary<int, (int Position, float Cost)[]>
        {
            [0] = [(1, 1f)],
            [1] = []
        });
        PathFinder<int> pathFinder = new PathFinder<int>(map, new ZeroHeuristic());
        List<(int Position, float Cost)> path = new List<(int Position, float Cost)> { (99, 99f) };

        PathResult result = pathFinder.FindPath(0, 1, path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new[] { (1, 1f) }));
        });
    }

    [Test]
    public void FindPath_EnforcesExpansionLimit()
    {
        TestMap map = new TestMap(new Dictionary<int, (int Position, float Cost)[]>
        {
            [0] = [(1, 1f)],
            [1] = [(2, 1f)],
            [2] = []
        });
        PathFinder<int> pathFinder = new PathFinder<int>(map, new ZeroHeuristic(), 1);
        List<(int Position, float Cost)> path = new List<(int Position, float Cost)>();

        PathResult result = pathFinder.FindPath(0, 2, path);

        Assert.That(result, Is.EqualTo(PathResult.ExpansionLimitExceeded));
    }

    private sealed class TestMap : IPathFinderMap<int>
    {
        private readonly IReadOnlyDictionary<int, (int Position, float Cost)[]> _neighbors;

        public TestMap(IReadOnlyDictionary<int, (int Position, float Cost)[]> neighbors)
        {
            _neighbors = neighbors;
        }

        public void ExpandPosition(int origin, ICollection<(int Position, float Cost)> neighbors)
        {
            foreach ((int position, float cost) in _neighbors[origin])
            {
                neighbors.Add((position, cost));
            }
        }
    }

    private sealed class TrackingMap : IPathFinderMap<int>
    {
        private readonly int _size;
        private ICollection<(int Position, float Cost)>? _firstCollection;

        public TrackingMap(int size)
        {
            _size = size;
        }

        public bool UsedOneNeighborCollection { get; private set; } = true;

        public void ExpandPosition(int origin, ICollection<(int Position, float Cost)> neighbors)
        {
            if (_firstCollection is null)
            {
                _firstCollection = neighbors;
            }
            else if (!ReferenceEquals(_firstCollection, neighbors))
            {
                UsedOneNeighborCollection = false;
            }

            if (origin > 0)
            {
                neighbors.Add((origin - 1, 1));
            }

            if (origin + 1 < _size)
            {
                neighbors.Add((origin + 1, 1));
            }
        }
    }

    private sealed class ZeroHeuristic : IDistanceHeuristicProvider<int>
    {
        public float GetCost(int start, int destination)
        {
            return 0;
        }
    }
}
