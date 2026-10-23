using BenchmarkDotNet.Attributes;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Adds a tag component to every entity and removes it again, so each operation leaves the worlds as it found them.
[Config(typeof(BenchmarkConfiguration))]
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

    [Benchmark(Baseline = true)]
    public void Arch()
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

    [Benchmark]
    public void Pixely()
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
}
