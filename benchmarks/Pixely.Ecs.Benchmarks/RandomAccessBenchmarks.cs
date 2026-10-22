using BenchmarkDotNet.Attributes;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Reads one component of every entity in a shuffled order, which defeats the prefetcher as lookups by entity do in a game.
[Config(typeof(BenchmarkConfiguration))]
public class RandomAccessBenchmarks
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
        int[] order = Enumerable.Range(0, Count).ToArray();
        new Random(42).Shuffle(order);
        _pixelyEntities = order.Select(i => _pixelyEntities[i]).ToArray();
        _archEntities = order.Select(i => _archEntities[i]).ToArray();
    }

    [GlobalCleanup]
    public void Cleanup() => ArchWorld.Destroy(_arch);

    [Benchmark(Baseline = true)]
    public float Arch()
    {
        float sum = 0;
        foreach (ArchEntity entity in _archEntities)
        {
            sum += _arch.Get<Position>(entity).X;
        }

        return sum;
    }

    [Benchmark]
    public float Pixely()
    {
        float sum = 0;
        foreach (Entity entity in _pixelyEntities)
        {
            sum += _pixely.Get<Position>(entity).X;
        }

        return sum;
    }
}
