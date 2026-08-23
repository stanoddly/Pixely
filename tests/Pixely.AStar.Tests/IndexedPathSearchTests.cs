using Pixely.AStar;

namespace Pixely.AStar.Tests;

public sealed class IndexedPathSearchTests
{
    [Test]
    public void ExpandTree_ReturnsLowestCostsAndPredecessors()
    {
        TestGraph graph = new TestGraph(
        [
            [(1, 10f), (2, 1f)],
            [(3, 1f)],
            [(1, 1f)],
            []
        ]);
        IndexedPathSearch<TestGraph> search = new IndexedPathSearch<TestGraph>();
        float[] costs = new float[graph.NodeCount];
        int[] predecessors = new int[graph.NodeCount];
        List<int> path = new List<int>();

        search.ExpandTree(graph, 0, costs, predecessors);
        PathResult result = IndexedPathSearch<TestGraph>.ReconstructPath(0, 3, predecessors, path);

        Assert.Multiple(() =>
        {
            Assert.That(costs, Is.EqualTo(new[] { 0f, 2f, 1f, 3f }));
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new[] { 2, 1, 3 }));
        });
    }

    [Test]
    public void ExpandTree_LeavesNodesBeyondMaximumCostUnreachable()
    {
        TestGraph graph = new TestGraph(
        [
            [(1, 2f), (2, 5f)],
            [(2, 2f)],
            [(3, 2f)],
            []
        ]);
        IndexedPathSearch<TestGraph> search = new IndexedPathSearch<TestGraph>();
        float[] costs = new float[graph.NodeCount];
        int[] predecessors = new int[graph.NodeCount];

        search.ExpandTree(graph, 0, costs, predecessors, 4f);

        Assert.Multiple(() =>
        {
            Assert.That(costs[0], Is.EqualTo(0f));
            Assert.That(costs[1], Is.EqualTo(2f));
            Assert.That(costs[2], Is.EqualTo(4f));
            Assert.That(costs[3], Is.EqualTo(float.PositiveInfinity));
            Assert.That(predecessors[3], Is.EqualTo(-1));
        });
    }

    [Test]
    public void FindPath_ReopensNodeWhenABetterRouteIsFound()
    {
        TestGraph graph = new TestGraph(
        [
            [(1, 2f), (2, 1f)],
            [(3, 2f)],
            [(1, 0.5f), (3, 100f)],
            []
        ]);
        IndexedPathSearch<TestGraph> search = new IndexedPathSearch<TestGraph>();
        List<int> path = new List<int> { 99 };

        PathResult result = search.FindPath(graph, 0, 3, path, new TestHeuristic([0f, 0f, 2.5f, 0f]));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new[] { 2, 1, 3 }));
        });
    }

    [Test]
    public void FindPath_ReturnsNotFoundAndClearsResult()
    {
        TestGraph graph = new TestGraph([[(1, 1f)], [], []]);
        IndexedPathSearch<TestGraph> search = new IndexedPathSearch<TestGraph>();
        List<int> path = new List<int> { 99 };

        PathResult result = search.FindPath(graph, 0, 2, path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.NotFound));
            Assert.That(path, Is.Empty);
        });
    }

    [Test]
    public void Search_RejectsInvalidGraphEdges()
    {
        TestGraph graph = new TestGraph([[(1, -1f)], []]);
        IndexedPathSearch<TestGraph> search = new IndexedPathSearch<TestGraph>();
        List<int> path = new List<int>();

        Assert.That(() => search.FindPath(graph, 0, 1, path), Throws.InvalidOperationException);
    }

    private readonly struct TestGraph : IIndexedPathGraph
    {
        private readonly PathEdge[][] _edges;

        internal TestGraph((int Destination, float Cost)[][] edges)
        {
            _edges = new PathEdge[edges.Length][];
            for (int node = 0; node < edges.Length; node++)
            {
                _edges[node] = new PathEdge[edges[node].Length];
                for (int edge = 0; edge < edges[node].Length; edge++)
                {
                    _edges[node][edge] = new PathEdge(edges[node][edge].Destination, edges[node][edge].Cost);
                }
            }

            MaximumDegree = _edges.Max(edgesForNode => edgesForNode.Length);
        }

        public int NodeCount => _edges.Length;
        public int MaximumDegree { get; }

        public int GetEdges(int origin, Span<PathEdge> edges)
        {
            _edges[origin].CopyTo(edges);
            return _edges[origin].Length;
        }
    }

    private readonly struct TestHeuristic : IIndexedPathHeuristic
    {
        private readonly float[] _costs;

        internal TestHeuristic(float[] costs)
        {
            _costs = costs;
        }

        public float EstimateCost(int origin, int destination)
        {
            return _costs[origin];
        }
    }
}
