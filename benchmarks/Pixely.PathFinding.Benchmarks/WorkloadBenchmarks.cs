using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Pixely.PathFinding.Benchmarks;

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class WorkloadBenchmarks
{
    private SearchScenario<int, float> _open = null!;
    private SearchScenario<int, float> _weighted = null!;
    private SearchScenario<int, float> _partitioned = null!;

    [GlobalSetup]
    public void Setup()
    {
        _open = Create(GridTopology.Open);
        _weighted = Create(GridTopology.Weighted);
        _partitioned = Create(GridTopology.Partitioned);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")] public int Open_ExpandTree() => _open.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Weighted_ExpandTree() => _weighted.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Partitioned_ExpandTree() => _partitioned.ExpandTree();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTreeLimited")] public int Open_ExpandTreeLimited() => _open.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Weighted_ExpandTreeLimited() => _weighted.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Partitioned_ExpandTreeLimited() => _partitioned.ExpandTreeLimited();
    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")] public int Open_Dijkstra() => _open.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Weighted_Dijkstra() => _weighted.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Partitioned_Dijkstra() => _partitioned.FindPathDijkstra();
    [Benchmark(Baseline = true), BenchmarkCategory("AStar")] public int Open_AStar() => _open.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Weighted_AStar() => _weighted.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Partitioned_AStar() => _partitioned.FindPathAStar();

    private static SearchScenario<int, float> Create(GridTopology topology)
    {
        SearchScenario<int, float> scenario = new SearchScenario<int, float>(128, 128, topology, 32);
        scenario.Prime();
        return scenario;
    }
}
