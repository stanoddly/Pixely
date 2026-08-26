using System.Numerics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Pixely.PathFinding.Benchmarks;

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ColdIndexTypeBenchmarks
{
    private ColdSearchScenario<byte, float> _byte = null!;
    private ColdSearchScenario<ushort, float> _ushort = null!;
    private ColdSearchScenario<int, float> _int = null!;
    private ColdSearchScenario<long, float> _long = null!;

    [GlobalSetup]
    public void Setup()
    {
        _byte = new ColdSearchScenario<byte, float>(15, 15);
        _ushort = new ColdSearchScenario<ushort, float>(15, 15);
        _int = new ColdSearchScenario<int, float>(15, 15);
        _long = new ColdSearchScenario<long, float>(15, 15);
    }

    [Benchmark, BenchmarkCategory("ExpandTree")] public int Byte_ExpandTree() => _byte.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int UInt16_ExpandTree() => _ushort.ExpandTree();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")] public int Int32_ExpandTree() => _int.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Int64_ExpandTree() => _long.ExpandTree();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Byte_Dijkstra() => _byte.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int UInt16_Dijkstra() => _ushort.FindPathDijkstra();
    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")] public int Int32_Dijkstra() => _int.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Int64_Dijkstra() => _long.FindPathDijkstra();
}

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ColdCostTypeBenchmarks
{
    private ColdSearchScenario<ushort, byte> _byte = null!;
    private ColdSearchScenario<ushort, ushort> _ushort = null!;
    private ColdSearchScenario<ushort, int> _int = null!;
    private ColdSearchScenario<ushort, Half> _half = null!;
    private ColdSearchScenario<ushort, float> _float = null!;
    private ColdSearchScenario<ushort, double> _double = null!;

    [GlobalSetup]
    public void Setup()
    {
        _byte = new ColdSearchScenario<ushort, byte>(64, 64);
        _ushort = new ColdSearchScenario<ushort, ushort>(64, 64);
        _int = new ColdSearchScenario<ushort, int>(64, 64);
        _half = new ColdSearchScenario<ushort, Half>(64, 64);
        _float = new ColdSearchScenario<ushort, float>(64, 64);
        _double = new ColdSearchScenario<ushort, double>(64, 64);
    }

    [Benchmark, BenchmarkCategory("ExpandTree")] public int Byte_ExpandTree() => _byte.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int UInt16_ExpandTree() => _ushort.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Int32_ExpandTree() => _int.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Half_ExpandTree() => _half.ExpandTree();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")] public int Single_ExpandTree() => _float.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Double_ExpandTree() => _double.ExpandTree();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Byte_Dijkstra() => _byte.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int UInt16_Dijkstra() => _ushort.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Int32_Dijkstra() => _int.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Half_Dijkstra() => _half.FindPathDijkstra();
    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")] public int Single_Dijkstra() => _float.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Double_Dijkstra() => _double.FindPathDijkstra();
}

internal sealed class ColdSearchScenario<TIndex, TCost>
    where TIndex : unmanaged, IBinaryInteger<TIndex>, IMinMaxValue<TIndex>
    where TCost : unmanaged, INumber<TCost>, IMinMaxValue<TCost>
{
    private readonly SyntheticGridGraph<TIndex, TCost> _graph;
    private readonly List<TIndex> _path;
    private readonly TIndex _destination;

    internal ColdSearchScenario(int width, int height)
    {
        _graph = SyntheticGridGraph<TIndex, TCost>.Create(width, height, GridTopology.Open);
        _path = new List<TIndex>(width + height);
        _destination = TIndex.CreateChecked(_graph.NodeCount - 1);
    }

    internal int ExpandTree()
    {
        IndexedPathSearch<TIndex, TCost> search = new IndexedPathSearch<TIndex, TCost>();
        TCost[] costs = new TCost[_graph.NodeCount];
        TIndex[] predecessors = new TIndex[_graph.NodeCount];
        search.ExpandTree(_graph, TIndex.Zero, costs, predecessors);
        return HashCode.Combine(costs[^1].GetHashCode(), predecessors[^1].GetHashCode());
    }

    internal int FindPathDijkstra()
    {
        IndexedPathSearch<TIndex, TCost> search = new IndexedPathSearch<TIndex, TCost>();
        PathResult result = search.FindPath(_graph, TIndex.Zero, _destination, _path);
        if (result != PathResult.Found)
        {
            throw new InvalidOperationException($"Expected {PathResult.Found}, but received {result}.");
        }

        return HashCode.Combine((int)result, _path.Count, _path[^1].GetHashCode());
    }
}
