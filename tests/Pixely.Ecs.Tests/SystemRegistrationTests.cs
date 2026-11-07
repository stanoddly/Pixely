using System.Reflection;
using Pixely.DependencyInjection;

namespace Pixely.Ecs.Tests;

public class SystemRegistrationTests
{
    public sealed class UpdateLog
    {
        public List<string> Entries { get; } = [];

        public List<object> Systems { get; } = [];
    }

    public sealed class Killer(UpdateLog log, Movement movement, ComponentAdder<Dead> dead) : ISystem
    {
        public void Update()
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

    // Takes its contract before and after a service, which the generated code resolves from the provider.
    public sealed class Cleaner(Corpses corpses, UpdateLog log, EntityDestroyer destroyer) : ISystem
    {
        public void Update()
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

    public sealed class Spawning(IEnumerable<UpdateLog> logs, UpdateLog? log, MovingSpawner spawner) : ISystem
    {
        public void Update()
        {
            log!.Entries.Add(nameof(Spawning) + logs.Count());
            spawner.Spawn(default, default);
        }
    }

    // Spawns the first entities in its first Update, unless the world has them, and leaves the scheduler.
    public sealed class Seeding(MovingEntities moving, MovingSpawner spawner, SystemScheduler scheduler, UpdateLog log) : ISystem
    {
        public void Update()
        {
            log.Entries.Add(nameof(Seeding));
            if (CountMoving(moving) == 0)
            {
                spawner.Spawn(default, default);
            }

            scheduler.Remove(this);
        }
    }

    public sealed class SeededCounter(MovingEntities moving, UpdateLog log) : ISystem, IAfter<Seeding>
    {
        public void Update() => log.Entries.Add(nameof(SeededCounter) + CountMoving(moving));
    }

    // Spawns before Seeding, as a world that already has its entities.
    public sealed class Preloading(MovingSpawner spawner) : ISystem, IBefore<Seeding>
    {
        private bool _spawned;

        public void Update()
        {
            if (!_spawned)
            {
                spawner.Spawn(default, default);
                _spawned = true;
            }
        }
    }

    // Explicit, so the scheduler calls Update through the interface.
    public sealed class ExplicitMover(Movement movement) : ISystem
    {
        void ISystem.Update()
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

    public sealed class First(UpdateLog log) : ISystem
    {
        public void Update()
        {
            log.Entries.Add(nameof(First));
            log.Systems.Add(this);
        }
    }

    public sealed class Second(UpdateLog log) : ISystem
    {
        public void Update()
        {
            log.Entries.Add(nameof(Second));
            log.Systems.Add(this);
        }
    }

    public sealed class StageOpener(UpdateLog log) : ISystem, IBefore<First>
    {
        public void Update() => log.Entries.Add(nameof(StageOpener));
    }

    // A service that needs a system, so the provider creates that system early.
    public sealed class SecondUser(Second second)
    {
        public Second Second { get; } = second;
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
        services.AddReadOnlyComponentLookup<Position>();
        using ServiceProvider provider = services.BuildServiceProvider();
        SystemScheduler scheduler = provider.GetRequiredService<SystemScheduler>();
        // The registered systems run in the update that spawns the entity too, before it exists, so the log starts after it.
        Entity entity = scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => spawner.Spawn(default, default));
        log.Entries.Clear();

