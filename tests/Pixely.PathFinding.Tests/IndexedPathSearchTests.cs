using System.Numerics;
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
        PathResult result = IndexedPathSearch<ushort, byte>.ReconstructPath(0, 3, predecessors, path);

        Assert.Multiple(() =>
        {
            Assert.That(costs, Is.EqualTo(new byte[] { 0, 2, 1, 3 }));
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
        PathResult reconstructedResult = IndexedPathSearch<byte, byte>.ReconstructPath(0, 1, predecessors, path);
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
    public void Search_RejectsAccumulatedCostOverflow()
    {
        TestGraph<byte, byte> graph = new TestGraph<byte, byte>([[(1, 250)], [(2, 10)], []]);
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        List<byte> path = new List<byte>();

        Assert.That(() => search.FindPath(graph, 0, 2, path), Throws.InvalidOperationException.With.InnerException.TypeOf<OverflowException>());
    }

    [Test]
    public void Search_RejectsGraphThatUsesIndexSentinel()
    {
        IndexedPathSearch<byte, byte> search = new IndexedPathSearch<byte, byte>();
        List<byte> path = new List<byte>();

        Assert.That(() => search.FindPath(new OversizedByteGraph(), 0, 1, path), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Search_RejectsInvalidGraphEdges()
    {
        TestGraph<int, float> graph = new TestGraph<int, float>([[(1, -1f)], []]);
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
}
