using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pixely.PathFinding.Grids;

namespace Pixely.PathFinding.Benchmarks;

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class GridBenchmarks
{
    private const int Extent = 128;

    private readonly ClearanceGrid _clearance = new ClearanceGrid(new GridGeometry(Extent, Extent));
    private readonly IndexedPathSearch<int, float> _search = new IndexedPathSearch<int, float>();
    private readonly List<int> _path = new List<int>(Extent * 2);
    private bool[] _blocked = [];
    private UniformGridGraph<int, float, NoGridOverlay> _smallAgent;
    private UniformGridGraph<int, float, NoGridOverlay> _largeAgent;
    private GridHeuristic<int, float> _heuristic;
    private int _destination;

    [GlobalSetup]
    public void Setup()
    {
        GridGeometry geometry = _clearance.Geometry;
        _blocked = new bool[geometry.NodeCount];
        for (int index = 0; index < _blocked.Length; index++)
        {
            (int x, int y) = geometry.GetPosition(index);
            _blocked[index] = x % 16 == 8 && y % 32 != 16;
        }

        _clearance.Rebuild(_blocked);
        GridSteps<NoGridOverlay> steps = new GridSteps<NoGridOverlay>(_clearance, GridConnectivity.EightWayNoCornerCutting, default);
        _smallAgent = new UniformGridGraph<int, float, NoGridOverlay>(steps, 1, 1f, MathF.Sqrt(2f));
        _largeAgent = new UniformGridGraph<int, float, NoGridOverlay>(steps, 3, 1f, MathF.Sqrt(2f));
        _heuristic = _smallAgent.GetHeuristic();
        _destination = geometry.NodeCount - 1;
        SmallAgent_AStar();
        LargeAgent_AStar();
        Rebuild();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("AStar")]
    public int SmallAgent_AStar()
    {
        _search.FindPath(_smallAgent, 0, _destination, _path, _heuristic);
        return _path.Count;
    }

    [Benchmark, BenchmarkCategory("AStar")]
    public int LargeAgent_AStar()
    {
        _search.FindPath(_largeAgent, 0, _destination, _path, _heuristic);
        return _path.Count;
    }

    [Benchmark, BenchmarkCategory("Rebuild")]
    public int Rebuild()
    {
        _clearance.Rebuild(_blocked);
        return _clearance.GetClearance(0);
    }
}
