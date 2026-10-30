using BenchmarkDotNet.Attributes;
using Pixely.DependencyInjection;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Reads one component of every entity in a shuffled order, which defeats the prefetcher as lookups by entity do in a game.
[Config(typeof(BenchmarkConfiguration))]
public class RandomAccessBenchmarks
{
    internal sealed class LookupSystem(Entity[] entities, ReadOnlyComponentLookup<Position> positions) : ISystem
    {
        public float Sum { get; private set; }

        public void Update()
        {
            float sum = 0;
            foreach (Entity entity in entities)
            {
                sum += positions[entity].X;
            }

            Sum = sum;
        }
    }

    internal sealed class TwoLookupsSystem(Entity[] entities, ReadOnlyComponentLookup<Position> positions, ReadOnlyComponentLookup<Velocity> velocities) : ISystem
    {
        public float Sum { get; private set; }

        public void Update()
        {
            float sum = 0;
            foreach (Entity entity in entities)
            {
                sum += positions[entity].X + velocities[entity].X;
            }

            Sum = sum;
        }
    }

    internal sealed class PairLookupSystem(Entity[] entities, ReadOnlyComponentLookup<Position, Velocity> lookup) : ISystem
    {
        public float Sum { get; private set; }

        public void Update()
        {
            float sum = 0;
            foreach (Entity entity in entities)
            {
                ReadOnlyComponentRefs<Position, Velocity> components = lookup[entity];
                sum += components.Item1.X + components.Item2.X;
            }

            Sum = sum;
        }
    }

    private ServiceProvider _pixely = null!;
    private Entity[] _pixelyEntities = [];
    // The lookup that AddReadOnlyComponentLookup registers for code outside systems, which reads at any time.
    private ReadOnlyComponentLookup<Position> _positions = null!;
    // One scheduler per system, so each benchmark updates only its own.
    private SystemScheduler _lookupScheduler = null!;
    private SystemScheduler _twoLookupsScheduler = null!;
    private SystemScheduler _pairLookupScheduler = null!;
    private LookupSystem _lookupSystem = null!;
    private TwoLookupsSystem _twoLookupsSystem = null!;
    private PairLookupSystem _pairLookupSystem = null!;
    private ArchWorld _arch = null!;
    private ArchEntity[] _archEntities = [];

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddReadOnlyComponentLookup<Position>();
        _pixely = services.BuildServiceProvider();
        World world = _pixely.GetRequiredService<World>();
        _positions = _pixely.GetRequiredService<ReadOnlyComponentLookup<Position>>();
        _pixelyEntities = Worlds.PopulatePixely(world, Count);
        (_arch, _archEntities) = Worlds.CreateArch(Count);
        int[] order = Enumerable.Range(0, Count).ToArray();
        new Random(42).Shuffle(order);
        _pixelyEntities = order.Select(i => _pixelyEntities[i]).ToArray();
        _archEntities = order.Select(i => _archEntities[i]).ToArray();
        _lookupScheduler = new SystemScheduler(world);
        _lookupSystem = _lookupScheduler.Add(builder => new LookupSystem(_pixelyEntities, builder.ReadOnlyComponentLookup<Position>()));
        _twoLookupsScheduler = new SystemScheduler(world);
        _twoLookupsSystem = _twoLookupsScheduler.Add(builder => new TwoLookupsSystem(_pixelyEntities, builder.ReadOnlyComponentLookup<Position>(), builder.ReadOnlyComponentLookup<Velocity>()));
        _pairLookupScheduler = new SystemScheduler(world);
        _pairLookupSystem = _pairLookupScheduler.Add(builder => new PairLookupSystem(_pixelyEntities, builder.ReadOnlyComponentLookup<Position, Velocity>()));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pixely.Dispose();
        ArchWorld.Destroy(_arch);
    }

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

    // Reads through the lookup of code outside systems, the checked path.
    [Benchmark]
    public float Pixely()
    {
        float sum = 0;
        foreach (Entity entity in _pixelyEntities)
        {
            sum += _positions[entity].X;
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
            sum += _positions.UnsafeGet(entity).X;
        }

        return sum;
    }

    // A system reads the positions through its ReadOnlyComponentLookup, which adds a null test of its world to each Get.
    [Benchmark]
    public float PixelyLookup()
    {
        _lookupScheduler.Update();
        return _lookupSystem.Sum;
    }

    // Reads each position and velocity through two lookups, which find the entity twice.
    [Benchmark]
    public float PixelyTwoLookups()
    {
        _twoLookupsScheduler.Update();
        return _twoLookupsSystem.Sum;
    }

    // Reads each position and velocity through one lookup, which finds the entity once.
    [Benchmark]
    public float PixelyPairLookup()
    {
        _pairLookupScheduler.Update();
        return _pairLookupSystem.Sum;
    }
}
