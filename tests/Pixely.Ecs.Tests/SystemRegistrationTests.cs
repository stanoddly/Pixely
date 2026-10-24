using System.Reflection;
using Pixely.DependencyInjection;

namespace Pixely.Ecs.Tests;

public class SystemRegistrationTests
{
    public sealed class UpdateLog
    {
        public List<string> Entries { get; } = [];
    }

    public sealed class Killer(UpdateLog log) : ISystem<Movement, ComponentAdder<Dead>>
    {
        public void Update(Movement movement, ComponentAdder<Dead> dead)
        {
            log.Entries.Add(nameof(Killer));
            foreach ((ReadOnlySpan<Entity> entities, _, _) in movement)
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    dead.Add(entities[i], new Dead());
                }
            }
        }
    }

    public sealed class Cleaner(UpdateLog log) : ISystem<Corpses, EntityDestroyer>
    {
        public void Update(Corpses corpses, EntityDestroyer destroyer)
        {
            log.Entries.Add(nameof(Cleaner));
            foreach (Columns<ReadOnlySpan<Entity>> columns in corpses)
            {
                ReadOnlySpan<Entity> entities = columns.Item1;
                for (int i = 0; i < entities.Length; i++)
                {
                    destroyer.Destroy(entities[i]);
                }
            }
        }
    }

    public sealed class Spawning(IEnumerable<UpdateLog> logs, UpdateLog? log) : ISystem<Spawner<MovingBlueprint>>
    {
        public void Update(Spawner<MovingBlueprint> spawner)
        {
            log!.Entries.Add(nameof(Spawning) + logs.Count());
            spawner.Spawn(new MovingBlueprint());
        }
    }

    // Explicit, so the generated code calls Update through the interface.
    public sealed class ExplicitMover : ISystem<Movement>
    {
        void ISystem<Movement>.Update(Movement movement)
        {
            foreach ((_, Span<Position> positions, ReadOnlySpan<Velocity> velocities) in movement)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i].X += velocities[i].X;
                }
            }
        }
    }

    public sealed class PlainSystem(UpdateLog log) : ISystem
    {
        public void Update() => log.Entries.Add(nameof(PlainSystem));
    }

    [Test]
    public void RegisteredSystemsRunInRegistrationOrderWithTheirContract()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<Killer>();
        services.AddSystem<Cleaner>();
        using ServiceProvider provider = services.BuildServiceProvider();
        World world = provider.GetRequiredService<World>();
        Entity entity = world.Create();
        world.Add(entity, new Position());
        world.Add(entity, new Velocity());

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Killer), nameof(Cleaner) }));
        Assert.That(world.IsAlive(entity), Is.False);
    }

    [Test]
    public void SystemResolvesCollectionsAndOptionalServicesFromTheProvider()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<Spawning>();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Spawning) + 1 }));
        Assert.That(CountMoving(provider.GetRequiredService<World>()), Is.EqualTo(1));
    }

    [Test]
    public void DisposingAChildProviderRemovesItsSystems()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceCollection stage = root.CreateServiceCollection();
        stage.AddEcs();
        stage.AddSystem<Cleaner>();
        ServiceProvider child = stage.BuildServiceProvider();
        SystemScheduler scheduler = root.GetRequiredService<SystemScheduler>();

        scheduler.Update();
        child.Dispose();
        scheduler.Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Cleaner) }));
    }

    [Test]
    public void AddEcsRegistersTheSchedulerAsAnUpdatableAtTheDefaultOrder()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddRegistry<IUpdatable>(static updatable => updatable.UpdateOrder);
        services.AddEcs();
        using ServiceProvider provider = services.BuildServiceProvider();
        SystemScheduler scheduler = provider.GetRequiredService<SystemScheduler>();

        Assert.That(provider.GetRequiredService<ServiceRegistry<IUpdatable>>(), Is.EqualTo(new IUpdatable[] { scheduler }));
        Assert.That(((IUpdatable)scheduler).UpdateOrder, Is.Zero);
    }

    [Test]
    public void ExplicitlyImplementedUpdateIsCalled()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddSystem<ExplicitMover>();
        using ServiceProvider provider = services.BuildServiceProvider();
        World world = provider.GetRequiredService<World>();
        Entity entity = world.Create();
        world.Add(entity, new Position(1, 0));
        world.Add(entity, new Velocity(2, 0));

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(world.Get<Position>(entity), Is.EqualTo(new Position(3, 0)));
    }

    [Test]
    public void SystemWithoutQueriesOrWritersGetsServices()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<PlainSystem>();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(PlainSystem) }));
    }

    [Test]
    public void RegisteredSystemResolvesAsItsOwnType()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<Killer>();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<Killer>(), Is.Not.Null);
    }

    [Test]
    public void SchedulerAddOfAnISystemWithTypeArgumentsThatWasNotInterceptedThrows()
    {
        MethodInfo add = typeof(SystemScheduler).GetMethods().Single(method => method.Name == nameof(SystemScheduler.Add) && method.GetParameters().Length == 1).MakeGenericMethod(typeof(Killer));

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => add.Invoke(new SystemScheduler(new World()), [new Killer(new UpdateLog())]))!;

        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void AddSystemThatWasNotInterceptedThrows()
    {
        MethodInfo addSystem = typeof(EcsServiceCollectionExtensions).GetMethod(nameof(EcsServiceCollectionExtensions.AddSystem))!.MakeGenericMethod(typeof(Cleaner));

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => addSystem.Invoke(null, [new ServiceCollection()]))!;

        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    private static int CountMoving(World world)
    {
        int count = 0;
        foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> columns in world.Query<Movement>())
        {
            count += columns.Count;
        }

        return count;
    }
}
