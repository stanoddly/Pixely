using Arch.Buffer;
using Arch.Core;
using BenchmarkDotNet.Attributes;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Each operation creates and disposes its world, so its cost is part of the measurement. Pixely's spawns wait for the scheduler's sync point, so
// Arch records its creations in a CommandBuffer and plays them back.
[Config(typeof(BenchmarkConfiguration))]
public class CreationBenchmarks
{
    internal sealed class SpawnSystem(MovingSpawner spawner, int count, bool ensureRoom) : ISystem
    {
        public void Update()
        {
            if (ensureRoom)
            {
                spawner.EnsureRoomFor(count);
            }

            for (int i = 0; i < count; i++)
            {
                spawner.Spawn(new Position(i, i), new Velocity(1, 1));
            }
        }
    }

    // Its query names the spawned components, so a sync point before it spawns the entities it counts.
    internal sealed class CountSystem(Movement movement) : ISystem
    {
        public int Count { get; private set; }

        public void Update()
        {
            int count = 0;
            foreach (Columns<Span<Position>, ReadOnlySpan<Velocity>> columns in movement)
            {
                count += columns.Count;
            }

            Count = count;
        }
    }

    private static readonly ComponentType[] s_movingTypes = [Component<Position>.ComponentType, Component<Velocity>.ComponentType];

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [Benchmark(Baseline = true)]
    public int Arch_CommandBuffer()
    {
        ArchWorld world = ArchWorld.Create();
        CommandBuffer buffer = new CommandBuffer();
        for (int i = 0; i < Count; i++)
        {
            ArchEntity entity = buffer.Create(s_movingTypes);
            buffer.Set(in entity, new Position(i, i));
            buffer.Set(in entity, new Velocity(1, 1));
        }

        buffer.Playback(world);
        int size = world.Size;
        buffer.Dispose();
        ArchWorld.Destroy(world);
        return size;
    }

    [Benchmark]
    public int Pixely_Spawn() => Spawn(ensureRoom: false);

    // The system first asks for room for all its spawns, so the archetype grows once at the sync point.
    [Benchmark]
    public int Pixely_Spawn_EnsureRoomFor() => Spawn(ensureRoom: true);

    private int Spawn(bool ensureRoom)
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(builder => new SpawnSystem(builder.Spawner<MovingSpawner>(), Count, ensureRoom));
        CountSystem counter = scheduler.Add(builder => new CountSystem(builder.Query<Movement>()));
        scheduler.Update();
        return counter.Count;
    }
}
