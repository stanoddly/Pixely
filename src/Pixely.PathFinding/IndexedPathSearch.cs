using System.Numerics;

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
/// <typeparam name="TCost">A fixed-size numeric type that can represent every accumulated path and estimated cost.</typeparam>
public sealed class IndexedPathSearch<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
{
    private static readonly TIndex MissingPredecessor = TIndex.MaxValue;
    private PathEdge<TIndex, TCost>[] _edges = [];
    private TCost[] _pathCosts = [];
    private TIndex[] _pathPredecessors = [];
    private readonly PriorityQueue<OpenNode, TCost> _open = new PriorityQueue<OpenNode, TCost>();

    /// <summary>
    /// Uses Dijkstra search to write the cheapest cost and predecessor for every node reachable without exceeding the numeric cost range.
    /// </summary>
    /// <remarks>
    /// Both buffers must have at least <see cref="IIndexedPathGraph{TIndex, TCost}.NodeCount"/> entries and are overwritten. Unreachable nodes receive <see cref="IMinMaxValue{TCost}.MaxValue"/> and predecessors receive <see cref="IMinMaxValue{TIndex}.MaxValue"/>.
    /// A reachable path whose cost equals <see cref="IMinMaxValue{TCost}.MaxValue"/> is distinguished by its predecessor.
    /// The search throws if accumulated path costs exceed the range of <typeparamref name="TCost"/>.
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
    /// The search throws if accumulated path costs exceed the range of <typeparamref name="TCost"/>.
    /// </remarks>
    public void ExpandTree<TGraph>(TGraph graph, TIndex start, Span<TCost> costs, Span<TIndex> predecessors, TCost maxCost) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        EnsureCapacity(graph);
        int startOffset = ValidateNode(graph, start, nameof(start));
        ValidateMaximumCost(maxCost);
        if (costs.Length < graph.NodeCount)
        {
            throw new ArgumentException("The cost buffer must contain an entry for every graph node.", nameof(costs));
        }

        if (predecessors.Length < graph.NodeCount)
        {
            throw new ArgumentException("The predecessor buffer must contain an entry for every graph node.", nameof(predecessors));
        }

        costs = costs[..graph.NodeCount];
        predecessors = predecessors[..graph.NodeCount];
        costs.Fill(TCost.MaxValue);
        predecessors.Fill(MissingPredecessor);
        _open.Clear();
        costs[startOffset] = TCost.Zero;
        predecessors[startOffset] = start;
        _open.Enqueue(new OpenNode(start, TCost.Zero), TCost.Zero);

