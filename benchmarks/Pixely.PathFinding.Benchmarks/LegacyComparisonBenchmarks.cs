using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using LegacySearch = Pixely.PathFinding.Benchmarks.Legacy.IndexedPathSearch<Pixely.PathFinding.Benchmarks.Legacy.LegacyGridGraph>;

namespace Pixely.PathFinding.Benchmarks;

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class LegacyComparisonBenchmarks
{
    private SearchScenario<int, float> _current = null!;
    private LegacySearchScenario _legacy = null!;

    [GlobalSetup]
    public void Setup()
    {
        _current = new SearchScenario<int, float>(255, 255, GridTopology.Open, 32);
        _current.Prime();
        _legacy = new LegacySearchScenario(255, 255);
        _legacy.Prime();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")]
    public int Legacy_ExpandTree() => _legacy.ExpandTree();

    [Benchmark, BenchmarkCategory("ExpandTree")]
    public int Current_ExpandTree() => _current.ExpandTree();

    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")]
    public int Legacy_Dijkstra() => _legacy.FindPathDijkstra();

    [Benchmark, BenchmarkCategory("Dijkstra")]
    public int Current_Dijkstra() => _current.FindPathDijkstra();

    [Benchmark(Baseline = true), BenchmarkCategory("AStar")]
    public int Legacy_AStar() => _legacy.FindPathAStar();

    [Benchmark, BenchmarkCategory("AStar")]
    public int Current_AStar() => _current.FindPathAStar();
}

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ColdStartBenchmarks
{
    private SyntheticGridGraph<int, float> _currentGraph;
    private Legacy.LegacyGridGraph _legacyGraph;
    private float[] _currentCosts = null!;
    private int[] _currentPredecessors = null!;
    private float[] _legacyCosts = null!;
    private int[] _legacyPredecessors = null!;
    private List<int> _currentPath = null!;
    private List<int> _legacyPath = null!;
    private ManhattanHeuristic<int, float> _currentHeuristic;
    private Legacy.LegacyManhattanHeuristic _legacyHeuristic;
    private int _destination;

    [GlobalSetup]
    public void Setup()
    {
        const int width = 255;
        const int height = 255;
        _currentGraph = SyntheticGridGraph<int, float>.Create(width, height, GridTopology.Open);
        _legacyGraph = Legacy.LegacyGridGraph.Create(width, height);
        int nodeCount = width * height;
        _currentCosts = new float[nodeCount];
        _currentPredecessors = new int[nodeCount];
        _legacyCosts = new float[nodeCount];
        _legacyPredecessors = new int[nodeCount];
        _currentPath = new List<int>(width + height);
        _legacyPath = new List<int>(width + height);
        _currentHeuristic = new ManhattanHeuristic<int, float>(width);
        _legacyHeuristic = new Legacy.LegacyManhattanHeuristic(width);
        _destination = nodeCount - 1;
    }

    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")]
    public int Legacy_ExpandTree()
    {
        LegacySearch search = new LegacySearch();
        search.ExpandTree(_legacyGraph, 0, _legacyCosts, _legacyPredecessors);
        return _legacyPredecessors[_destination];
    }

    [Benchmark, BenchmarkCategory("ExpandTree")]
    public int Current_ExpandTree()
    {
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        search.ExpandTree(_currentGraph, 0, _currentCosts, _currentPredecessors);
        return _currentPredecessors[_destination];
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")]
    public int Legacy_Dijkstra()
    {
        LegacySearch search = new LegacySearch();
        PathResult result = search.FindPath(_legacyGraph, 0, _destination, _legacyPath);
        return HashCode.Combine((int)result, _legacyPath.Count);
    }

    [Benchmark, BenchmarkCategory("Dijkstra")]
    public int Current_Dijkstra()
    {
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        PathResult result = search.FindPath(_currentGraph, 0, _destination, _currentPath);
        return HashCode.Combine((int)result, _currentPath.Count);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("AStar")]
    public int Legacy_AStar()
    {
        LegacySearch search = new LegacySearch();
        PathResult result = search.FindPath(_legacyGraph, 0, _destination, _legacyPath, _legacyHeuristic);
        return HashCode.Combine((int)result, _legacyPath.Count);
    }

    [Benchmark, BenchmarkCategory("AStar")]
    public int Current_AStar()
    {
        IndexedPathSearch<int, float> search = new IndexedPathSearch<int, float>();
        PathResult result = search.FindPath(_currentGraph, 0, _destination, _currentPath, _currentHeuristic);
        return HashCode.Combine((int)result, _currentPath.Count);
    }
}

internal sealed class LegacySearchScenario
{
    private readonly Legacy.LegacyGridGraph _graph;
    private readonly LegacySearch _search = new LegacySearch();
    private readonly float[] _costs;
    private readonly int[] _predecessors;
    private readonly List<int> _path;
    private readonly Legacy.LegacyManhattanHeuristic _heuristic;
    private readonly int _destination;

    internal LegacySearchScenario(int width, int height)
    {
        _graph = Legacy.LegacyGridGraph.Create(width, height);
        _costs = new float[_graph.NodeCount];
        _predecessors = new int[_graph.NodeCount];
        _path = new List<int>(width + height);
        _heuristic = new Legacy.LegacyManhattanHeuristic(width);
        _destination = _graph.NodeCount - 1;
    }

    internal int ExpandTree()
    {
        _search.ExpandTree(_graph, 0, _costs, _predecessors);
        return HashCode.Combine(_costs[_destination], _predecessors[_destination]);
    }

    internal int FindPathDijkstra()
    {
        PathResult result = _search.FindPath(_graph, 0, _destination, _path);
        ValidateResult(result);
        return HashCode.Combine((int)result, _path.Count, _path[^1]);
    }

    internal int FindPathAStar()
    {
        PathResult result = _search.FindPath(_graph, 0, _destination, _path, _heuristic);
        ValidateResult(result);
        return HashCode.Combine((int)result, _path.Count, _path[^1]);
    }

    internal void Prime()
    {
        ExpandTree();
        FindPathDijkstra();
        FindPathAStar();
    }

    private static void ValidateResult(PathResult result)
    {
        if (result != PathResult.Found)
        {
            throw new InvalidOperationException($"Expected {PathResult.Found}, but received {result}.");
        }
    }
}
