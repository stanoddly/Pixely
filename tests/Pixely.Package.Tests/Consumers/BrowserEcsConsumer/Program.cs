using Pixely.DependencyInjection;
using Pixely.Ecs;

namespace BrowserEcsConsumer;

// Runs Pixely.Ecs under Mono, the browser's default runtime, because it uses generic type arguments that allow ref structs, such as Query<Span<T>>. No frame loop
// or SDL is needed, so a plain Main updates the scheduler and prints what code outside systems reads. PackageIntegrationTests expects these exact lines,
// worked out by hand from the rules below, so a difference from the desktop's behavior fails it.
//
// The hero starts at 5 with velocity 2 and health 4, and the rock at 10 with health 2. A body with a velocity moves by it, and one without sinks by 1. Every
// body that is not frozen loses 1 health, and at 0 it is destroyed and counts as a kill. A new kill gives the hero 1 health and takes its velocity away. A
// hero with 1 health freezes, which stops it moving and losing health.
internal static class Program
{
    private const int Updates = 5;

    private static int Main()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddUnique<Score, IReadOnlyScore>(new Score());
        services.AddQuery<Bodies>();
        services.AddReadOnlyComponentLookup<Position, Health>();
        services.AddReadOnlyComponentLookup<Velocity>();
        services.AddReadOnlyComponentLookup<Frozen>();

        // Added against the order the systems declare, so only IAfter and IBefore can make the expected lines come out.
        services.AddSystem<RewardSystem>();
        services.AddSystem<HurtSystem>();
        services.AddSystem<MoveSystem>();
        services.AddSystem<SpawnSystem>();

        using ServiceProvider provider = services.BuildServiceProvider();
        SystemScheduler scheduler = provider.GetRequiredService<SystemScheduler>();
        Bodies bodies = provider.GetRequiredService<Bodies>();
        ReadOnlyComponentLookup<Position, Health> stats = provider.GetRequiredService<ReadOnlyComponentLookup<Position, Health>>();
        ReadOnlyComponentLookup<Velocity> velocities = provider.GetRequiredService<ReadOnlyComponentLookup<Velocity>>();
        ReadOnlyComponentLookup<Frozen> frozen = provider.GetRequiredService<ReadOnlyComponentLookup<Frozen>>();
        ReadOnlyUnique<IReadOnlyScore> score = provider.GetRequiredService<ReadOnlyUnique<IReadOnlyScore>>();

        for (int update = 1; update <= Updates; update++)
        {
            scheduler.Update();

            List<string> lines = [];
            foreach ((ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions) in bodies)
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    ReadOnlyComponentRefs<Position, Health> body = stats[entities[i]];
                    lines.Add($"position {positions[i].Value} health {body.Item2.Value} moving {velocities.Has(entities[i])} frozen {frozen.Has(entities[i])}");
                }
            }

            // The order of archetypes is unspecified, so the lines are sorted to compare them.
            lines.Sort(StringComparer.Ordinal);
            Console.WriteLine($"RESULT update {update} kills {score.Value.Kills}: {string.Join(", ", lines)}");
        }

        return 0;
    }
}

internal record struct Position(int Value);

internal record struct Velocity(int Value);

internal record struct Health(int Value);

internal struct Hero : ITag;

internal struct Frozen : ITag;

internal interface IReadOnlyScore
{
    int Kills { get; }
}

internal sealed class Score : IReadOnlyScore
{
    public int Kills { get; set; }
}

internal sealed class HeroSpawner : Spawner<Position, Velocity, Health>, IWith<Hero>;

internal sealed class Movers : Query<Span<Position>, Optional<ReadOnlySpan<Velocity>>>, IWithout<Frozen>;

internal sealed class Living : Query<ReadOnlySpan<Entity>, Span<Health>>, IWithout<Frozen>;

internal sealed class Heroes : Query<ReadOnlySpan<Entity>>, IWith<Hero>;

internal sealed class Bodies : Query<ReadOnlySpan<Entity>, ReadOnlySpan<Position>>;

internal sealed class SpawnSystem(HeroSpawner heroes, Spawner<Position, Health> rocks) : ISystem, IBefore<MoveSystem>
{
    private bool _spawned;

    public void Update()
    {
        if (!_spawned)
        {
            heroes.Spawn(new Position(5), new Velocity(2), new Health(4));
            rocks.Spawn(new Position(10), new Health(2));
            _spawned = true;
        }
    }
}

internal sealed class MoveSystem(Movers movers) : ISystem
{
    public void Update()
    {
        foreach ((Span<Position> positions, Optional<ReadOnlySpan<Velocity>> velocities) in movers)
        {
            bool moving = velocities.TryGet(out ReadOnlySpan<Velocity> present);
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].Value += moving ? present[i].Value : -1;
            }
        }
    }
}

internal sealed class HurtSystem(Living living, EntityDestroyer destroyer, Unique<Score> score) : ISystem, IAfter<MoveSystem>
{
    public void Update()
    {
        foreach ((ReadOnlySpan<Entity> entities, Span<Health> healths) in living)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                healths[i].Value--;
                if (healths[i].Value == 0)
                {
                    destroyer.Destroy(entities[i]);
                    score.Value.Kills++;
                }
            }
        }
    }
}

internal sealed class RewardSystem(Heroes heroes, ComponentLookup<Health> healths, ReadOnlyUnique<IReadOnlyScore> score, ComponentAdder<Frozen> freezer, ComponentRemover<Velocity> stopper)
    : ISystem, IAfter<HurtSystem>
{
    private int _rewardedKills;

    public void Update()
    {
        bool rewarding = score.Value.Kills > _rewardedKills;
        _rewardedKills = score.Value.Kills;
        foreach (Columns<ReadOnlySpan<Entity>> columns in heroes)
        {
            ReadOnlySpan<Entity> entities = columns.Item1;
            for (int i = 0; i < entities.Length; i++)
            {
                if (rewarding)
                {
                    healths[entities[i]].Value++;
                    stopper.Remove(entities[i]);
                }

                if (healths[entities[i]].Value == 1)
                {
                    freezer.Add(entities[i], new Frozen());
                }
            }
        }
    }
}
