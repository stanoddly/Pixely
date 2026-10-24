using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Adds a component to every entity and removes it again, so each operation leaves the worlds as it found them. Pixely moves no value for the tag
// Frozen, while Arch stores it like any component, so the Health case compares moving a component with data.
[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class StructuralChangeBenchmarks
{
    private World _pixely = null!;
    private Entity[] _pixelyEntities = [];
    private ArchWorld _arch = null!;
    private ArchEntity[] _archEntities = [];

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_pixely, _pixelyEntities) = Worlds.CreatePixely(Count);
        (_arch, _archEntities) = Worlds.CreateArch(Count);
    }

    [GlobalCleanup]
    public void Cleanup() => ArchWorld.Destroy(_arch);

    [Benchmark(Baseline = true), BenchmarkCategory("Tag")]
    public void Arch_Tag()
    {
        foreach (ArchEntity entity in _archEntities)
        {
            _arch.Add(entity, new Frozen());
        }

        foreach (ArchEntity entity in _archEntities)
        {
            _arch.Remove<Frozen>(entity);
        }
    }

    [Benchmark, BenchmarkCategory("Tag")]
    public void Pixely_Tag()
    {
        foreach (Entity entity in _pixelyEntities)
        {
            _pixely.Add(entity, new Frozen());
        }

        foreach (Entity entity in _pixelyEntities)
        {
            _pixely.Remove<Frozen>(entity);
        }
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Data")]
    public void Arch_Data()
    {
        foreach (ArchEntity entity in _archEntities)
        {
            _arch.Add(entity, new Health(100));
        }

        foreach (ArchEntity entity in _archEntities)
        {
            _arch.Remove<Health>(entity);
        }
    }

    [Benchmark, BenchmarkCategory("Data")]
    public void Pixely_Data()
    {
        foreach (Entity entity in _pixelyEntities)
        {
            _pixely.Add(entity, new Health(100));
        }

        foreach (Entity entity in _pixelyEntities)
        {
            _pixely.Remove<Health>(entity);
        }
    }
}
