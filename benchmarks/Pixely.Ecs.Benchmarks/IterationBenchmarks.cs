using Arch.Core;
using BenchmarkDotNet.Attributes;
using ArchEntity = Arch.Core.Entity;
using ArchQueryDescription = Arch.Core.QueryDescription;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

[Config(typeof(BenchmarkConfiguration))]
public class IterationBenchmarks
{
    internal sealed class MoveSystem(Movement movement) : ISystem
    {
        public int Visited { get; private set; }

        public void Update()
        {
            int visited = 0;
            foreach ((Span<Position> positions, ReadOnlySpan<Velocity> velocities) in movement)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i].X += velocities[i].X;
                    positions[i].Y += velocities[i].Y;
                }

                visited += positions.Length;
            }

            Visited = visited;
        }
    }

    private readonly ArchQueryDescription _archMovement = new ArchQueryDescription().WithAll<Position, Velocity>();
    private SystemScheduler _pixely = null!;
    private MoveSystem _moveSystem = null!;
    private ArchWorld _arch = null!;

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        World world = new World();
        Worlds.PopulatePixely(world, Count);
        _pixely = new SystemScheduler(world);
        _moveSystem = _pixely.Add(builder => new MoveSystem(builder.Query<Movement>()));
        (_arch, ArchEntity[] _) = Worlds.CreateArch(Count);
    }

    [GlobalCleanup]
    public void Cleanup() => ArchWorld.Destroy(_arch);

    [Benchmark(Baseline = true)]
    public int Arch()
    {
        int visited = 0;
        foreach (ref Chunk chunk in _arch.Query(in _archMovement).GetChunkIterator())
        {
            Span<Position> positions = chunk.GetSpan<Position>();
            Span<Velocity> velocities = chunk.GetSpan<Velocity>();
            int count = chunk.Count;
            for (int i = 0; i < count; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
            }

            visited += count;
        }

        return visited;
    }

    // Only a system writes the world, so the scheduler's update, whose sync point has nothing to apply, is part of the measurement.
    [Benchmark]
    public int Pixely()
    {
        _pixely.Update();
        return _moveSystem.Visited;
    }
}
