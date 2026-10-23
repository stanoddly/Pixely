namespace Pixely.Ecs.Tests;

public class QueryTests
{
    [Test]
    public void QueryVisitsEveryArchetypeWithTheRequiredComponents()
    {
        World world = new World();
        Entity moving = CreateMoving(world, new Position(0, 0), new Velocity(1, 0));
        Entity movingPlayer = CreateMoving(world, new Position(0, 0), new Velocity(0, 1));
        world.Add(movingPlayer, new Player());
        Entity still = world.Create();
        world.Add(still, new Position(0, 0));

        List<Entity> visited = [];
        foreach (Movement chunk in world.Query<Movement>())
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                chunk.Positions[i].X += chunk.Velocities[i].X;
                chunk.Positions[i].Y += chunk.Velocities[i].Y;
                visited.Add(chunk.Entities[i]);
            }
        }

        Assert.That(visited, Is.EquivalentTo(new[] { moving, movingPlayer }));
        Assert.That(world.Get<Position>(moving), Is.EqualTo(new Position(1, 0)));
        Assert.That(world.Get<Position>(movingPlayer), Is.EqualTo(new Position(0, 1)));
        Assert.That(world.Get<Position>(still), Is.EqualTo(new Position(0, 0)));
    }

    [Test]
    public void WithoutExcludesEntitiesThatHaveTheComponent()
    {
        World world = new World();
        Entity free = world.Create();
        world.Add(free, new Position());
        Entity frozen = world.Create();
        world.Add(frozen, new Position());
        world.Add(frozen, new Frozen());

        Assert.That(Collect<UnfrozenPositions>(world, chunk => chunk.Entities), Is.EquivalentTo(new[] { free }));
    }

    [Test]
    public void WithRequiresComponentWithoutReadingIt()
    {
        World world = new World();
        Entity player = world.Create();
        world.Add(player, new Player());
        world.Create();

        List<Entity> visited = [];
        foreach (PlayerEntities chunk in world.Query<PlayerEntities>())
        {
            visited.AddRange(chunk.Entities.ToArray());
        }

        Assert.That(visited, Is.EquivalentTo(new[] { player }));
    }

    [Test]
    public void QuerySeesArchetypesCreatedAfterItsFirstUse()
    {
        World world = new World();
        Entity first = world.Create();
        world.Add(first, new Position());
        Assert.That(Collect<UnfrozenPositions>(world, chunk => chunk.Entities), Has.Count.EqualTo(1));

        Entity second = world.Create();
        world.Add(second, new Name("late"));
        world.Add(second, new Position());

        Assert.That(Collect<UnfrozenPositions>(world, chunk => chunk.Entities), Is.EquivalentTo(new[] { first, second }));
    }

    [Test]
    public void NestedQueryIsGenerated()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Name("nested"));

        List<string> names = [];
        foreach (Container.NestedNames chunk in world.Query<Container.NestedNames>())
        {
            foreach (Name name in chunk.Names)
            {
                names.Add(name.Value);
            }
        }

        Assert.That(names, Is.EqualTo(new[] { "nested" }));
    }

    [Test]
    public void StructuralChangesThrowWhileQueryRuns()
    {
        World world = new World();
        Entity entity = CreateMoving(world, new Position(), new Velocity());

        foreach (Movement _ in world.Query<Movement>())
        {
            Assert.Throws<InvalidOperationException>(() => world.Add(entity, new Frozen()));
            Assert.Throws<InvalidOperationException>(() => world.Remove<Velocity>(entity));
            Assert.Throws<InvalidOperationException>(() => world.Destroy(entity));
        }
    }

    [Test]
    public void CreateIsAllowedWhileQueryRuns()
    {
        World world = new World();
        CreateMoving(world, new Position(), new Velocity());

        Entity created = default;
        foreach (Movement _ in world.Query<Movement>())
        {
            created = world.Create();
        }

        Assert.That(world.IsAlive(created), Is.True);
    }

    [Test]
    public void StructuralChangesAreAllowedAfterBreakingOutOfQuery()
    {
        World world = new World();
        Entity entity = CreateMoving(world, new Position(), new Velocity());

        foreach (Movement _ in world.Query<Movement>())
        {
            break;
        }

        world.Add(entity, new Frozen());
        Assert.That(world.Has<Frozen>(entity), Is.True);
    }

    [Test]
    public void QueryAllowsStructuralChangesAfterItsBodyThrows()
    {
        World world = new World();
        Entity entity = CreateMoving(world, new Position(), new Velocity());

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (Movement _ in world.Query<Movement>())
            {
                throw new InvalidOperationException("The body failed.");
            }
        });

        Assert.DoesNotThrow(() => world.Destroy(entity));
    }

    [Test]
    public void DisposingCopiesOfAnEnumeratorKeepsTheNextQueryRunning()
    {
        World world = new World();
        Entity entity = CreateMoving(world, new Position(), new Velocity());
        QueryEnumerator<Movement> first = world.Query<Movement>().GetEnumerator();
        QueryEnumerator<Movement> copy = first;
        first.Dispose();
        copy.Dispose();

        QueryEnumerator<Movement> next = world.Query<Movement>().GetEnumerator();
        try
        {
            Assert.Throws<InvalidOperationException>(() => world.Destroy(entity));
        }
        finally
        {
            next.Dispose();
        }

        Assert.DoesNotThrow(() => world.Destroy(entity));
    }

    [Test]
    public void HandWrittenQueryReadsTheSpansAndComponentsOfEachChunk()
    {
        World world = new World();
        Entity still = world.Create();
        world.Add(still, new Position(1, 0));
        Entity moving = CreateMoving(world, new Position(2, 0), new Velocity());
        List<(Entity Entity, bool HasVelocity)> visited = [];

        foreach (ManualPositions chunk in world.Query<ManualPositions>())
        {
            for (int i = 0; i < chunk.Positions.Length; i++)
            {
                chunk.Positions[i].X++;
                visited.Add((chunk.Entities[i], chunk.HasVelocity));
            }
        }

        Assert.That(visited, Is.EquivalentTo(new[] { (still, false), (moving, true) }));
        Assert.That(world.Get<Position>(still).X, Is.EqualTo(2));
        Assert.That(world.Get<Position>(moving).X, Is.EqualTo(3));
    }

    [Test]
    public void QueryRequiringNoComponentThrows()
    {
        World world = new World();

        Assert.Throws<InvalidOperationException>(() => world.Query<EverythingQuery>());
    }

    private static Entity CreateMoving(World world, Position position, Velocity velocity)
    {
        Entity entity = world.Create();
        world.Add(entity, position);
        world.Add(entity, velocity);
        return entity;
    }

    private delegate ReadOnlySpan<Entity> EntitiesSelector<TQuery>(TQuery chunk) where TQuery : allows ref struct;

    private static List<Entity> Collect<TQuery>(World world, EntitiesSelector<TQuery> select) where TQuery : IQuery<TQuery>, allows ref struct
    {
        List<Entity> entities = [];
        foreach (TQuery chunk in world.Query<TQuery>())
        {
            entities.AddRange(select(chunk).ToArray());
        }

        return entities;
    }
}
