using BenchmarkDotNet.Attributes;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Each operation creates and disposes its world, so its cost is part of the measurement.
[Config(typeof(BenchmarkConfiguration))]
public class CreationBenchmarks
{
    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [Benchmark(Baseline = true)]
    public int Arch_CreateWithComponents()
    {
        ArchWorld world = ArchWorld.Create();
        for (int i = 0; i < Count; i++)
        {
            world.Create(new Position(i, i), new Velocity(1, 1));
        }

        int size = world.Size;
        ArchWorld.Destroy(world);
        return size;
    }

    [Benchmark]
    public int Arch_CreateThenAdd()
    {
        ArchWorld world = ArchWorld.Create();
        for (int i = 0; i < Count; i++)
        {
            Arch.Core.Entity entity = world.Create();
            world.Add(entity, new Position(i, i));
            world.Add(entity, new Velocity(1, 1));
        }

        int size = world.Size;
        ArchWorld.Destroy(world);
        return size;
    }

    [Benchmark]
    public int Pixely_CreateThenAdd()
    {
        World world = new World();
        Entity last = default;
        for (int i = 0; i < Count; i++)
        {
            last = world.Create();
            world.Add(last, new Position(i, i));
            world.Add(last, new Velocity(1, 1));
        }

        return world.IsAlive(last) ? Count : 0;
    }
}
