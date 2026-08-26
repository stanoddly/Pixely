using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace Pixely.PathFinding.Benchmarks;

public sealed class BenchmarkConfiguration : ManualConfig
{
    public BenchmarkConfiguration()
    {
        AddColumn(StatisticColumn.P95);
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
