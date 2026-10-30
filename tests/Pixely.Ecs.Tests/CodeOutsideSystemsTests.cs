using Pixely.DependencyInjection;

namespace Pixely.Ecs.Tests;

// Code outside systems, such as a renderer, reads the world through the queries of AddQuery and the lookups of AddReadOnlyComponentLookup.
public class CodeOutsideSystemsTests
{
    public sealed class PositionSpawning(Spawner<Position> spawner, UnfrozenPositions positions) : ISystem
    {
        public UnfrozenPositions Positions { get; } = positions;

        public void Update() => spawner.Spawn(new Position(1, 2));
    }

    [Test]
    public void AddQueryRegistersTheQueryThatSystemsEnumerate()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddSystem<PositionSpawning>();
        services.AddQuery<UnfrozenPositions>();
        using ServiceProvider provider = services.BuildServiceProvider();
        UnfrozenPositions positions = provider.GetRequiredService<UnfrozenPositions>();

        Assert.That(positions, Is.SameAs(provider.GetRequiredService<PositionSpawning>().Positions));
        Assert.That(CollectPositions(positions), Is.Empty);

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(CollectPositions(positions), Is.EqualTo(new[] { new Position(1, 2) }));
    }

    // Only systems write the world, so code outside systems cannot have a query with a Span<T>. The provider creates its singletons when it is built,
    // so building it throws.
    [Test]
    public void WritingQueryOfAddQueryThrowsWhenTheProviderCreatesIt()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddQuery<Movement>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => services.BuildServiceProvider());

        Assert.That(exception.Message, Does.StartWith($"The query {nameof(Movement)} writes through a Span<T>"));
    }

    // Unlike a system's lookup, which reads only during its system's Update.
    [Test]
    public void AddReadOnlyComponentLookupReadsOutsideAnySystemsUpdate()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddReadOnlyComponentLookup<Position>();
        using ServiceProvider provider = services.BuildServiceProvider();
        SystemScheduler scheduler = provider.GetRequiredService<SystemScheduler>();
        ReadOnlyComponentLookup<Position> positions = provider.GetRequiredService<ReadOnlyComponentLookup<Position>>();

        Entity entity = scheduler.RunOnce(builder => builder.Spawner<Spawner<Position>>(), spawner => spawner.Spawn(new Position(1, 2)));

        Assert.That(positions.Has(entity), Is.True);
        Assert.That(positions[entity], Is.EqualTo(new Position(1, 2)));

        scheduler.RunOnce(builder => builder.ComponentLookup<Position>(), lookup => lookup[entity] = new Position(3, 4));

        Assert.That(positions[entity], Is.EqualTo(new Position(3, 4)));

        scheduler.RunOnce(builder => builder.EntityDestroyer(), destroyer => destroyer.Destroy(entity));

        Assert.That(positions.Has(entity), Is.False);
        Assert.Throws<ArgumentException>(() => _ = positions[entity]);
    }

    [Test]
    public void AddReadOnlyComponentLookupOfSeveralComponentsReadsEachOfThem()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddReadOnlyComponentLookup<Position, Velocity>();
        using ServiceProvider provider = services.BuildServiceProvider();
        SystemScheduler scheduler = provider.GetRequiredService<SystemScheduler>();
        ReadOnlyComponentLookup<Position, Velocity> lookup = provider.GetRequiredService<ReadOnlyComponentLookup<Position, Velocity>>();

        (Entity moving, Entity still) = scheduler.RunOnce(builder => (builder.Spawner<MovingSpawner>(), builder.Spawner<Spawner<Position>>()), spawners =>
            (spawners.Item1.Spawn(new Position(1, 2), new Velocity(3, 4)), spawners.Item2.Spawn(new Position(5, 6))));

        ReadOnlyComponentRefs<Position, Velocity> components = lookup[moving];
        Assert.That(lookup.Has(moving), Is.True);
        Assert.That(components.Item1, Is.EqualTo(new Position(1, 2)));
        Assert.That(components.Item2, Is.EqualTo(new Velocity(3, 4)));
        Assert.That(lookup.Has(still), Is.False);
        Assert.Throws<InvalidOperationException>(() => { _ = lookup[still]; });
    }

    private static List<Position> CollectPositions(UnfrozenPositions query)
    {
        List<Position> positions = [];
        foreach ((_, ReadOnlySpan<Position> matched) in query)
        {
            positions.AddRange(matched.ToArray());
        }

        return positions;
    }
}
