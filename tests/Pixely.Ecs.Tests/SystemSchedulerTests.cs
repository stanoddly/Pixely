namespace Pixely.Ecs.Tests;

public class SystemSchedulerTests
{
    private sealed class Killer(Query<Movement> movement, ComponentAdder<Dead> dead) : ISystem
    {
        public void Update()
        {
            foreach (Movement chunk in movement)
            {
                for (int i = 0; i < chunk.Count; i++)
                {
                    dead.Add(chunk.Entities[i], new Dead());
                }
            }
        }
    }

    private sealed class CorpseCounter(Query<Corpses> corpses) : ISystem
    {
        public int Count { get; private set; }

        public void Update()
        {
            Count = 0;
            foreach (Corpses chunk in corpses)
            {
                Count += chunk.Count;
            }
        }
    }

    // Reads the world directly, which a real system does not, to see whether a change has been applied yet.
    private sealed class DeadProbe(World world, Query<Movement> movement, Entity entity) : ISystem
    {
        public bool SawDead { get; private set; }

        public void Update()
        {
            foreach (Movement _ in movement)
            {
            }

            SawDead = world.Has<Dead>(entity);
        }
    }

    private sealed class ActionSystem(Action update) : ISystem
    {
        public void Update() => update();
    }

    [Test]
    public void ChangesApplyAtTheEndOfTheUpdate()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));

        scheduler.Update();

        Assert.That(world.Has<Dead>(entity), Is.True);
    }

    [Test]
    public void LaterSystemThatNamesTheChangedComponentSeesItInTheSameUpdate()
    {
        World world = new World();
        CreateMoving(world);
        CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        CorpseCounter counter = scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));

        scheduler.Update();

        Assert.That(counter.Count, Is.EqualTo(2));
    }

    [Test]
    public void LaterSystemThatDoesNotNameTheChangedComponentRunsBeforeTheSyncPoint()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        DeadProbe probe = scheduler.Add(builder => new DeadProbe(world, builder.Query<Movement>(), entity));

        scheduler.Update();

        Assert.That(probe.SawDead, Is.False);
        Assert.That(world.Has<Dead>(entity), Is.True);
    }

    [Test]
    public void EarlierSystemDoesNotSeeChangesOfALaterOneUntilTheNextUpdate()
    {
        World world = new World();
        CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        CorpseCounter counter = scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));

        scheduler.Update();
        Assert.That(counter.Count, Is.Zero);

        scheduler.Update();
        Assert.That(counter.Count, Is.EqualTo(1));
    }

    [Test]
    public void SpawnedEntityIsReservedUntilTheSyncPointAndOtherCommandsCanTargetIt()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity spawned = default;
        bool aliveDuringUpdate = true;
        Spawner<MovingBlueprint> spawner = null!;
        ComponentAdder<Name> names = null!;
        scheduler.Add(builder =>
        {
            spawner = builder.Spawner<MovingBlueprint>();
            names = builder.ComponentAdder<Name>();
            return new ActionSystem(() =>
            {
                spawned = spawner.Spawn(new MovingBlueprint { Position = new Position(1, 2), Velocity = new Velocity(3, 4) });
                names.Add(spawned, new Name("spawned"));
                aliveDuringUpdate = world.IsAlive(spawned);
            });
        });

        scheduler.Update();

        Assert.That(aliveDuringUpdate, Is.False);
        Assert.That(world.Get<Position>(spawned), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Get<Velocity>(spawned), Is.EqualTo(new Velocity(3, 4)));
        Assert.That(world.Get<Name>(spawned), Is.EqualTo(new Name("spawned")));
    }

    [Test]
    public void RunsOfSpawnsBetweenOtherCommandsApplyInRecordingOrder()
    {
        World world = new World();
        Entity existing = world.Create();
        world.Add(existing, new Position());
        SystemScheduler scheduler = new SystemScheduler(world);
        List<Entity> spawned = [];
        Spawner<MovingBlueprint> spawner = null!;
        ComponentAdder<Name> names = null!;
        scheduler.Add(builder =>
        {
            spawner = builder.Spawner<MovingBlueprint>();
            names = builder.ComponentAdder<Name>();
            return new ActionSystem(() =>
            {
                spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(1, 0) }));
                spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(2, 0) }));
                names.Add(spawned[1], new Name("second"));
                names.Add(existing, new Name("existing"));
                spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(3, 0) }));
            });
        });

        scheduler.Update();

        Assert.That(spawned.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(world.Get<Name>(spawned[1]), Is.EqualTo(new Name("second")));
        Assert.That(world.Has<Name>(spawned[0]), Is.False);
        Assert.That(world.Get<Name>(existing), Is.EqualTo(new Name("existing")));
    }

    [Test]
    public void BlueprintWithPositionalPropertiesSpawnsTheirValues()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity spawned = default;
        Spawner<NamedBlueprint> spawner = null!;
        scheduler.Add(builder =>
        {
            spawner = builder.Spawner<NamedBlueprint>();
            return new ActionSystem(() => spawned = spawner.Spawn(new NamedBlueprint(new Name("a"), new Position(5, 6))));
        });

        scheduler.Update();

        Assert.That(world.Get<Name>(spawned), Is.EqualTo(new Name("a")));
        Assert.That(world.Get<Position>(spawned), Is.EqualTo(new Position(5, 6)));
    }

    [Test]
    public void CommandsOnAnEntityDestroyedEarlierAreSkipped()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        EntityDestroyer destroyer = null!;
        ComponentAdder<Dead> dead = null!;
        ComponentRemover<Position> positions = null!;
        scheduler.Add(builder =>
        {
            destroyer = builder.EntityDestroyer();
            return new ActionSystem(() => destroyer.Destroy(entity));
        });
        scheduler.Add(builder =>
        {
            dead = builder.ComponentAdder<Dead>();
            positions = builder.ComponentRemover<Position>();
            return new ActionSystem(() =>
            {
                dead.Add(entity, new Dead());
                positions.Remove(entity);
                destroyer.Destroy(entity);
            });
        });

        Assert.DoesNotThrow(scheduler.Update);
        Assert.That(world.IsAlive(entity), Is.False);
    }

    [Test]
    public void AddingAPresentComponentReplacesItsValue()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Name("old"));
        SystemScheduler scheduler = new SystemScheduler(world);
        ComponentAdder<Name> names = null!;
        scheduler.Add(builder =>
        {
            names = builder.ComponentAdder<Name>();
            return new ActionSystem(() => names.Add(entity, new Name("new")));
        });

        scheduler.Update();

        Assert.That(world.Get<Name>(entity), Is.EqualTo(new Name("new")));
    }

    [Test]
    public void DestroyerForcesASyncPointBeforeALaterQuery()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Dead());
        SystemScheduler scheduler = new SystemScheduler(world);
        EntityDestroyer destroyer = null!;
        scheduler.Add(builder =>
        {
            destroyer = builder.EntityDestroyer();
            return new ActionSystem(() => destroyer.Destroy(entity));
        });
        CorpseCounter counter = scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));

        scheduler.Update();

        Assert.That(counter.Count, Is.Zero);
    }

    [Test]
    public void RemovedSystemNoLongerRuns()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        int updates = 0;
        ActionSystem system = scheduler.Add(_ => new ActionSystem(() => updates++));

        scheduler.Update();
        scheduler.Remove(system);
        scheduler.Update();

        Assert.That(updates, Is.EqualTo(1));
    }

    [Test]
    public void SystemsRunInTheOrderTheyWereAdded()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        List<int> order = [];
        scheduler.Add(_ => new ActionSystem(() => order.Add(1)));
        scheduler.Add(_ => new ActionSystem(() => order.Add(2)));
        scheduler.Add(_ => new ActionSystem(() => order.Add(3)));

        scheduler.Update();

        Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void BuilderRefusesUseAfterItsSystemIsCreated()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        SystemBuilder builder = null!;
        scheduler.Add(created =>
        {
            builder = created;
            return new ActionSystem(() => { });
        });

        Assert.Throws<InvalidOperationException>(() => builder.ComponentAdder<Dead>());
    }

    private static Entity CreateMoving(World world)
    {
        Entity entity = world.Create();
        world.Add(entity, new Position());
        world.Add(entity, new Velocity());
        return entity;
    }
}
