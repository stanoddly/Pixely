using Arch.Core;
using BenchmarkDotNet.Attributes;
using ArchEntity = Arch.Core.Entity;
using ArchQueryDescription = Arch.Core.QueryDescription;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

[Config(typeof(BenchmarkConfiguration))]
public class IterationBenchmarks
{
    private readonly ArchQueryDescription _archMovement = new ArchQueryDescription().WithAll<Position, Velocity>();
    private World _pixely = null!;
    private ArchWorld _arch = null!;

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        (_pixely, _) = Worlds.CreatePixely(Count);
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

    [Benchmark]
    public int Pixely()
    {
        int visited = 0;
        foreach ((Span<Position> positions, ReadOnlySpan<Velocity> velocities) in _pixely.Query<Movement>())
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
            }

            visited += positions.Length;
        }

        return visited;
    }
}
