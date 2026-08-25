using System.Numerics;
using System.Reflection;
using Pixely.PathFinding;

namespace Pixely.PathFinding.Tests;

public sealed class IndexedPathSearchTests
{
    [Test]
    public void ExpandTree_ReturnsLowestCostsAndPredecessorsWithCompactTypes()
    {
        TestGraph<ushort, byte> graph = new TestGraph<ushort, byte>(
        [
            [(1, 10), (2, 1)],
            [(3, 1)],
            [(1, 1)],
            []
        ]);
        IndexedPathSearch<ushort, byte> search = new IndexedPathSearch<ushort, byte>();
        byte[] costs = new byte[graph.NodeCount];
        ushort[] predecessors = new ushort[graph.NodeCount];
        List<ushort> path = new List<ushort>();

        search.ExpandTree(graph, 0, costs, predecessors);
        PathResult result = IndexedPathSearch<ushort, byte>.ReconstructPath(0, 3, graph.NodeCount, predecessors, path);

        Assert.Multiple(() =>
        {
            Assert.That(costs, Is.EqualTo(new byte[] { 0, 2, 1, 3 }));
            Assert.That(predecessors[0], Is.Zero);
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new ushort[] { 2, 1, 3 }));
        });
    }

    [Test]
    public void ExpandTree_LeavesNodesBeyondMaximumCostUnreachable()
    {
        TestGraph<ushort, byte> graph = new TestGraph<ushort, byte>(
        [
            [(1, 2), (2, 5)],
            [(2, 2)],
            [(3, 2)],
            []
        ]);
        IndexedPathSearch<ushort, byte> search = new IndexedPathSearch<ushort, byte>();
        byte[] costs = new byte[graph.NodeCount];
        ushort[] predecessors = new ushort[graph.NodeCount];

        search.ExpandTree(graph, 0, costs, predecessors, 4);

        Assert.Multiple(() =>
        {
            Assert.That(costs[0], Is.Zero);
            Assert.That(costs[1], Is.EqualTo(2));
            Assert.That(costs[2], Is.EqualTo(4));
            Assert.That(costs[3], Is.EqualTo(byte.MaxValue));
            Assert.That(predecessors[3], Is.EqualTo(ushort.MaxValue));
        });
    }

    [Test]
    public void ExpandTree_RejectsShortBuffers()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, 1)], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();

        Assert.Multiple(() =>
        {
            Assert.That(() => search.ExpandTree(graph, 0, new byte[1], new byte[2]), Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("costs"));
            Assert.That(() => search.ExpandTree(graph, 0, new byte[2], new byte[1]), Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("predecessors"));
        });
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    public void ExpandTree_RejectsInvalidMaximumCost(float maxCost)
    {
        TestGraph<int, float> graph = new TestGraph<int, float>([[]]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();

        Assert.That(() => search.ExpandTree(graph, 0, new float[1], new int[1], maxCost), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void FindPath_ReopensNodeWhenABetterRouteIsFound()
    {
        TestGraph<int, float> graph = new TestGraph<int, float>(
        [
            [(1, 2f), (2, 1f)],
            [(3, 2f)],
            [(1, 0.5f), (3, 100f)],
            []
        ]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int> { 99 };

        PathResult result = search.FindPath(graph, 0, 3, path, new TestHeuristic<int, float>([0f, 0f, 2.5f, 0f]));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new[] { 2, 1, 3 }));
        });
    }

    [Test]
    public void FindPath_ReturnsNotFoundAndClearsResult()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, 1)], [], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        List<byte> path = new List<byte> { 99 };

        PathResult result = search.FindPath(graph, 0, 2, path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.NotFound));
            Assert.That(path, Is.Empty);
        });
    }

    [Test]
    public void FindPath_AcceptsMaximumRepresentableCost()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, byte.MaxValue)], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        byte[] costs = new byte[graph.NodeCount];
        byte[] predecessors = new byte[graph.NodeCount];
        List<byte> path = new List<byte>();

        search.ExpandTree(graph, 0, costs, predecessors);
        PathResult reconstructedResult = IndexedPathSearch<byte, byte>.ReconstructPath(0, 1, graph.NodeCount, predecessors, path);
        PathResult result = search.FindPath(graph, 0, 1, path);

        Assert.Multiple(() =>
        {
            Assert.That(costs[1], Is.EqualTo(byte.MaxValue));
            Assert.That(predecessors[1], Is.Zero);
            Assert.That(reconstructedResult, Is.EqualTo(PathResult.Found));
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new byte[] { 1 }));
        });
    }

    [Test]
    public void FindPath_IgnoresOverflowingBranch()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, 1), (2, 5)], [(3, byte.MaxValue)], [], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        List<byte> path = new List<byte>();

        PathResult result = search.FindPath(graph, 0, 2, path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new byte[] { 2 }));
        });
    }

    [Test]
    public void FindPath_IgnoresOverflowingEstimatedCost()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, 1), (2, 5)], [], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        List<byte> path = new List<byte>();

        PathResult result = search.FindPath(graph, 0, 2, path, new TestHeuristic<byte, byte>([0, byte.MaxValue, 0]));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new byte[] { 2 }));
        });
    }

    [Test]
    public void FindPath_ReturnsNotFoundWhenEveryPathExceedsCostRange()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, 250)], [(2, 10)], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        List<byte> path = new List<byte>();

        PathResult result = search.FindPath(graph, 0, 2, path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.NotFound));
            Assert.That(path, Is.Empty);
        });
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void FindPath_RejectsInvalidHeuristic(float estimate)
    {
        TestGraph<int, float> graph = new TestGraph<int, float>([[(1, 1f)], []]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int>();

        Assert.That(() => search.FindPath(graph, 0, 1, path, new TestHeuristic<int, float>([estimate, 0f])), Throws.InvalidOperationException);
    }

    [Test]
    public void ExpandTree_IgnoresOverflowBeyondMaximumCost()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, 1), (2, 5)], [(3, byte.MaxValue)], [], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        byte[] costs = new byte[graph.NodeCount];
        byte[] predecessors = new byte[graph.NodeCount];

        search.ExpandTree(graph, 0, costs, predecessors, 10);

        Assert.Multiple(() =>
        {
            Assert.That(costs[2], Is.EqualTo(5));
            Assert.That(costs[3], Is.EqualTo(byte.MaxValue));
            Assert.That(predecessors[3], Is.EqualTo(byte.MaxValue));
        });
    }

    [Test]
    public void ReconstructPath_ReturnsNotFoundWhenTreeRootIsReached()
    {
        List<byte> path = new List<byte> { 99 };

        PathResult result = IndexedPathSearch<byte, byte>.ReconstructPath(2, 1, 3, new byte[] { 0, 0, 1 }, path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.NotFound));
            Assert.That(path, Is.Empty);
        });
    }

    [Test]
    public void ReconstructPath_AcceptsOversizedPredecessorBuffer()
    {
        BoundaryByteGraph graph = new BoundaryByteGraph();
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        byte[] costs = new byte[300];
        byte[] predecessors = new byte[300];
        List<byte> path = new List<byte>();

        search.ExpandTree(graph, 0, costs, predecessors);
        PathResult result = IndexedPathSearch<byte, byte>.ReconstructPath(0, 254, graph.NodeCount, predecessors, path);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(PathResult.Found));
            Assert.That(path, Is.EqualTo(new byte[] { 254 }));
        });
    }

    [Test]
    public void ReconstructPath_RejectsDestinationOutsideLogicalNodeCount()
    {
        byte[] predecessors = new byte[10];
        predecessors.AsSpan().Fill(byte.MaxValue);
        predecessors[0] = 0;
        List<byte> path = new List<byte>();

        Assert.That(() => IndexedPathSearch<byte, byte>.ReconstructPath(0, 7, 3, predecessors, path), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("destination"));
    }

    [Test]
    public void ReconstructPath_RejectsLongerPredecessorCycle()
    {
        List<byte> path = new List<byte>();

        Assert.That(() => IndexedPathSearch<byte, byte>.ReconstructPath(0, 2, 3, new byte[] { 0, 2, 1 }, path), Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("predecessors"));
    }

    [Test]
    public void ExpandTree_DoesNotRetainFindPathState()
    {
        TestGraph<int, float> graph = new TestGraph<int, float>([[(1, 1f)], []]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        float[] costs = new float[graph.NodeCount];
        int[] predecessors = new int[graph.NodeCount];

        search.ExpandTree(graph, 0, costs, predecessors);

        FieldInfo costsField = typeof(IndexedPathSearch<int, float>).GetField("_pathCosts", BindingFlags.Instance | BindingFlags.NonPublic)!;
        FieldInfo predecessorsField = typeof(IndexedPathSearch<int, float>).GetField("_pathPredecessors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Multiple(() =>
        {
            Assert.That((Array)costsField.GetValue(search)!, Is.Empty);
            Assert.That((Array)predecessorsField.GetValue(search)!, Is.Empty);
        });
    }

    [Test]
    public void Search_RejectsGraphThatUsesIndexSentinel()
    {
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        List<byte> path = new List<byte>();

        Assert.That(() => search.FindPath(new OversizedByteGraph(), 0, 1, path), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("graph"));
    }

    [Test]
    public void Search_RejectsInvalidGraphEdges()
    {
        TestGraph<int, float> graph = new TestGraph<int, float>([[(1, -1f)], []]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int>();

        Assert.That(() => search.FindPath(graph, 0, 1, path), Throws.InvalidOperationException);
    }

    [Test]
    public void Search_RejectsNegativeNodes()
    {
        TestGraph<int, float> graph = new TestGraph<int, float>([[(1, 1f)], []]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int>();

        Assert.Multiple(() =>
        {
            Assert.That(() => search.FindPath(graph, -1, 1, path), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("start"));
            Assert.That(() => search.FindPath(graph, 0, -1, path), Throws.TypeOf<ArgumentOutOfRangeException>().With.Property(nameof(ArgumentOutOfRangeException.ParamName)).EqualTo("destination"));
        });
    }

    [Test]
    public void Search_RejectsNegativeEdgeDestination()
    {
        TestGraph<int, float> graph = new TestGraph<int, float>([[(-1, 1f)], []]);
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        List<int> path = new List<int>();

        Assert.That(() => search.FindPath(graph, 0, 1, path), Throws.InvalidOperationException);
    }

    private readonly struct TestGraph<TIndex, TCost> : IIndexedPathGraph<TIndex, TCost> where TIndex : unmanaged, IBinaryInteger<TIndex>
    {
        private readonly PathEdge<TIndex, TCost>[][] _edges;

        internal TestGraph((TIndex Destination, TCost Cost)[][] edges)
        {
            _edges = new PathEdge<TIndex, TCost>[edges.Length][];
            for (int node = 0; node < edges.Length; node++)
            {
                _edges[node] = new PathEdge<TIndex, TCost>[edges[node].Length];
                for (int edge = 0; edge < edges[node].Length; edge++)
                {
                    _edges[node][edge] = new PathEdge<TIndex, TCost>(edges[node][edge].Destination, edges[node][edge].Cost);
                }
            }

            MaximumDegree = _edges.Max(edgesForNode => edgesForNode.Length);
        }

        public int NodeCount => _edges.Length;
        public int MaximumDegree { get; }

        public int GetEdges(TIndex origin, Span<PathEdge<TIndex, TCost>> edges)
        {
            PathEdge<TIndex, TCost>[] source = _edges[int.CreateChecked(origin)];
            source.CopyTo(edges);
            return source.Length;
        }
    }

    private readonly struct TestHeuristic<TIndex, TCost> : IIndexedPathHeuristic<TIndex, TCost> where TIndex : unmanaged, IBinaryInteger<TIndex>
    {
        private readonly TCost[] _costs;

        internal TestHeuristic(TCost[] costs)
        {
            _costs = costs;
        }

        public TCost EstimateCost(TIndex origin, TIndex destination)
        {
            return _costs[int.CreateChecked(origin)];
        }
    }

    private readonly struct OversizedByteGraph : IIndexedPathGraph<byte, byte>
    {
        public int NodeCount => 256;
        public int MaximumDegree => 0;

        public int GetEdges(byte origin, Span<PathEdge<byte, byte>> edges)
        {
            return 0;
        }
    }

    private readonly struct BoundaryByteGraph : IIndexedPathGraph<byte, byte>
    {
        public int NodeCount => byte.MaxValue;
        public int MaximumDegree => 1;

        public int GetEdges(byte origin, Span<PathEdge<byte, byte>> edges)
        {
            if (origin == 0)
            {
                edges[0] = new PathEdge<byte, byte>(254, 1);
                return 1;
            }

            return 0;
        }
    }
}
