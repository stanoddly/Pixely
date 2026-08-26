using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pixely.PathFinding;

public readonly record struct PathEdge<TIndex, TCost>(TIndex Destination, TCost Cost);

public interface IIndexedPathGraph<TIndex, TCost>
{
    int NodeCount { get; }
    int MaximumDegree { get; }
    int GetEdges(TIndex origin, Span<PathEdge<TIndex, TCost>> edges);
}

public interface IIndexedPathHeuristic<TIndex, TCost>
{
    // Estimates must not exceed the cheapest remaining path cost.
    TCost EstimateCost(TIndex origin, TIndex destination);
}

/// <summary>
/// Finds least-cost paths and shortest-path trees in graphs whose nodes have stable dense integer indices.
/// </summary>
/// <remarks>
/// Choose this type for repeated searches when every node maps to an index from zero through <see cref="IIndexedPathGraph{TIndex, TCost}.NodeCount"/> minus one.
/// It uses array-indexed state, accepts caller-owned tree buffers, and reuses its internal search storage to avoid hash lookups and steady-state search allocations.
/// <see cref="ExpandTree{TGraph}(TGraph, TIndex, Span{TCost}, Span{TIndex})"/> and the <see cref="FindPath{TGraph}(TGraph, TIndex, TIndex, List{TIndex})"/> overload use Dijkstra search; the heuristic overload uses A*.
/// Use <see cref="PathFinder{TPoint}"/> instead when nodes are more naturally represented by arbitrary value types or stable dense indices are unavailable.
/// Calls on the same instance must not overlap. Graph and heuristic struct constraints let the JIT specialize each search and inline constrained interface calls.
/// </remarks>
/// <typeparam name="TIndex">An integer type that can represent the graph's dense node indices. Its maximum value is reserved as the missing-predecessor sentinel.</typeparam>
/// <typeparam name="TCost">A fixed-size numeric type used for accumulated path and estimated costs. Paths whose costs exceed its range are ignored.</typeparam>
public sealed class IndexedPathSearch<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
{
    private static readonly TIndex MissingPredecessor = TIndex.MaxValue;
    private PathEdge<TIndex, TCost>[] _edges = [];
    private TCost[] _pathCosts = [];
    private TIndex[] _pathPredecessors = [];
    private readonly IndexedPriorityQueue<TIndex, TCost> _open = new IndexedPriorityQueue<TIndex, TCost>();

