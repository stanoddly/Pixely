using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Pixely.PathFinding.Benchmarks;

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SmallIndexTypeBenchmarks
{
    private SearchScenario<byte, float> _byte = null!;
    private SearchScenario<ushort, float> _ushort = null!;
    private SearchScenario<int, float> _int = null!;
    private SearchScenario<long, float> _long = null!;

    [GlobalSetup]
    public void Setup()
    {
        _byte = Create<byte>();
        _ushort = Create<ushort>();
        _int = Create<int>();
        _long = Create<long>();
    }

    [Benchmark, BenchmarkCategory("ExpandTree")] public int Byte_ExpandTree() => _byte.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int UInt16_ExpandTree() => _ushort.ExpandTree();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")] public int Int32_ExpandTree() => _int.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Int64_ExpandTree() => _long.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Byte_ExpandTreeLimited() => _byte.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int UInt16_ExpandTreeLimited() => _ushort.ExpandTreeLimited();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTreeLimited")] public int Int32_ExpandTreeLimited() => _int.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Int64_ExpandTreeLimited() => _long.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Byte_Dijkstra() => _byte.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int UInt16_Dijkstra() => _ushort.FindPathDijkstra();
    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")] public int Int32_Dijkstra() => _int.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Int64_Dijkstra() => _long.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("AStar")] public int Byte_AStar() => _byte.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int UInt16_AStar() => _ushort.FindPathAStar();
    [Benchmark(Baseline = true), BenchmarkCategory("AStar")] public int Int32_AStar() => _int.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Int64_AStar() => _long.FindPathAStar();

    private static SearchScenario<TIndex, float> Create<TIndex>() where TIndex : unmanaged, System.Numerics.IBinaryInteger<TIndex>, System.Numerics.IMinMaxValue<TIndex>
    {
        SearchScenario<TIndex, float> scenario = new SearchScenario<TIndex, float>(15, 15, GridTopology.Open, 8);
        scenario.Prime();
        return scenario;
    }
}

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class PracticalIndexTypeBenchmarks
{
    private SearchScenario<ushort, float> _ushort = null!;
    private SearchScenario<int, float> _int = null!;
    private SearchScenario<long, float> _long = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ushort = Create<ushort>();
        _int = Create<int>();
        _long = Create<long>();
    }

    [Benchmark, BenchmarkCategory("ExpandTree")] public int UInt16_ExpandTree() => _ushort.ExpandTree();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")] public int Int32_ExpandTree() => _int.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Int64_ExpandTree() => _long.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int UInt16_ExpandTreeLimited() => _ushort.ExpandTreeLimited();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTreeLimited")] public int Int32_ExpandTreeLimited() => _int.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Int64_ExpandTreeLimited() => _long.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int UInt16_Dijkstra() => _ushort.FindPathDijkstra();
    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")] public int Int32_Dijkstra() => _int.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Int64_Dijkstra() => _long.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("AStar")] public int UInt16_AStar() => _ushort.FindPathAStar();
    [Benchmark(Baseline = true), BenchmarkCategory("AStar")] public int Int32_AStar() => _int.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Int64_AStar() => _long.FindPathAStar();

    private static SearchScenario<TIndex, float> Create<TIndex>() where TIndex : unmanaged, System.Numerics.IBinaryInteger<TIndex>, System.Numerics.IMinMaxValue<TIndex>
    {
        SearchScenario<TIndex, float> scenario = new SearchScenario<TIndex, float>(255, 255, GridTopology.Open, 32);
        scenario.Prime();
        return scenario;
    }
}

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CostTypeBenchmarks
{
    private SearchScenario<ushort, byte> _byte = null!;
    private SearchScenario<ushort, ushort> _ushort = null!;
    private SearchScenario<ushort, int> _int = null!;
    private SearchScenario<ushort, Half> _half = null!;
    private SearchScenario<ushort, float> _float = null!;
    private SearchScenario<ushort, double> _double = null!;

    [GlobalSetup]
    public void Setup()
    {
        _byte = Create<byte>();
        _ushort = Create<ushort>();
        _int = Create<int>();
        _half = Create<Half>();
        _float = Create<float>();
        _double = Create<double>();
    }

    [Benchmark, BenchmarkCategory("ExpandTree")] public int Byte_ExpandTree() => _byte.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int UInt16_ExpandTree() => _ushort.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Int32_ExpandTree() => _int.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Half_ExpandTree() => _half.ExpandTree();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")] public int Single_ExpandTree() => _float.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Double_ExpandTree() => _double.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Byte_ExpandTreeLimited() => _byte.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int UInt16_ExpandTreeLimited() => _ushort.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Int32_ExpandTreeLimited() => _int.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Half_ExpandTreeLimited() => _half.ExpandTreeLimited();
    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTreeLimited")] public int Single_ExpandTreeLimited() => _float.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("ExpandTreeLimited")] public int Double_ExpandTreeLimited() => _double.ExpandTreeLimited();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Byte_Dijkstra() => _byte.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int UInt16_Dijkstra() => _ushort.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Int32_Dijkstra() => _int.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Half_Dijkstra() => _half.FindPathDijkstra();
    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")] public int Single_Dijkstra() => _float.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Double_Dijkstra() => _double.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("AStar")] public int Byte_AStar() => _byte.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int UInt16_AStar() => _ushort.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Int32_AStar() => _int.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Half_AStar() => _half.FindPathAStar();
    [Benchmark(Baseline = true), BenchmarkCategory("AStar")] public int Single_AStar() => _float.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Double_AStar() => _double.FindPathAStar();

    private static SearchScenario<ushort, TCost> Create<TCost>() where TCost : unmanaged, System.Numerics.INumber<TCost>, System.Numerics.IMinMaxValue<TCost>
    {
        SearchScenario<ushort, TCost> scenario = new SearchScenario<ushort, TCost>(64, 64, GridTopology.Open, 16);
        scenario.Prime();
        return scenario;
    }
}

[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class LargeIndexTypeBenchmarks
{
    private SearchScenario<int, float> _int = null!;
    private SearchScenario<long, float> _long = null!;

    [GlobalSetup]
    public void Setup()
    {
        _int = Create<int>();
        _long = Create<long>();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("ExpandTree")] public int Int32_ExpandTree() => _int.ExpandTree();
    [Benchmark, BenchmarkCategory("ExpandTree")] public int Int64_ExpandTree() => _long.ExpandTree();
    [Benchmark(Baseline = true), BenchmarkCategory("Dijkstra")] public int Int32_Dijkstra() => _int.FindPathDijkstra();
    [Benchmark, BenchmarkCategory("Dijkstra")] public int Int64_Dijkstra() => _long.FindPathDijkstra();
    [Benchmark(Baseline = true), BenchmarkCategory("AStar")] public int Int32_AStar() => _int.FindPathAStar();
    [Benchmark, BenchmarkCategory("AStar")] public int Int64_AStar() => _long.FindPathAStar();

    private static SearchScenario<TIndex, float> Create<TIndex>() where TIndex : unmanaged, System.Numerics.IBinaryInteger<TIndex>, System.Numerics.IMinMaxValue<TIndex>
    {
        SearchScenario<TIndex, float> scenario = new SearchScenario<TIndex, float>(1024, 1024, GridTopology.Open, 64);
        scenario.Prime();
        return scenario;
    }
}
