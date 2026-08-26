using System.Numerics;

namespace Pixely.PathFinding.Benchmarks;

internal enum GridTopology
{
    Open,
    Weighted,
    Partitioned
}

internal readonly struct SyntheticGridGraph<TIndex, TCost> : IIndexedPathGraph<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
{
    private readonly int[] _offsets;
    private readonly PathEdge<TIndex, TCost>[] _edges;

    private SyntheticGridGraph(int width, int height, int[] offsets, PathEdge<TIndex, TCost>[] edges)
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
    public int EdgeCount => _edges.Length;

    public int GetEdges(TIndex origin, Span<PathEdge<TIndex, TCost>> edges)
    {
        int originOffset = int.CreateChecked(origin);
        int start = _offsets[originOffset];
        int count = _offsets[originOffset + 1] - start;
        _edges.AsSpan(start, count).CopyTo(edges);
        return count;
    }

    internal static SyntheticGridGraph<TIndex, TCost> Create(int width, int height, GridTopology topology)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int nodeCount = checked(width * height);
        TIndex.CreateChecked(nodeCount);
        int[] offsets = new int[nodeCount + 1];
        int edgeCount = 0;
        for (int origin = 0; origin < nodeCount; origin++)
        {
            edgeCount += CountEdges(origin, width, height, topology);
            offsets[origin + 1] = edgeCount;
        }

        PathEdge<TIndex, TCost>[] edges = new PathEdge<TIndex, TCost>[edgeCount];
        int edgeOffset = 0;
        for (int origin = 0; origin < nodeCount; origin++)
        {
            int x = origin % width;
            int y = origin / width;
            if (x > 0)
            {
                AddEdge(edges, ref edgeOffset, origin, origin - 1, width, topology);
            }

            if (x + 1 < width)
            {
                AddEdge(edges, ref edgeOffset, origin, origin + 1, width, topology);
            }

            if (y > 0)
            {
                AddEdge(edges, ref edgeOffset, origin, origin - width, width, topology);
            }

            if (y + 1 < height)
            {
                AddEdge(edges, ref edgeOffset, origin, origin + width, width, topology);
            }
        }

        return new SyntheticGridGraph<TIndex, TCost>(width, height, offsets, edges);
    }

    private static int CountEdges(int origin, int width, int height, GridTopology topology)
    {
        int x = origin % width;
        int y = origin / width;
        int count = 0;
        count += x > 0 && HasEdge(origin, origin - 1, width, topology) ? 1 : 0;
        count += x + 1 < width && HasEdge(origin, origin + 1, width, topology) ? 1 : 0;
        count += y > 0 && HasEdge(origin, origin - width, width, topology) ? 1 : 0;
        count += y + 1 < height && HasEdge(origin, origin + width, width, topology) ? 1 : 0;
        return count;
    }

    private static void AddEdge(PathEdge<TIndex, TCost>[] edges, ref int edgeOffset, int origin, int destination, int width, GridTopology topology)
    {
        if (!HasEdge(origin, destination, width, topology))
        {
            return;
        }

        int cost = topology == GridTopology.Weighted ? 1 + DeterministicWeight(origin, destination) % 4 : 1;
        edges[edgeOffset++] = new PathEdge<TIndex, TCost>(TIndex.CreateChecked(destination), TCost.CreateChecked(cost));
    }

    private static bool HasEdge(int origin, int destination, int width, GridTopology topology)
    {
        if (topology != GridTopology.Partitioned)
        {
            return true;
        }

        int divider = width / 2;
        int originX = origin % width;
        int destinationX = destination % width;
        return !(originX == divider - 1 && destinationX == divider || originX == divider && destinationX == divider - 1);
    }

    private static int DeterministicWeight(int origin, int destination)
    {
        uint value = unchecked((uint)origin * 2654435761u ^ (uint)destination * 2246822519u);
        value ^= value >> 16;
        return (int)(value & 0x7fffffffu);
    }
}

internal readonly struct ManhattanHeuristic<TIndex, TCost> : IIndexedPathHeuristic<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
{
    private readonly int _width;

    internal ManhattanHeuristic(int width)
    {
        _width = width;
    }

    public TCost EstimateCost(TIndex origin, TIndex destination)
    {
        int originOffset = int.CreateChecked(origin);
        int destinationOffset = int.CreateChecked(destination);
        int distance = Math.Abs(originOffset % _width - destinationOffset % _width) + Math.Abs(originOffset / _width - destinationOffset / _width);
        return TCost.CreateChecked(distance);
    }
}
