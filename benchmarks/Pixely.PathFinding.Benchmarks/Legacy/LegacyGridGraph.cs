namespace Pixely.PathFinding.Benchmarks.Legacy;

internal readonly struct LegacyGridGraph : IIndexedPathGraph
{
    private readonly int[] _offsets;
    private readonly PathEdge[] _edges;

    private LegacyGridGraph(int width, int height, int[] offsets, PathEdge[] edges)
    {
        Width = width;
        Height = height;
        _offsets = offsets;
        _edges = edges;
    }

    public int Width { get; }
    public int Height { get; }
    public int NodeCount => Width * Height;
    public int MaximumDegree => 4;

    public int GetEdges(int origin, Span<PathEdge> edges)
    {
        int start = _offsets[origin];
        int count = _offsets[origin + 1] - start;
        _edges.AsSpan(start, count).CopyTo(edges);
        return count;
    }

    internal static LegacyGridGraph Create(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int nodeCount = checked(width * height);
        int[] offsets = new int[nodeCount + 1];
        int edgeCount = 0;
        for (int origin = 0; origin < nodeCount; origin++)
        {
            int x = origin % width;
            int y = origin / width;
            edgeCount += x > 0 ? 1 : 0;
            edgeCount += x + 1 < width ? 1 : 0;
            edgeCount += y > 0 ? 1 : 0;
            edgeCount += y + 1 < height ? 1 : 0;
            offsets[origin + 1] = edgeCount;
        }

        PathEdge[] edges = new PathEdge[edgeCount];
        int edgeOffset = 0;
        for (int origin = 0; origin < nodeCount; origin++)
        {
            int x = origin % width;
            int y = origin / width;
            if (x > 0)
            {
                edges[edgeOffset++] = new PathEdge(origin - 1, 1f);
            }

            if (x + 1 < width)
            {
                edges[edgeOffset++] = new PathEdge(origin + 1, 1f);
            }

            if (y > 0)
            {
                edges[edgeOffset++] = new PathEdge(origin - width, 1f);
            }

            if (y + 1 < height)
            {
                edges[edgeOffset++] = new PathEdge(origin + width, 1f);
            }
        }

        return new LegacyGridGraph(width, height, offsets, edges);
    }
}

internal readonly struct LegacyManhattanHeuristic : IIndexedPathHeuristic
{
    private readonly int _width;

    internal LegacyManhattanHeuristic(int width)
    {
        _width = width;
    }

    public float EstimateCost(int origin, int destination)
    {
        return Math.Abs(origin % _width - destination % _width) + Math.Abs(origin / _width - destination / _width);
    }
}