    /// <summary>
    /// Uses Dijkstra search to write the cheapest cost and predecessor for every node reachable without exceeding the numeric cost range.
    /// </summary>
    /// <remarks>
    /// Both buffers must have at least <see cref="IIndexedPathGraph{TIndex, TCost}.NodeCount"/> entries and are overwritten. Unreachable nodes receive <see cref="IMinMaxValue{TCost}.MaxValue"/> and predecessors receive <see cref="IMinMaxValue{TIndex}.MaxValue"/>.
    /// A reachable path whose cost equals <see cref="IMinMaxValue{TCost}.MaxValue"/> is distinguished by its predecessor.
    /// The start node's predecessor is itself. Paths whose accumulated costs exceed the range of <typeparamref name="TCost"/> are ignored.
    /// </remarks>
    public void ExpandTree<TGraph>(TGraph graph, TIndex start, Span<TCost> costs, Span<TIndex> predecessors) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        ExpandTree(graph, start, costs, predecessors, TCost.MaxValue);
    }

    /// <summary>
    /// Uses Dijkstra search to write the cheapest cost and predecessor for every node reachable within the maximum cost.
    /// </summary>
    /// <remarks>
    /// Both buffers must have at least <see cref="IIndexedPathGraph{TIndex, TCost}.NodeCount"/> entries and are overwritten. Unreachable nodes receive <see cref="IMinMaxValue{TCost}.MaxValue"/> and predecessors receive <see cref="IMinMaxValue{TIndex}.MaxValue"/>.
    /// A reachable path whose cost equals <see cref="IMinMaxValue{TCost}.MaxValue"/> is distinguished by its predecessor.
    /// The start node's predecessor is itself. Paths whose accumulated costs exceed the range of <typeparamref name="TCost"/> are ignored.
    /// </remarks>
    public void ExpandTree<TGraph>(TGraph graph, TIndex start, Span<TCost> costs, Span<TIndex> predecessors, TCost maxCost) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        (int nodeCount, int maximumDegree) = EnsureGraphCapacity(graph);
        int startOffset = ValidateNode(start, nodeCount, nameof(start));
        ValidateMaximumCost(maxCost);
        if (costs.Length < nodeCount)
        {
            throw new ArgumentException("The cost buffer must contain an entry for every graph node.", nameof(costs));
        }

        if (predecessors.Length < nodeCount)
        {
            throw new ArgumentException("The predecessor buffer must contain an entry for every graph node.", nameof(predecessors));
        }

        costs = costs[..nodeCount];
        predecessors = predecessors[..nodeCount];
        costs.Fill(TCost.MaxValue);
        predecessors.Fill(MissingPredecessor);
        _open.Clear();
        costs[startOffset] = TCost.Zero;
        predecessors[startOffset] = start;
        _open.EnqueueOrUpdate(startOffset, TCost.Zero);

        while (_open.TryDequeue(out int currentOffset))
        {
            TIndex current = TIndex.CreateChecked(currentOffset);
            TCost currentCost = costs[currentOffset];
            int edgeCount = GetEdges(graph, current, maximumDegree);
            for (int index = 0; index < edgeCount; index++)
            {
                PathEdge<TIndex, TCost> edge = _edges[index];
                int destinationOffset = ValidateEdge(edge, nodeCount);
                if (!TryAddCosts(currentCost, edge.Cost, out TCost cost))
                {
                    continue;
                }

                if (cost > maxCost || (cost >= costs[destinationOffset] && (cost != TCost.MaxValue || predecessors[destinationOffset] != MissingPredecessor)))
                {
                    continue;
                }

                costs[destinationOffset] = cost;
                predecessors[destinationOffset] = current;
                _open.EnqueueOrUpdate(destinationOffset, cost);
            }
        }
    }

    /// <summary>
    /// Uses Dijkstra search to find a least-cost path to one destination.
    /// </summary>
    /// <remarks>The result is overwritten and contains each node after the start through the destination. Paths whose accumulated costs exceed the range of <typeparamref name="TCost"/> are ignored.</remarks>
    public PathResult FindPath<TGraph>(TGraph graph, TIndex start, TIndex destination, List<TIndex> result) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        return FindPath(graph, start, destination, result, new ZeroHeuristic());
    }

    /// <summary>
    /// Uses A* to find a least-cost path to one destination with an admissible heuristic.
    /// </summary>
    /// <remarks>The result is overwritten and contains each node after the start through the destination. Paths whose accumulated costs exceed the range of <typeparamref name="TCost"/> are ignored, while unrepresentable estimated costs receive the lowest priority.</remarks>
    /// <typeparam name="TGraph">A value-type adapter that exposes the indexed graph.</typeparam>
    /// <typeparam name="THeuristic">A value-type heuristic whose estimates do not exceed the cheapest remaining path cost.</typeparam>
    public PathResult FindPath<TGraph, THeuristic>(TGraph graph, TIndex start, TIndex destination, List<TIndex> result, THeuristic heuristic)
        where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
        where THeuristic : struct, IIndexedPathHeuristic<TIndex, TCost>
    {
        ArgumentNullException.ThrowIfNull(result);
        (int nodeCount, int maximumDegree) = EnsureGraphCapacity(graph);
        EnsurePathCapacity(nodeCount);
        int startOffset = ValidateNode(start, nodeCount, nameof(start));
        ValidateNode(destination, nodeCount, nameof(destination));
        result.Clear();
        Array.Fill(_pathCosts, TCost.MaxValue, 0, nodeCount);
        Array.Fill(_pathPredecessors, MissingPredecessor, 0, nodeCount);
        _open.Clear();
        _pathCosts[startOffset] = TCost.Zero;
        _pathPredecessors[startOffset] = start;
        _open.EnqueueOrUpdate(startOffset, EstimateCost(heuristic, start, destination));

        while (_open.TryDequeue(out int currentOffset))
        {
            TIndex current = TIndex.CreateChecked(currentOffset);
            if (current == destination)
            {
                ReconstructPath(start, destination, nodeCount, _pathPredecessors, result);
                return PathResult.Found;
            }

            TCost currentCost = _pathCosts[currentOffset];
            int edgeCount = GetEdges(graph, current, maximumDegree);
            for (int index = 0; index < edgeCount; index++)
            {
                PathEdge<TIndex, TCost> edge = _edges[index];
                int destinationOffset = ValidateEdge(edge, nodeCount);
                if (!TryAddCosts(currentCost, edge.Cost, out TCost cost))
                {
                    continue;
                }

                if (cost >= _pathCosts[destinationOffset] && (cost != TCost.MaxValue || _pathPredecessors[destinationOffset] != MissingPredecessor))
                {
                    continue;
                }

                _pathCosts[destinationOffset] = cost;
                _pathPredecessors[destinationOffset] = current;
                TCost estimatedCost = TryAddCosts(cost, EstimateCost(heuristic, edge.Destination, destination), out TCost sum) ? sum : TCost.MaxValue;
                _open.EnqueueOrUpdate(destinationOffset, estimatedCost);
            }
        }

        return PathResult.NotFound;
    }

    /// <summary>
    /// Reconstructs one path from a predecessor tree produced by <see cref="ExpandTree{TGraph}(TGraph, TIndex, Span{TCost}, Span{TIndex})"/>.
    /// </summary>
    /// <remarks>
    /// The result is overwritten and contains each node after the start through the destination. Only the first <paramref name="nodeCount"/> predecessor entries belong to the tree.
    /// A self-predecessor marks a tree root; reaching a root other than <paramref name="start"/> returns <see cref="PathResult.NotFound"/>. Longer predecessor cycles are invalid.
    /// </remarks>
    public static PathResult ReconstructPath(TIndex start, TIndex destination, int nodeCount, ReadOnlySpan<TIndex> predecessors, List<TIndex> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nodeCount);
        if (predecessors.Length < nodeCount)
        {
            throw new ArgumentException("The predecessor buffer must contain an entry for every graph node.", nameof(predecessors));
        }

        TIndex predecessorCount;
        try
        {
            predecessorCount = TIndex.CreateChecked(nodeCount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeCount), nodeCount, $"The node count must leave {TIndex.MaxValue} available as the missing-predecessor sentinel.");
        }

        ValidateBufferIndex(start, predecessorCount, nameof(start));
        ValidateBufferIndex(destination, predecessorCount, nameof(destination));
        predecessors = predecessors[..nodeCount];
        result.Clear();
        if (start == destination)
        {
            return PathResult.Found;
        }

        TIndex current = destination;
        for (int count = 0; count < predecessors.Length; count++)
        {
            TIndex predecessor = predecessors[ToOffset(current)];
            if (predecessor == MissingPredecessor || predecessor == current || TIndex.IsNegative(predecessor) || predecessor >= predecessorCount)
            {
                result.Clear();
                return PathResult.NotFound;
            }

            result.Add(current);
            if (predecessor == start)
            {
                result.Reverse();
                return PathResult.Found;
            }

            current = predecessor;
        }

        throw new ArgumentException("The predecessor buffer contains a cycle.", nameof(predecessors));
    }

    private (int NodeCount, int MaximumDegree) EnsureGraphCapacity<TGraph>(TGraph graph) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        int nodeCount = graph.NodeCount;
        int maximumDegree = graph.MaximumDegree;
        if (nodeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(graph), nodeCount, "The graph node count must be positive.");
        }

        if (maximumDegree < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(graph), maximumDegree, "The graph maximum degree must be non-negative.");
        }

        try
        {
            TIndex.CreateChecked(nodeCount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(graph), nodeCount, $"The graph node count must leave {TIndex.MaxValue} available as the missing-predecessor sentinel.");
        }

        if (_edges.Length < maximumDegree)
        {
            _edges = new PathEdge<TIndex, TCost>[maximumDegree];
        }

        _open.EnsureNodeCapacity(nodeCount);

        return (nodeCount, maximumDegree);
    }

    private void EnsurePathCapacity(int nodeCount)
    {
        if (_pathCosts.Length < nodeCount)
        {
            _pathCosts = new TCost[nodeCount];
            _pathPredecessors = new TIndex[nodeCount];
        }
    }

    private int GetEdges<TGraph>(TGraph graph, TIndex origin, int maximumDegree) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        int edgeCount = graph.GetEdges(origin, _edges.AsSpan(0, maximumDegree));
        if (edgeCount < 0 || edgeCount > maximumDegree)
        {
            throw new InvalidOperationException("The graph returned an invalid edge count.");
        }

        return edgeCount;
    }

    private static int ValidateEdge(PathEdge<TIndex, TCost> edge, int nodeCount)
    {
        int destinationOffset = int.CreateSaturating(edge.Destination);
        if ((uint)destinationOffset >= (uint)nodeCount)
        {
            throw new InvalidOperationException("The graph returned an edge outside its node range.");
        }

        if (!TCost.IsFinite(edge.Cost) || TCost.IsNegative(edge.Cost))
        {
            throw new InvalidOperationException("The graph returned a non-finite or negative edge cost.");
        }

        return destinationOffset;
    }

    private static TCost EstimateCost<THeuristic>(THeuristic heuristic, TIndex origin, TIndex destination) where THeuristic : struct, IIndexedPathHeuristic<TIndex, TCost>
    {
        TCost cost = heuristic.EstimateCost(origin, destination);
        if (!TCost.IsFinite(cost) || TCost.IsNegative(cost))
        {
            throw new InvalidOperationException("The heuristic returned a non-finite or negative estimate.");
        }

        return cost;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryAddCosts(TCost left, TCost right, out TCost result)
    {
        if (TCost.IsInfinity(TCost.CreateSaturating(double.PositiveInfinity)))
        {
            result = left + right;
            if (!TCost.IsFinite(result))
            {
                result = TCost.MaxValue;
                return false;
            }

            return true;
        }

        if (right > TCost.MaxValue - left)
        {
            result = TCost.MaxValue;
            return false;
        }

        result = left + right;
        if (!TCost.IsFinite(result))
        {
            result = TCost.MaxValue;
            return false;
        }

        return true;
    }

    private static void ValidateMaximumCost(TCost maxCost)
    {
        if (TCost.IsNaN(maxCost) || TCost.IsNegative(maxCost))
        {
            throw new ArgumentOutOfRangeException(nameof(maxCost), maxCost, "Maximum cost must be non-negative.");
        }
    }

    private static int ValidateNode(TIndex node, int nodeCount, string parameterName)
    {
        int nodeOffset = int.CreateSaturating(node);
        if ((uint)nodeOffset >= (uint)nodeCount)
        {
            throw new ArgumentOutOfRangeException(parameterName, node, "The node is outside the graph.");
        }

        return nodeOffset;
    }

    private static void ValidateBufferIndex(TIndex index, TIndex count, string parameterName)
    {
        if (TIndex.IsNegative(index) || index >= count)
        {
            throw new ArgumentOutOfRangeException(parameterName, index, "The node is outside the predecessor buffer.");
        }
    }

    private static int ToOffset(TIndex index)
    {
        return int.CreateChecked(index);
    }

    private readonly struct ZeroHeuristic : IIndexedPathHeuristic<TIndex, TCost>
    {
        public TCost EstimateCost(TIndex origin, TIndex destination)
        {
            return TCost.Zero;
        }
    }
}
