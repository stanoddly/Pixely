using Pixely.DependencyInjection;

namespace Pixely.Ecs.Tests;

// A world as a game gets it from a ServiceCollection with AddEcs. Tests change it through systems of Scheduler and read it as code outside systems
// does, through the lookups of the components in Components.cs and the queries there that only read. configure registers more before the build.
internal sealed class TestWorld : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestWorld(Action<ServiceCollection>? configure = null)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddReadOnlyComponentLookup<Position>();
        services.AddReadOnlyComponentLookup<Velocity>();
        services.AddReadOnlyComponentLookup<Name>();
        services.AddReadOnlyComponentLookup<Mass>();
        services.AddReadOnlyComponentLookup<Player>();
        services.AddReadOnlyComponentLookup<Frozen>();
        services.AddReadOnlyComponentLookup<Dead>();
        services.AddQuery<MovingEntities>();
        services.AddQuery<UnfrozenPositions>();
        services.AddQuery<PlayerEntities>();
        services.AddQuery<Corpses>();
        services.AddQuery<Positions>();
        services.AddQuery<Masses>();
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();
        Scheduler = _provider.GetRequiredService<SystemScheduler>();
    }

    public SystemScheduler Scheduler { get; }

    public T Get<T>() where T : class => _provider.GetRequiredService<T>();

    public ReadOnlyComponentLookup<T> Lookup<T>() where T : struct => Get<ReadOnlyComponentLookup<T>>();

    // Each of the methods below updates the scheduler once with a system that makes the change, so the scheduler's other systems run too.
    public Entity Spawn<T1>(T1 component1) where T1 : struct => Scheduler.RunOnce(builder => builder.Spawner<Spawner<T1>>(), spawner => spawner.Spawn(component1));

    public Entity Spawn<T1, T2>(T1 component1, T2 component2) where T1 : struct where T2 : struct =>
        Scheduler.RunOnce(builder => builder.Spawner<Spawner<T1, T2>>(), spawner => spawner.Spawn(component1, component2));

    public Entity Spawn<T1, T2, T3>(T1 component1, T2 component2, T3 component3) where T1 : struct where T2 : struct where T3 : struct =>
        Scheduler.RunOnce(builder => builder.Spawner<Spawner<T1, T2, T3>>(), spawner => spawner.Spawn(component1, component2, component3));

    public void AddComponent<T>(Entity entity, T component) where T : struct => Scheduler.RunOnce(builder => builder.ComponentAdder<T>(), adder => adder.Add(entity, component));

    public void RemoveComponent<T>(Entity entity) where T : struct => Scheduler.RunOnce(builder => builder.ComponentRemover<T>(), remover => remover.Remove(entity));

    public void Destroy(Entity entity) => Scheduler.RunOnce(builder => builder.EntityDestroyer(), destroyer => destroyer.Destroy(entity));

    public void Dispose() => _provider.Dispose();
}
