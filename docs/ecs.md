# ECS

`Pixely.Ecs` is an experimental entity component system (ECS). Entities are ids, components are structs stored by type, and a query reads the components of every entity that matches it.

It is not in the Pixely package yet. Reference `src/Pixely.Ecs` and, as an analyzer, `src/Pixely.Ecs.Generator`:

```xml
<ProjectReference Include="..\Pixely\src\Pixely.Ecs\Pixely.Ecs.csproj" />
<ProjectReference Include="..\Pixely\src\Pixely.Ecs.Generator\Pixely.Ecs.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## World and entities

`World` holds the entities and their components. A component is any struct.

```csharp
public record struct Position(Vector2 Value);
public record struct Velocity(Vector2 Value);
public struct Frozen;

World world = new World();
Entity entity = world.Create();
world.Add(entity, new Position(Vector2.Zero));
world.Add(entity, new Velocity(Vector2.UnitX));

world.Get<Position>(entity).Value += Vector2.One;
bool frozen = world.Has<Frozen>(entity);
world.Remove<Velocity>(entity);
world.Destroy(entity);
```

- `Get<T>` returns a reference. It stays valid until the next structural change: `Create`, `Destroy`, `Add` or `Remove`.
- `Add` throws when the entity already has the component. `Remove` and `Get` throw when it does not.
- Every call that takes an entity, except `IsAlive`, throws `ArgumentException` for an entity that is not alive. An `Entity` keeps a version, so a destroyed entity stays dead after its index is reused. `default(Entity)` is never alive.

## Storage

Entities with exactly the same component types share an archetype. An archetype stores one array per component type, so a query reads each component as one contiguous span. Adding or removing a component moves the entity to another archetype. Archetypes are created on first use and never removed.

## Queries

A query is a `ref partial struct` with the `[Query]` attribute. Its fields say what it reads:

- `Span<T>`: the entity must have `T`, and the query may write it.
- `ReadOnlySpan<T>`: the entity must have `T`, and the query only reads it.
- `ReadOnlySpan<Entity>`: the matched entities.
- `[With<T>]`: the entity must have `T`, without reading it.
- `[Without<T>]`: the entity must not have `T`.

```csharp
[Query]
[Without<Frozen>]
internal ref partial struct Movement
{
    public ReadOnlySpan<Entity> Entities;
    public Span<Position> Positions;
    public ReadOnlySpan<Velocity> Velocities;
}
```

`world.Query<Movement>()` yields one `Movement` per matching archetype. All fields have `Count` items, and index `i` is the same entity in each.

```csharp
internal sealed class MovementSystem(World world) : IUpdatable
{
    public void Update()
    {
        foreach (Movement chunk in world.Query<Movement>())
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                chunk.Positions[i].Value += chunk.Velocities[i].Value;
            }
        }
    }
}
```

There is no system base class. A system is any `IUpdatable`, as `docs/peach-architecture.md` describes.

The generator implements `IQuery<TSelf>` and fails the build when a query is invalid:

| Id | Rule |
|----|------|
| PXECS001 | The query is a non-generic `ref partial struct`, and every type that contains it is partial and non-generic. |
| PXECS002 | Each instance field is `Span<T>` or `ReadOnlySpan<T>` of a struct component, or `ReadOnlySpan<Entity>`. |
| PXECS003 | No component appears in two fields. |
| PXECS004 | The query has at least one field and requires at least one component. |
| PXECS005 | No component is both required and excluded. |

A query can also implement `IQuery<TSelf>` by hand. It must require a component, or `Query` throws.

## Structural changes and command buffers

While a query runs, `Destroy`, `Add` and `Remove` throw `InvalidOperationException`, because they would move rows under the spans. `Create` is allowed, because a new entity has no components and every query requires one. The query ends when `foreach` ends, including on `break` or an exception.

Record the changes in an `EntityCommandBuffer` and apply them after the query:

```csharp
EntityCommandBuffer commands = new EntityCommandBuffer();
foreach (Movement chunk in world.Query<Movement>())
{
    for (int i = 0; i < chunk.Count; i++)
    {
        if (chunk.Velocities[i].Value == Vector2.Zero)
        {
            commands.Add(chunk.Entities[i], new Frozen());
        }
    }
}

commands.Apply(world);
```

`Apply` runs the commands in the order they were recorded, with the same checks as the world, and then clears the buffer. A command that throws stops `Apply`, and the buffer is still cleared.

## Not supported yet

- Creating an entity with several components in one step. Each `Add` moves the entity to another archetype.
- Optional components, change detection, events on add or remove, relations between entities.
- Running systems in parallel.
- Registration through `ServiceCollection`. Register `World` as any other singleton.