        scheduler.Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Killer), nameof(Cleaner) }));
        Assert.That(provider.GetRequiredService<ReadOnlyComponentLookup<Position>>().Has(entity), Is.False);
    }

    [Test]
    public void SystemResolvesCollectionsAndOptionalServicesFromTheProvider()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<Spawning>();
        services.AddQuery<MovingEntities>();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Spawning) + 1 }));
        Assert.That(CountMoving(provider.GetRequiredService<MovingEntities>()), Is.EqualTo(1));
    }

    // Its spawns apply although it removed itself during the update, and a system after it sees them in the same update.
    [Test]
    public void SystemThatRemovesItselfRunsOnceAndItsSpawnsApply()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<SeededCounter>();
        services.AddSystem<Seeding>();
        services.AddQuery<MovingEntities>();
        using ServiceProvider provider = services.BuildServiceProvider();
        SystemScheduler scheduler = provider.GetRequiredService<SystemScheduler>();

        scheduler.Update();
        scheduler.Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Seeding), nameof(SeededCounter) + 1, nameof(SeededCounter) + 1 }));
        Assert.That(CountMoving(provider.GetRequiredService<MovingEntities>()), Is.EqualTo(1));
    }

    [Test]
    public void SeedingSkipsAWorldThatHasItsEntities()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<Seeding>();
        services.AddSystem<Preloading>();
        services.AddQuery<MovingEntities>();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Seeding) }));
        Assert.That(CountMoving(provider.GetRequiredService<MovingEntities>()), Is.EqualTo(1));
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
        stage.AddSystem<Cleaner>();
        ServiceProvider child = stage.BuildServiceProvider();
        SystemScheduler scheduler = root.GetRequiredService<SystemScheduler>();

        scheduler.Update();
        child.Dispose();
        scheduler.Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(Cleaner) }));
    }

    [Test]
    public void AddEcsInAChildOfACollectionWithAWorldThrows()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceCollection stage = root.CreateServiceCollection();

        Assert.Throws<InvalidOperationException>(() => stage.AddEcs());
    }

    [Test]
    public void AddEcsTwiceInOneCollectionRegistersOneWorld()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddEcs();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetServices<World>(), Has.Count.EqualTo(1));
        Assert.That(provider.GetServices<SystemScheduler>(), Has.Count.EqualTo(1));
    }

    // A stage that calls AddEcs owns its world: the next stage starts empty, and the unloaded stage's scheduler leaves the root's updatables.
    [Test]
    public void StageThatCallsAddEcsDropsItsWorldWhenDisposed()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddRegistry<IUpdatable>(static updatable => updatable.UpdateOrder);
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceRegistry<IUpdatable> updatables = root.GetRequiredService<ServiceRegistry<IUpdatable>>();
        ServiceCollection first = root.CreateServiceCollection();
        first.AddEcs();
        ServiceProvider firstStage = first.BuildServiceProvider();
        SystemScheduler firstScheduler = firstStage.GetRequiredService<SystemScheduler>();
        World firstWorld = firstStage.GetRequiredService<World>();
        firstScheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => spawner.Spawn(default, default));

        Assert.That(updatables, Is.EqualTo(new IUpdatable[] { firstScheduler }));

        firstStage.Dispose();
        ServiceCollection second = root.CreateServiceCollection();
        second.AddEcs();
        second.AddQuery<MovingEntities>();
        using ServiceProvider secondStage = second.BuildServiceProvider();

        Assert.That(updatables, Is.EqualTo(new IUpdatable[] { secondStage.GetRequiredService<SystemScheduler>() }));
        Assert.That(secondStage.GetRequiredService<World>(), Is.Not.SameAs(firstWorld));
        Assert.That(CountMoving(secondStage.GetRequiredService<MovingEntities>()), Is.Zero);
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
        services.AddReadOnlyComponentLookup<Position>();
        using ServiceProvider provider = services.BuildServiceProvider();
        SystemScheduler scheduler = provider.GetRequiredService<SystemScheduler>();
        Entity entity = scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => spawner.Spawn(new Position(1, 0), new Velocity(2, 0)));

        scheduler.Update();

        Assert.That(provider.GetRequiredService<ReadOnlyComponentLookup<Position>>()[entity], Is.EqualTo(new Position(3, 0)));
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

    // The provider creates singletons grouped by type, and earlier when another service needs one; the scheduler still follows the AddSystem calls.
    [Test]
    public void SystemsRunInTheOrderOfTheirAddSystemCalls()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton<SecondUser>(static sp => new SecondUser(sp.GetRequiredService<Second>()));
        services.AddEcs();
        services.AddSystem<First>();
        services.AddSystem<Second>();
        services.AddSystem<First>();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(First), nameof(Second), nameof(First) }));
        Assert.That(log.Systems[2], Is.Not.SameAs(log.Systems[0]));
        Assert.That(log.Systems[1], Is.SameAs(provider.GetRequiredService<SecondUser>().Second));
    }

    // Systems join the scheduler while the provider creates its singletons, so even an OnBuilt callback registered before them sees them.
    [Test]
    public void OnBuiltCallbacksSeeEverySystem()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.OnBuilt(static sp => sp.GetRequiredService<SystemScheduler>().Update());
        services.AddSystem<PlainSystem>();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(PlainSystem) }));
    }

    [Test]
    public void SystemsOfAStageRunAfterThoseOfItsParent()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<First>();
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceCollection stage = root.CreateServiceCollection();
        stage.AddSystem<Second>();
        stage.AddSystem<First>();
        using ServiceProvider child = stage.BuildServiceProvider();

        root.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(First), nameof(Second), nameof(First) }));
        Assert.That(log.Systems[2], Is.Not.SameAs(log.Systems[0]));
    }

    // A child collection that adds systems without calling AddEcs shares its parent's scheduler, so its systems are ordered among the parent's.
    [Test]
    public void SystemOfAStageCanRunBeforeOneOfItsParent()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<First>();
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceCollection stage = root.CreateServiceCollection();
        stage.AddSystem<StageOpener>();
        using ServiceProvider child = stage.BuildServiceProvider();

        root.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(StageOpener), nameof(First) }));
    }

    [Test]
    public void AddSystemThroughConditionalAccessIsIntercepted()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection? services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services?.AddSystem<PlainSystem>();
        using ServiceProvider provider = services!.BuildServiceProvider();

        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(PlainSystem) }));
    }

    // The interceptor runs only when the receiver is not null, as the call it replaces would.
    [Test]
    public void ConditionalAccessOnANullReceiverDoesNothing()
    {
        ServiceCollection? services = null;

        Assert.DoesNotThrow(() => services?.AddSystem<Cleaner>());
    }

    // The systems a failed child build added leave the parent's scheduler when the build disposes what it created.
    [Test]
    public void SystemsOfAChildWhoseBuildFailsLeaveTheScheduler()
    {
        UpdateLog log = new UpdateLog();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddEcs();
        services.AddSystem<First>();
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceCollection stage = root.CreateServiceCollection();
        stage.AddSystem<Second>();
        stage.OnBuilt(static _ => throw new InvalidOperationException("The stage failed."));

        Assert.Throws<InvalidOperationException>(() => stage.BuildServiceProvider());
        root.GetRequiredService<SystemScheduler>().Update();

        Assert.That(log.Entries, Is.EqualTo(new[] { nameof(First) }));
    }

    [Test]
    public void AddSystemThatWasNotInterceptedThrows()
    {
        MethodInfo addSystem = typeof(EcsServiceCollectionExtensions).GetMethod(nameof(EcsServiceCollectionExtensions.AddSystem))!.MakeGenericMethod(typeof(Cleaner));

        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => addSystem.Invoke(null, [new ServiceCollection()]))!;

        Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    private static int CountMoving(MovingEntities moving)
    {
        int count = 0;
        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>> columns in moving)
        {
            count += columns.Count;
        }

        return count;
    }
}
