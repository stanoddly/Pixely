namespace Pixely.AStar;

public readonly record struct PathEdge(int Destination, float Cost);

public interface IIndexedPathGraph
{
    int NodeCount { get; }
    int MaximumDegree { get; }
    int GetEdges(int origin, Span<PathEdge> edges);
}

public interface IIndexedPathHeuristic
{
    // Estimates must not exceed the cheapest remaining path cost.
    float EstimateCost(int origin, int destination);
}

// Reusable search scratch storage. Calls on one instance must not overlap.
public sealed class IndexedPathSearch
{
    private PathEdge[] _edges = [];
    private float[] _pathCosts = [];
    private int[] _pathPredecessors = [];
    private readonly PriorityQueue<OpenNode, float> _open = new PriorityQueue<OpenNode, float>();

    public void ExpandTree(IIndexedPathGraph graph, int start, Span<float> costs, Span<int> predecessors, float maxCost = float.PositiveInfinity)
    {
        EnsureCapacity(graph);
        ValidateNode(graph, start, nameof(start));
        if (float.IsNaN(maxCost) || maxCost < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCost), maxCost, "Maximum cost must be non-negative.");
        }

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
        costs.Fill(float.PositiveInfinity);
        predecessors.Fill(-1);
        _open.Clear();
        costs[start] = 0f;
        _open.Enqueue(new OpenNode(start, 0f), 0f);

        while (_open.TryDequeue(out OpenNode current, out float _))
        {
            if (current.Cost > costs[current.Index])
            {
                continue;
            }

            int edgeCount = GetEdges(graph, current.Index);
            for (int index = 0; index < edgeCount; index++)
            {
                PathEdge edge = _edges[index];
                float cost = current.Cost + edge.Cost;
                if (cost > maxCost || cost >= costs[edge.Destination])
                {
                    continue;
                }

                costs[edge.Destination] = cost;
                predecessors[edge.Destination] = current.Index;
                _open.Enqueue(new OpenNode(edge.Destination, cost), cost);
            }
        }
    }

    public PathResult FindPath(IIndexedPathGraph graph, int start, int destination, List<int> result, IIndexedPathHeuristic? heuristic = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureCapacity(graph);
        ValidateNode(graph, start, nameof(start));
        ValidateNode(graph, destination, nameof(destination));
        result.Clear();
        Array.Fill(_pathCosts, float.PositiveInfinity, 0, graph.NodeCount);
        Array.Fill(_pathPredecessors, -1, 0, graph.NodeCount);
        _open.Clear();
        _pathCosts[start] = 0f;
        _open.Enqueue(new OpenNode(start, 0f), EstimateCost(heuristic, start, destination));

        while (_open.TryDequeue(out OpenNode current, out float _))
        {
            if (current.Cost > _pathCosts[current.Index])
            {
                continue;
            }

            if (current.Index == destination)
            {
                ReconstructPath(start, destination, _pathPredecessors, result);
                return PathResult.Found;
            }

            int edgeCount = GetEdges(graph, current.Index);
            for (int index = 0; index < edgeCount; index++)
            {
                PathEdge edge = _edges[index];
                float cost = current.Cost + edge.Cost;
                if (cost >= _pathCosts[edge.Destination])
                {
                    continue;
                }

                _pathCosts[edge.Destination] = cost;
                _pathPredecessors[edge.Destination] = current.Index;
                float estimatedCost = cost + EstimateCost(heuristic, edge.Destination, destination);
                _open.Enqueue(new OpenNode(edge.Destination, cost), estimatedCost);
            }
        }

        return PathResult.NotFound;
    }

    public static PathResult ReconstructPath(int start, int destination, ReadOnlySpan<int> predecessors, List<int> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, predecessors.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(destination);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(destination, predecessors.Length);
        result.Clear();
        if (start == destination)
        {
            return PathResult.Found;
        }

        int current = destination;
        for (int count = 0; count < predecessors.Length; count++)
        {
            int predecessor = predecessors[current];
            if (predecessor < 0 || predecessor >= predecessors.Length)
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

    private void EnsureCapacity(IIndexedPathGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(graph.NodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(graph.MaximumDegree);
        if (_edges.Length < graph.MaximumDegree)
        {
            _edges = new PathEdge[graph.MaximumDegree];
        }

        if (_pathCosts.Length < graph.NodeCount)
        {
            _pathCosts = new float[graph.NodeCount];
            _pathPredecessors = new int[graph.NodeCount];
        }
    }

    private int GetEdges(IIndexedPathGraph graph, int origin)
    {
        int edgeCount = graph.GetEdges(origin, _edges.AsSpan(0, graph.MaximumDegree));
        if (edgeCount < 0 || edgeCount > graph.MaximumDegree)
        {
            throw new InvalidOperationException("The graph returned an invalid edge count.");
        }

        for (int index = 0; index < edgeCount; index++)
        {
            PathEdge edge = _edges[index];
            if ((uint)edge.Destination >= (uint)graph.NodeCount)
            {
                throw new InvalidOperationException("The graph returned an edge outside its node range.");
            }

            if (!float.IsFinite(edge.Cost) || edge.Cost < 0f)
            {
                throw new InvalidOperationException("The graph returned a non-finite or negative edge cost.");
            }
        }

        return edgeCount;
    }

    private static float EstimateCost(IIndexedPathHeuristic? heuristic, int origin, int destination)
    {
        if (heuristic == null)
        {
            return 0f;
        }

        float cost = heuristic.EstimateCost(origin, destination);
        if (!float.IsFinite(cost) || cost < 0f)
        {
            throw new InvalidOperationException("The heuristic returned a non-finite or negative estimate.");
        }

        return cost;
    }

    private static void ValidateNode(IIndexedPathGraph graph, int node, string parameterName)
    {
        if ((uint)node >= (uint)graph.NodeCount)
        {
            throw new ArgumentOutOfRangeException(parameterName, node, "The node is outside the graph.");
        }
    }

    private readonly record struct OpenNode(int Index, float Cost);
}