        while (_open.TryDequeue(out OpenNode current, out TCost _))
        {
            int currentOffset = ToOffset(current.Index);
            if (current.Cost > costs[currentOffset])
            {
                continue;
            }

            int edgeCount = GetEdges(graph, current.Index);
            for (int index = 0; index < edgeCount; index++)
            {
                PathEdge<TIndex, TCost> edge = _edges[index];
                TCost cost = AddCosts(current.Cost, edge.Cost);
                int destinationOffset = ToOffset(edge.Destination);
                if (cost > maxCost || (predecessors[destinationOffset] != MissingPredecessor && cost >= costs[destinationOffset]))
                {
                    continue;
                }

                costs[destinationOffset] = cost;
                predecessors[destinationOffset] = current.Index;
                _open.Enqueue(new OpenNode(edge.Destination, cost), cost);
            }
        }
    }

    /// <summary>
    /// Uses Dijkstra search to find a least-cost path to one destination.
    /// </summary>
    /// <remarks>The result is overwritten and contains each node after the start through the destination. The search throws if accumulated path costs exceed the range of <typeparamref name="TCost"/>.</remarks>
    public PathResult FindPath<TGraph>(TGraph graph, TIndex start, TIndex destination, List<TIndex> result) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        return FindPath(graph, start, destination, result, new ZeroHeuristic());
    }

    /// <summary>
    /// Uses A* to find a least-cost path to one destination with an admissible heuristic.
    /// </summary>
    /// <remarks>The result is overwritten and contains each node after the start through the destination. The search throws if accumulated path or estimated costs exceed the range of <typeparamref name="TCost"/>.</remarks>
    /// <typeparam name="TGraph">A value-type adapter that exposes the indexed graph.</typeparam>
    /// <typeparam name="THeuristic">A value-type heuristic whose estimates do not exceed the cheapest remaining path cost.</typeparam>
    public PathResult FindPath<TGraph, THeuristic>(TGraph graph, TIndex start, TIndex destination, List<TIndex> result, THeuristic heuristic)
        where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
        where THeuristic : struct, IIndexedPathHeuristic<TIndex, TCost>
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureCapacity(graph);
        int startOffset = ValidateNode(graph, start, nameof(start));
        ValidateNode(graph, destination, nameof(destination));
        result.Clear();
        Array.Fill(_pathCosts, TCost.MaxValue, 0, graph.NodeCount);
        Array.Fill(_pathPredecessors, MissingPredecessor, 0, graph.NodeCount);
        _open.Clear();
        _pathCosts[startOffset] = TCost.Zero;
        _pathPredecessors[startOffset] = start;
        _open.Enqueue(new OpenNode(start, TCost.Zero), EstimateCost(heuristic, start, destination));

        while (_open.TryDequeue(out OpenNode current, out TCost _))
        {
            int currentOffset = ToOffset(current.Index);
            if (current.Cost > _pathCosts[currentOffset])
            {
                continue;
            }

            if (current.Index == destination)
            {
                ReconstructPath(start, destination, _pathPredecessors.AsSpan(0, graph.NodeCount), result);
                return PathResult.Found;
            }

            int edgeCount = GetEdges(graph, current.Index);
            for (int index = 0; index < edgeCount; index++)
            {
                PathEdge<TIndex, TCost> edge = _edges[index];
                TCost cost = AddCosts(current.Cost, edge.Cost);
                int destinationOffset = ToOffset(edge.Destination);
                if (_pathPredecessors[destinationOffset] != MissingPredecessor && cost >= _pathCosts[destinationOffset])
                {
                    continue;
                }

                _pathCosts[destinationOffset] = cost;
                _pathPredecessors[destinationOffset] = current.Index;
                TCost estimatedCost = AddCosts(cost, EstimateCost(heuristic, edge.Destination, destination));
                _open.Enqueue(new OpenNode(edge.Destination, cost), estimatedCost);
            }
        }

        return PathResult.NotFound;
    }

    /// <summary>
    /// Reconstructs one path from a predecessor tree produced by <see cref="ExpandTree{TGraph}(TGraph, TIndex, Span{TCost}, Span{TIndex})"/>.
    /// </summary>
    /// <remarks>The result is overwritten and contains each node after the start through the destination.</remarks>
    public static PathResult ReconstructPath(TIndex start, TIndex destination, ReadOnlySpan<TIndex> predecessors, List<TIndex> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        TIndex predecessorCount;
        try
        {
            predecessorCount = TIndex.CreateChecked(predecessors.Length);
        }
        catch (OverflowException)
        {
            throw new ArgumentException($"The predecessor buffer must leave {TIndex.MaxValue} available as the missing-predecessor sentinel.", nameof(predecessors));
        }

        ValidateBufferIndex(start, predecessorCount, nameof(start));
        ValidateBufferIndex(destination, predecessorCount, nameof(destination));
        result.Clear();
        if (start == destination)
        {
            return PathResult.Found;
        }

        TIndex current = destination;
        for (int count = 0; count < predecessors.Length; count++)
        {
            TIndex predecessor = predecessors[ToOffset(current)];
            if (predecessor == MissingPredecessor || TIndex.IsNegative(predecessor) || predecessor >= predecessorCount)
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

    private void EnsureCapacity<TGraph>(TGraph graph) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(graph.NodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(graph.MaximumDegree);
        try
        {
            TIndex.CreateChecked(graph.NodeCount);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(graph.NodeCount), graph.NodeCount, $"The graph node count must leave {TIndex.MaxValue} available as the missing-predecessor sentinel.");
        }

        if (_edges.Length < graph.MaximumDegree)
        {
            _edges = new PathEdge<TIndex, TCost>[graph.MaximumDegree];
        }

        if (_pathCosts.Length < graph.NodeCount)
        {
            _pathCosts = new TCost[graph.NodeCount];
            _pathPredecessors = new TIndex[graph.NodeCount];
        }
    }

    private int GetEdges<TGraph>(TGraph graph, TIndex origin) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        int edgeCount = graph.GetEdges(origin, _edges.AsSpan(0, graph.MaximumDegree));
        if (edgeCount < 0 || edgeCount > graph.MaximumDegree)
        {
            throw new InvalidOperationException("The graph returned an invalid edge count.");
        }

        TIndex nodeCount = TIndex.CreateChecked(graph.NodeCount);
        for (int index = 0; index < edgeCount; index++)
        {
            PathEdge<TIndex, TCost> edge = _edges[index];
            if (TIndex.IsNegative(edge.Destination) || edge.Destination >= nodeCount)
            {
                throw new InvalidOperationException("The graph returned an edge outside its node range.");
            }

            if (!TCost.IsFinite(edge.Cost) || TCost.IsNegative(edge.Cost))
            {
                throw new InvalidOperationException("The graph returned a non-finite or negative edge cost.");
            }
        }

        return edgeCount;
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

    private static TCost AddCosts(TCost left, TCost right)
    {
        try
        {
            TCost cost = checked(left + right);
            if (!TCost.IsFinite(cost) || TCost.IsNegative(cost))
            {
                throw new InvalidOperationException("The accumulated path cost is outside the selected cost type's finite non-negative range.");
            }

            return cost;
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("The accumulated path cost exceeds the selected cost type's range.", exception);
        }
    }

    private static void ValidateMaximumCost(TCost maxCost)
    {
        if (TCost.IsNaN(maxCost) || TCost.IsNegative(maxCost))
        {
            throw new ArgumentOutOfRangeException(nameof(maxCost), maxCost, "Maximum cost must be non-negative.");
        }
    }

    private static int ValidateNode<TGraph>(TGraph graph, TIndex node, string parameterName) where TGraph : struct, IIndexedPathGraph<TIndex, TCost>
    {
        if (TIndex.IsNegative(node) || node >= TIndex.CreateChecked(graph.NodeCount))
        {
            throw new ArgumentOutOfRangeException(parameterName, node, "The node is outside the graph.");
        }

        return ToOffset(node);
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

    private readonly record struct OpenNode(TIndex Index, TCost Cost);

    private readonly struct ZeroHeuristic : IIndexedPathHeuristic<TIndex, TCost>
    {
        public TCost EstimateCost(TIndex origin, TIndex destination)
        {
            return TCost.Zero;
        }
    }
}
