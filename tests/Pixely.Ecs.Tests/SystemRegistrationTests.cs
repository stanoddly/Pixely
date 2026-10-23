using Pixely.DependencyInjection;

namespace Pixely.Ecs.Tests;

public class SystemRegistrationTests
{
    public sealed class UpdateLog
    {
        public List<string> Entries { get; } = [];
    }

    public sealed class Killer(Query<Movement> movement, ComponentAdder<Dead> dead, UpdateLog log) : ISystem
    {
        public void Update()
        {
            log.Entries.Add(nameof(Killer));
            foreach (Movement chunk in movement)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    dead.Add(chunk.Entities[i], new Dead());
                }
            }
        }
    }

    public sealed class Cleaner(Query<Corpses> corpses, EntityDestroyer destroyer, UpdateLog log) : ISystem
    {
        public void Update()
        {
            log.Entries.Add(nameof(Cleaner));
            foreach (Corpses chunk in corpses)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    destroyer.Destroy(chunk.Entities[i]);
                }
            }
        }
    }

    public sealed class Spawning(Spawner<MovingBlueprint> spawner, IEnumerable<UpdateLog> logs, UpdateLog? log) : ISystem
    {
        public void Update()
        {
            log!.Entries.Add(nameof(Spawning) + logs.Count());
            spawner.Spawn(new MovingBlueprint());
        }
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

    private static int CountMoving(World world)
    {
        int count = 0;
        foreach (Movement chunk in world.Query<Movement>())
        {
            count += chunk.Count;
        }

        return count;
    }
}
