using Arch.Buffer;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace Pixely.Ecs.Benchmarks;

// Adds a component to every entity and removes it again, so each operation leaves the worlds as it found them. Pixely moves no value for the tag
// Frozen, while Arch stores it like any component, so the Health case compares moving a component with data.
// Pixely's writers apply their changes at the scheduler's sync point, so Arch records its changes in a CommandBuffer and plays them back. Each side
// applies the adds, then the removes.
[Config(typeof(BenchmarkConfiguration))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class StructuralChangeBenchmarks
{
    // Adds T to every entity in one update and removes it in the next.
    internal sealed class AddThenRemoveSystem<T>(Entity[] entities, T value, ComponentAdder<T> adder, ComponentRemover<T> remover) : ISystem where T : struct
    {
        private bool _remove;

        public void Update()
        {
            if (_remove)
            {
                foreach (Entity entity in entities)
                {
                    remover.Remove(entity);
                }
            }
            else
            {
                foreach (Entity entity in entities)
                {
                    adder.Add(entity, value);
                }
            }

            _remove = !_remove;
        }
    }

    // One scheduler per system, so each benchmark updates only its own.
    private SystemScheduler _pixelyTag = null!;
    private SystemScheduler _pixelyData = null!;
    private ArchWorld _arch = null!;
    private ArchEntity[] _archEntities = [];
    private readonly CommandBuffer _archBuffer = new CommandBuffer();

    [Params(10_000, 100_000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        World world = new World();
        Entity[] entities = Worlds.PopulatePixely(world, Count);
        _pixelyTag = new SystemScheduler(world);
        _pixelyTag.Add(builder => new AddThenRemoveSystem<Frozen>(entities, new Frozen(), builder.ComponentAdder<Frozen>(), builder.ComponentRemover<Frozen>()));
        _pixelyData = new SystemScheduler(world);
        _pixelyData.Add(builder => new AddThenRemoveSystem<Health>(entities, new Health(100), builder.ComponentAdder<Health>(), builder.ComponentRemover<Health>()));
        (_arch, _archEntities) = Worlds.CreateArch(Count);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _archBuffer.Dispose();
        ArchWorld.Destroy(_arch);
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Tag")]
    public void Arch_Tag()
    {
        foreach (ArchEntity entity in _archEntities)
        {
            _archBuffer.Add(in entity, new Frozen());
        }

        _archBuffer.Playback(_arch);
        foreach (ArchEntity entity in _archEntities)
        {
            _archBuffer.Remove<Frozen>(in entity);
        }

        _archBuffer.Playback(_arch);
    }

    [Benchmark, BenchmarkCategory("Tag")]
    public void Pixely_Tag()
    {
        _pixelyTag.Update();
        _pixelyTag.Update();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Data")]
    public void Arch_Data()
    {
        foreach (ArchEntity entity in _archEntities)
        {
            _archBuffer.Add(in entity, new Health(100));
        }

        _archBuffer.Playback(_arch);
        foreach (ArchEntity entity in _archEntities)
        {
            _archBuffer.Remove<Health>(in entity);
        }

        _archBuffer.Playback(_arch);
    }

    [Benchmark, BenchmarkCategory("Data")]
    public void Pixely_Data()
    {
        _pixelyData.Update();
        _pixelyData.Update();
    }
}
