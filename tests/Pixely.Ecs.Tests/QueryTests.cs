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
        foreach ((ReadOnlySpan<Entity> entities, Span<Position> positions, ReadOnlySpan<Velocity> velocities) in world.Query<Movement>())
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X;
                positions[i].Y += velocities[i].Y;
                visited.Add(entities[i]);
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

        Assert.That(CollectEntities(world.Query<UnfrozenPositions>()), Is.EquivalentTo(new[] { free }));
    }

    [Test]
    public void WithRequiresComponentWithoutReadingIt()
    {
        World world = new World();
        Entity player = world.Create();
        world.Add(player, new Player());
        world.Create();

        List<Entity> visited = [];
        foreach (Columns<ReadOnlySpan<Entity>> columns in world.Query<PlayerEntities>())
        {
            visited.AddRange(columns.Item1.ToArray());
        }

        Assert.That(visited, Is.EquivalentTo(new[] { player }));
    }

    [Test]
    public void QuerySeesArchetypesCreatedAfterItsFirstUse()
    {
        World world = new World();
        Entity first = world.Create();
        world.Add(first, new Position());
        Assert.That(CollectEntities(world.Query<UnfrozenPositions>()), Has.Count.EqualTo(1));

        Entity second = world.Create();
        world.Add(second, new Name("late"));
        world.Add(second, new Position());

        Assert.That(CollectEntities(world.Query<UnfrozenPositions>()), Is.EquivalentTo(new[] { first, second }));
    }

    [Test]
    public void StructuralChangesThrowWhileQueryRuns()
    {
        World world = new World();
        Entity entity = CreateMoving(world, new Position(), new Velocity());

        foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> _ in world.Query<Movement>())
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
        foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> _ in world.Query<Movement>())
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

        foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> _ in world.Query<Movement>())
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
            foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> _ in world.Query<Movement>())
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
        ColumnsEnumerator<Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>> first = world.Query<Movement>().GetEnumerator();
        ColumnsEnumerator<Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>> copy = first;
        first.Dispose();
        copy.Dispose();

        ColumnsEnumerator<Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>> next = world.Query<Movement>().GetEnumerator();
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
    public void QueryWithAGenericBaseReadsItsSpans()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Mass(3));
        Entity other = world.Create();
        world.Add(other, new Name("other"));

        List<float> masses = [];
        foreach (Columns<ReadOnlySpan<Mass>> columns in world.Query<Masses>())
        {
            foreach (Mass mass in columns.Item1)
            {
                masses.Add(mass.Value);
            }
        }

        foreach (Columns<Span<Name>> columns in world.Query<Writable<Name>>())
        {
            columns.Item1[0] = new Name("written");
        }

        Assert.That(masses, Is.EqualTo(new[] { 3f }));
        Assert.That(world.Get<Name>(other), Is.EqualTo(new Name("written")));
    }

    [Test]
    public void WorldReturnsOneInstanceOfEachQuery()
    {
        World world = new World();
        Movement first = world.Query<Movement>();
        Movement second = world.Query<Movement>();

        Assert.That(second, Is.SameAs(first));
        Assert.That(new World().Query<Movement>(), Is.Not.SameAs(first));
    }

    [Test]
    public void QueryCreatedWithNewThrowsWhenEnumerated()
    {
        Movement movement = new Movement();

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> _ in movement)
            {
            }
        });
    }

    [Test]
    public void QueryRequiringNoComponentThrows()
    {
        Assert.Throws<InvalidOperationException>(() => new World().Query<EntitiesOnly>());
    }

    [Test]
    public void QueryRequiringAndExcludingAComponentThrows()
    {
        Assert.Throws<InvalidOperationException>(() => new World().Query<ExcludedPositions>());
    }

    private static Entity CreateMoving(World world, Position position, Velocity velocity)
    {
        Entity entity = world.Create();
        world.Add(entity, position);
        world.Add(entity, velocity);
        return entity;
    }

    private static List<Entity> CollectEntities(Query<ReadOnlySpan<Entity>, ReadOnlySpan<Position>> query)
    {
        List<Entity> entities = [];
        foreach ((ReadOnlySpan<Entity> matched, _) in query)
        {
            entities.AddRange(matched.ToArray());
        }

        return entities;
    }
}
