using BenchmarkDotNet.Attributes;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Reads one component of every entity in a shuffled order, which defeats the prefetcher as lookups by entity do in a game.
[Config(typeof(BenchmarkConfiguration))]
public class RandomAccessBenchmarks
{
    internal sealed class LookupSystem(Entity[] entities) : ISystem<ReadOnlyComponentLookup<Position>>
    {
        public float Sum { get; private set; }

        public void Update(ReadOnlyComponentLookup<Position> positions)
        {
            float sum = 0;
            foreach (Entity entity in entities)
            {
                sum += positions[entity].X;
            }

            Sum = sum;
        }
    }

    private World _pixely = null!;
    private Entity[] _pixelyEntities = [];
    private SystemScheduler _scheduler = null!;
    private LookupSystem _lookupSystem = null!;
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
        _scheduler = new SystemScheduler(_pixely);
        _lookupSystem = _scheduler.Add(new LookupSystem(_pixelyEntities));
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

    // Skips the checks that Arch's Get skips too: bounds, version and the component's presence.
    [Benchmark]
    public float PixelyUnsafe()
    {
        float sum = 0;
        foreach (Entity entity in _pixelyEntities)
        {
            sum += _pixely.UnsafeGet<Position>(entity).X;
        }

        return sum;
    }

    // A system reads the positions through its ReadOnlyComponentLookup, which checks that its Update runs before each Get.
    [Benchmark]
    public float PixelyLookup()
    {
        _scheduler.Update();
        return _lookupSystem.Sum;
    }
}
