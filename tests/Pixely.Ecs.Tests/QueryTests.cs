namespace Pixely.Ecs.Tests;

public class QueryTests
{
    [Test]
    public void QueryVisitsEveryArchetypeWithTheRequiredComponents()
    {
        using TestWorld world = new TestWorld();
        Entity moving = world.Spawn(new Position(0, 0), new Velocity(1, 0));
        Entity movingPlayer = world.Spawn(new Position(0, 0), new Velocity(0, 1), new Player());
        Entity still = world.Spawn(new Position(0, 0));

        List<Entity> visited = [];
        world.Scheduler.RunOnce(builder => builder.Query<Movement>(), movement =>
        {
            foreach ((ReadOnlySpan<Entity> entities, Span<Position> positions, ReadOnlySpan<Velocity> velocities) in movement)
            {
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i].X += velocities[i].X;
                    positions[i].Y += velocities[i].Y;
                    visited.Add(entities[i]);
                }
            }
        });

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(visited, Is.EquivalentTo(new[] { moving, movingPlayer }));
        Assert.That(positions[moving], Is.EqualTo(new Position(1, 0)));
        Assert.That(positions[movingPlayer], Is.EqualTo(new Position(0, 1)));
        Assert.That(positions[still], Is.EqualTo(new Position(0, 0)));
    }

    [Test]
    public void OptionalSpanIsPresentOnlyInArchetypesWithItsComponent()
    {
        using TestWorld world = new TestWorld();
        Entity moving = world.Spawn(new Position(0, 0), new Velocity(1, 2));
        Entity still = world.Spawn(new Position(0, 0));
        world.Spawn(new Velocity(1, 1));

        List<Entity> visited = [];
        List<Entity> withVelocity = [];
        world.Scheduler.RunOnce(builder => builder.Query<OptionalMovement>(), movement =>
        {
            foreach ((ReadOnlySpan<Entity> entities, Span<Position> positions, Optional<ReadOnlySpan<Velocity>> velocities) in movement)
            {
                visited.AddRange(entities.ToArray());
                if (velocities.TryGet(out ReadOnlySpan<Velocity> present))
                {
                    Assert.That(velocities.HasValue, Is.True);
                    withVelocity.AddRange(entities.ToArray());
                    for (int i = 0; i < positions.Length; i++)
                    {
                        positions[i].X += present[i].X;
                        positions[i].Y += present[i].Y;
                    }
                }
                else
                {
                    Assert.That(velocities.HasValue, Is.False);
                }
            }
        });

        Assert.That(visited, Is.EquivalentTo(new[] { moving, still }));
        Assert.That(withVelocity, Is.EqualTo(new[] { moving }));
        Assert.That(world.Lookup<Position>()[moving], Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Lookup<Position>()[still], Is.EqualTo(new Position(0, 0)));
    }

    [Test]
    public void OptionalSpanWritesTheEntitiesThatHaveItsComponent()
    {
        using TestWorld world = new TestWorld();
        Entity moving = world.Spawn(new Position(), new Velocity());
        world.Spawn(new Position());

        world.Scheduler.RunOnce(builder => builder.Query<Query<ReadOnlySpan<Position>, Optional<Span<Velocity>>>>(), query =>
        {
            foreach ((_, Optional<Span<Velocity>> velocities) in query)
            {
                if (velocities.HasValue)
                {
                    velocities.Value[0] = new Velocity(3, 4);
                }
            }
        });

        Assert.That(world.Lookup<Velocity>()[moving], Is.EqualTo(new Velocity(3, 4)));
    }

    [Test]
    public void ValueOfAnAbsentOptionalSpanThrows()
    {
        Assert.Throws<InvalidOperationException>(() => _ = default(Optional<ReadOnlySpan<Velocity>>).Value);
    }

    [Test]
    public void WithoutExcludesEntitiesThatHaveTheComponent()
    {
        using TestWorld world = new TestWorld();
        Entity free = world.Spawn(new Position());
        Entity frozen = world.Spawn(new Position());
        world.AddComponent(frozen, new Frozen());

        Assert.That(CollectEntities(world.Get<UnfrozenPositions>()), Is.EquivalentTo(new[] { free }));
    }

    [Test]
    public void WithRequiresComponentWithoutReadingIt()
    {
        using TestWorld world = new TestWorld();
        Entity player = world.Spawn(new Position(), new Player());
        world.Spawn(new Position());

        List<Entity> visited = [];
        foreach (Columns<ReadOnlySpan<Entity>> columns in world.Get<PlayerEntities>())
        {
            visited.AddRange(columns.Item1.ToArray());
        }

        Assert.That(visited, Is.EquivalentTo(new[] { player }));
    }

    [Test]
    public void QuerySeesArchetypesCreatedAfterItsFirstUse()
    {
        using TestWorld world = new TestWorld();
        Entity first = world.Spawn(new Position());
        Assert.That(CollectEntities(world.Get<UnfrozenPositions>()), Has.Count.EqualTo(1));

        Entity second = world.Spawn(new Name("late"), new Position());

        Assert.That(CollectEntities(world.Get<UnfrozenPositions>()), Is.EquivalentTo(new[] { first, second }));
    }

    // A structural change waits for a sync point, which throws while code outside systems runs a query, and drops the change.
    [Test]
    public void StructuralChangesThrowWhileQueryRuns()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        int change = 0;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Frozen>(), builder.ComponentRemover<Velocity>(), builder.EntityDestroyer(), (frozen, velocities, destroyer) =>
        {
            switch (change)
            {
                case 0:
                    frozen.Add(entity, new Frozen());
                    break;
                case 1:
                    velocities.Remove(entity);
                    break;
                default:
                    destroyer.Destroy(entity);
                    break;
            }
        }));

        int runs = 0;
        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>> _ in world.Get<MovingEntities>())
        {
            runs++;
            for (change = 0; change < 3; change++)
            {
                Assert.Throws<InvalidOperationException>(world.Scheduler.Update);
            }
        }

        Assert.That(runs, Is.EqualTo(1));
        Assert.That(world.Lookup<Frozen>().Has(entity), Is.False);
        Assert.That(world.Lookup<Velocity>().Has(entity), Is.True);
    }

    // A spawn only reserves its entity until the sync point, so a system spawns while its query runs.
    [Test]
    public void SpawnIsAllowedWhileQueryRuns()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Position(), new Velocity());

        Entity spawned = world.Scheduler.RunOnce(builder => (builder.Query<Movement>(), builder.Spawner<Spawner<Position>>()), contract =>
        {
            Entity created = default;
            foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> _ in contract.Item1)
            {
                created = contract.Item2.Spawn(new Position(1, 2));
            }

            return created;
        });

        Assert.That(world.Lookup<Position>()[spawned], Is.EqualTo(new Position(1, 2)));
    }

    [Test]
    public void StructuralChangesAreAllowedAfterBreakingOutOfQuery()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());

        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>> _ in world.Get<MovingEntities>())
        {
            break;
        }

        world.AddComponent(entity, new Frozen());
        Assert.That(world.Lookup<Frozen>().Has(entity), Is.True);
    }

    [Test]
    public void QueryAllowsStructuralChangesAfterItsBodyThrows()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>> _ in world.Get<MovingEntities>())
            {
                throw new InvalidOperationException("The body failed.");
            }
        });

        Assert.DoesNotThrow(() => world.Destroy(entity));
        Assert.That(world.Lookup<Position>().Has(entity), Is.False);
    }

    [Test]
    public void DisposingCopiesOfAnEnumeratorKeepsTheNextQueryRunning()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        MovingEntities query = world.Get<MovingEntities>();
        ColumnsEnumerator<Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>>> first = query.GetEnumerator();
        ColumnsEnumerator<Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>>> copy = first;
        first.Dispose();
        copy.Dispose();

        ColumnsEnumerator<Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>>> next = query.GetEnumerator();
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
        using TestWorld world = new TestWorld();
        world.Spawn(new Mass(3));
        Entity other = world.Spawn(new Name("other"));

        List<float> masses = [];
        foreach (Columns<ReadOnlySpan<Mass>> columns in world.Get<Masses>())
        {
            foreach (Mass mass in columns.Item1)
            {
                masses.Add(mass.Value);
            }
        }

        world.Scheduler.RunOnce(builder => builder.Query<Writable<Name>>(), names =>
        {
            foreach (Columns<Span<Name>> columns in names)
            {
                columns.Item1[0] = new Name("written");
            }
        });

        Assert.That(masses, Is.EqualTo(new[] { 3f }));
        Assert.That(world.Lookup<Name>()[other], Is.EqualTo(new Name("written")));
    }

    // The systems and the code outside systems of a world share its one instance of each query.
    [Test]
    public void WorldHasOneInstanceOfEachQuery()
    {
        using TestWorld world = new TestWorld();
        UnfrozenPositions first = null!;
        UnfrozenPositions second = null!;
        world.Scheduler.Add(builder => ActionSystem.Of(first = builder.Query<UnfrozenPositions>(), _ => { }));
        world.Scheduler.Add(builder => ActionSystem.Of(second = builder.Query<UnfrozenPositions>(), _ => { }));
        using TestWorld other = new TestWorld();

        Assert.That(second, Is.SameAs(first));
        Assert.That(world.Get<UnfrozenPositions>(), Is.SameAs(first));
        Assert.That(other.Get<UnfrozenPositions>(), Is.Not.SameAs(first));
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
        SystemScheduler scheduler = new SystemScheduler(new World());

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Query<EntitiesOnly>(), _ => { })));
    }

    [Test]
    public void QueryRequiringAndExcludingAComponentThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Query<ExcludedPositions>(), _ => { })));
    }

    [Test]
    public void QueryRequiringOnlyOptionalComponentsThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Query<Query<Optional<ReadOnlySpan<Position>>>>(), _ => { })));
    }

    [Test]
    public void QueryFilteringOnAnOptionalComponentThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Query<OptionalRequiredVelocities>(), _ => { })));
        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Query<OptionalExcludedVelocities>(), _ => { })));
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
