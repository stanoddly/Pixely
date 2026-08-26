using System.Numerics;

namespace Pixely.PathFinding.Benchmarks;

internal sealed class SearchScenario<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
{
    private readonly SyntheticGridGraph<TIndex, TCost> _graph;
    private readonly IndexedPathSearch<TIndex, TCost> _search = new IndexedPathSearch<TIndex, TCost>();
    private readonly TCost[] _costs;
    private readonly TIndex[] _predecessors;
    private readonly List<TIndex> _path;
    private readonly ManhattanHeuristic<TIndex, TCost> _heuristic;
    private readonly TIndex _destination;
    private readonly TCost _maximumCost;
    private readonly bool _expectedFound;

    internal SearchScenario(int width, int height, GridTopology topology, int maximumCost)
    {
        _graph = SyntheticGridGraph<TIndex, TCost>.Create(width, height, topology);
        _costs = new TCost[_graph.NodeCount];
        _predecessors = new TIndex[_graph.NodeCount];
        _path = new List<TIndex>(width + height);
        _heuristic = new ManhattanHeuristic<TIndex, TCost>(width);
        _destination = TIndex.CreateChecked(_graph.NodeCount - 1);
        _maximumCost = TCost.CreateChecked(maximumCost);
        _expectedFound = topology != GridTopology.Partitioned;
    }

    internal int ExpandTree()
    {
        _search.ExpandTree(_graph, TIndex.Zero, _costs, _predecessors);
        return ChecksumTree();
    }

    internal int ExpandTreeLimited()
    {
        _search.ExpandTree(_graph, TIndex.Zero, _costs, _predecessors, _maximumCost);
        return ChecksumTree();
    }

    internal int FindPathDijkstra()
    {
        PathResult result = _search.FindPath(_graph, TIndex.Zero, _destination, _path);
        ValidateResult(result);
        return ChecksumPath(result);
    }

    internal int FindPathAStar()
    {
        PathResult result = _search.FindPath(_graph, TIndex.Zero, _destination, _path, _heuristic);
        ValidateResult(result);
        return ChecksumPath(result);
    }

    internal void Prime()
    {
        ExpandTree();
        FindPathDijkstra();
        FindPathAStar();
    }

    private int ChecksumTree()
    {
        int destinationOffset = _graph.NodeCount - 1;
        return HashCode.Combine(_costs[destinationOffset].GetHashCode(), _predecessors[destinationOffset].GetHashCode());
    }

    private int ChecksumPath(PathResult result)
    {
        int last = _path.Count == 0 ? -1 : int.CreateChecked(_path[^1]);
        return HashCode.Combine((int)result, _path.Count, last);
    }

    private void ValidateResult(PathResult result)
    {
        PathResult expected = _expectedFound ? PathResult.Found : PathResult.NotFound;
        if (result != expected)
        {
            throw new InvalidOperationException($"Expected {expected}, but received {result}.");
        }
    }
}
