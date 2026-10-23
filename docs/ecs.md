# ECS

`Pixely.Ecs` is an experimental entity component system (ECS). Entities are ids, components are structs stored by type, and a query reads the components of every entity that matches it. A system declares in its constructor what it reads and which structural changes it makes, so the constructor is the system's contract.

It is not in the Pixely package yet. Reference `src/Pixely.Ecs` and, as an analyzer, `src/Pixely.Ecs.Generator`, and allow the generator's interceptors:

```xml
<PropertyGroup>
    <InterceptorsNamespaces>$(InterceptorsNamespaces);Pixely.Ecs.Generated</InterceptorsNamespaces>
</PropertyGroup>

<ItemGroup>
    <ProjectReference Include="..\Pixely\src\Pixely.Ecs\Pixely.Ecs.csproj" />
    <ProjectReference Include="..\Pixely\src\Pixely.Ecs.Generator\Pixely.Ecs.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
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

Systems do not use these calls. They change the world through writers, described below.

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

A query yields one `Movement` per matching archetype. All fields have `Count` items, and index `i` is the same entity in each. A system enumerates a `Query<Movement>` from its constructor; other code enumerates `world.Query<Movement>()`.

While a query runs, `Destroy`, `Add` and `Remove` on the world throw `InvalidOperationException`, because they would move rows under the spans. `Create` is allowed, because a new entity has no components and every query requires one. The query ends when `foreach` ends, including on `break` or an exception.

A query can also implement `IQuery<TSelf>` by hand. It must require a component, or the query throws.

## Systems

A system implements `ISystem`. Its constructor parameters are its contract:

| Parameter | What the system does |
|----|----|
| `Query<TQuery>` | Reads and writes the components of `TQuery`'s fields, and filters on its `[With<T>]` and `[Without<T>]`. |
| `ComponentAdder<T>` | Adds `T` to entities. |
| `ComponentRemover<T>` | Removes `T` from entities. |
| `EntityDestroyer` | Destroys entities. |
| `Spawner<TBlueprint>` | Creates entities with the components of a blueprint. |
| Anything else | Comes from the service provider. |

```csharp
internal sealed class MovementSystem(Query<Movement> movement) : ISystem
{
    public void Update()
    {
        foreach (Movement chunk in movement)
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                chunk.Positions[i].Value += chunk.Velocities[i].Value;
            }
        }
    }
}

internal sealed class DeathSystem(Query<Mortals> mortals, ComponentAdder<Dead> dead) : ISystem
{
    public void Update()
    {
        foreach (Mortals chunk in mortals)
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                if (chunk.Healths[i].Value <= 0)
                {
                    dead.Add(chunk.Entities[i], new Dead());
                }
            }
        }
    }
}
```

Register the world and the systems:

```csharp
services.AddEcs();
services.AddSystem<DeathSystem>();
services.AddSystem<MovementSystem>();
```

- `AddEcs` registers a `World` and a `SystemScheduler`. The scheduler is an `IUpdatable` at the default `UpdateOrder` and updates every system once per frame. A child collection that calls `AddEcs` keeps its parent's world and scheduler.
- `AddSystem<T>` is intercepted by the generator. The generated code creates `T` through the scheduler, which creates the queries and writers and records the contract.
- Systems run in the order of their `AddSystem` calls. A system has no `UpdateOrder`, and it must not also implement `IUpdatable`, or it would run twice.
- A system registered in a stage's collection leaves the scheduler when the stage's provider is disposed.

## Blueprints

A blueprint is a partial struct with `[Blueprint]`. Its fields, or the properties of a positional record struct, are the components of an entity that a `Spawner<TBlueprint>` creates.

```csharp
[Blueprint]
internal partial struct Bullet
{
    public Position Position;
    public Velocity Velocity;
    public Damage Damage;
}

[Blueprint]
internal readonly partial record struct Corpse(Position Position, Dead Dead);

internal sealed class WeaponSystem(Query<Shooters> shooters, Spawner<Bullet> bullets) : ISystem
{
    public void Update()
    {
        foreach (Shooters chunk in shooters)
        {
            for (int i = 0; i < chunk.Count; i++)
            {
                bullets.Spawn(new Bullet { Position = chunk.Positions[i], Velocity = chunk.Aims[i].Velocity, Damage = new Damage(10) });
            }
        }
    }
}
```

A spawned entity goes directly into the archetype of its components, without the moves of `Create` followed by `Add`. `Spawn` returns the entity at once. It is not alive until the sync point, but the system's other writers can already target it.

## Sync points

Writers record their changes, and the scheduler applies them at sync points:

- After the last system of every update.
- Before a system whose queries name a component that an earlier system changed since the last sync point. A query names the components of its fields, `[With<T>]` and `[Without<T>]`. A `Spawner<TBlueprint>` changes every component of the blueprint.
- Before any system with a query, after a system with an `EntityDestroyer` ran since the last sync point, because a destroy changes every component.

At a sync point, the changes apply in system order, and within a system in the order they were recorded. A change to an entity that is no longer alive is skipped, because an earlier change may have destroyed it. `ComponentAdder<T>` replaces `T` when the entity has it by then, and `ComponentRemover<T>` skips an entity without `T`.

So with `DeathSystem`, `MovementSystem` and then a `CleanupSystem` whose query has `[With<Dead>]`:

- `MovementSystem` does not name `Dead`, so it runs without a sync point and does not see this update's deaths.
- A sync point runs before `CleanupSystem`, which sees them in the same update.
- A system before `DeathSystem` sees them in the next update.

Changes through a `Span<T>` field are not structural. Later systems see them at once.

## Without dependency injection

`SystemScheduler.Add` takes a function that creates the system from a `SystemBuilder`. This is what the generated code calls, and it suits tests:

```csharp
World world = new World();
SystemScheduler scheduler = new SystemScheduler(world);
scheduler.Add(builder => new DeathSystem(builder.Query<Mortals>(), builder.ComponentAdder<Dead>()));
scheduler.Add(builder => new MovementSystem(builder.Query<Movement>()));

scheduler.Update();
```

The builder works only while its system is created. `Remove` takes a system out; a system added or removed during `Update` takes effect from the next `Update`.

## Diagnostics

The generator fails the build on these:

| Id | Rule |
|----|------|
| PXECS001 | A query is a non-generic `ref partial struct`, and every type that contains it is partial and non-generic. |
| PXECS002 | Each instance field of a query is `Span<T>` or `ReadOnlySpan<T>` of a struct component, or `ReadOnlySpan<Entity>`. |
| PXECS003 | No component appears in two fields of a query. |
| PXECS004 | A query has at least one field and requires at least one component. |
| PXECS005 | No component is both required and excluded by a query. |
| PXECS006 | A blueprint is a non-generic partial struct that is not a `ref struct`, and every type that contains it is partial and non-generic. |
| PXECS007 | Each member of a blueprint is a struct component, not `Entity`, a `ref struct` or a type parameter. |
| PXECS008 | No component appears in two members of a blueprint. |
| PXECS009 | A blueprint holds at least one component. |
| PXECS010 | `AddSystem<T>` takes a concrete class without type parameters. |
| PXECS011 | The system has exactly one constructor accessible where `AddSystem<T>` is called. |

## Not supported yet

- Reading or writing a component of a given entity through the contract. A system that needs `Get<T>` injects `World`, which the scheduler cannot see.
- Running systems in parallel. The contracts carry what a scheduler would need, but systems run one after another.
- Optional components, change detection, events on add or remove, relations between entities.
- Pixely.Fitness and the Peach architecture still expect Systems to be `IUpdatable`.
