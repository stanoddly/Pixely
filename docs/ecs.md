# ECS

`Pixely.Ecs` is an experimental entity component system (ECS). Entities are ids, components are structs stored by type, and a query reads the components of every entity that matches it. A system names the queries it reads and the structural changes it makes in the type arguments of its `ISystem<...>`, which are its contract.

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

`World` holds the entities and their components. A component is any struct. A component that implements `ITag` is a tag: it marks an entity and has no data.

```csharp
public record struct Position(Vector2 Value);
public record struct Velocity(Vector2 Value);
public struct Frozen : ITag;

World world = new World();
Entity entity = world.Create();
world.Add(entity, new Position(Vector2.Zero));
world.Add(entity, new Velocity(Vector2.UnitX));

world.Get<Position>(entity).Value += Vector2.One;
bool frozen = world.Has<Frozen>(entity);
world.Remove<Velocity>(entity);
world.Destroy(entity);
```

- `Get<T>` returns a reference. It stays valid until the next structural change: `Create`, `Destroy`, `Add`, `Remove`, `EnsureCapacity` or a sync point.
- `Add` throws when the entity already has the component. `Remove` and `Get` throw when it does not.
- `Get<T>` of a tag returns one value that all entities share, because a tag stores nothing.
- Every call that takes an entity, except `IsAlive`, throws `ArgumentException` for an entity that is not alive. An `Entity` keeps a version, so a destroyed entity stays dead after its index is reused. An index whose version runs out is never reused. `default(Entity)` is never alive.
- An entity belongs to the world that created it. Another world does not detect it: when an entity of its own has the same index and version, the call acts on that entity.
- `UnsafeGet<T>` is `Get<T>` without its checks, for lookups in hot loops. The entity must be alive in this world and have the component, which only Debug builds assert. Otherwise the behavior is undefined: it may throw, return a reference into another entity's data, or read memory as the wrong type.

Systems do not use these calls. They change the world through writers, described below.

## Storage

Entities with exactly the same component types share an archetype. Adding or removing a component moves the entity to another archetype. Archetypes are created on first use and never removed.

An archetype stores each component type that is not a tag in one array, with a row per entity, so a query reads each component of an archetype as one contiguous span.

- An archetype starts with room for 8 entities. When it runs out, it doubles the room and copies its rows into the new arrays.
- Doubling leaves the old arrays as garbage, and an array over 85 KB goes on the large object heap, which only a gen 2 collection frees. Growing to a known size with `EnsureCapacity` allocates once instead.
- Removing an entity moves the archetype's last entity into its place.
- The room is kept when entities leave.
- A tag has no array, so adding or removing one moves the entity's other components and nothing else. An empty struct without `ITag` still takes one byte per row.

`EnsureCapacity<TBlueprint>(capacity)` grows the archetype of a blueprint's components to hold at least `capacity` entities, and returns its capacity. An archetype that is smaller grows to exactly `capacity`. One that is already larger keeps its size. A level load calls it on the world before spawning or adding many entities. It throws while a query runs, because growing replaces the arrays that the query's spans point into. Systems use `EnsureRoomFor` of a `Spawner<TBlueprint>` instead, described under blueprints.

## Queries

A query is a class that derives from `Query<...>`. Its type arguments are spans, which say what it reads of each entity:

- `Span<T>`: the entity must have `T`, and the query may write it.
- `ReadOnlySpan<T>`: the entity must have `T`, and the query only reads it.
- `ReadOnlySpan<Entity>`: the matched entities.

`IWith<...>` and `IWithout<...>` on the class filter the entities without reading their components:

- `IWith<T>`: the entity must have `T`.
- `IWithout<T>`: the entity must not have `T`.

```csharp
internal sealed class Movement : Query<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>>, IWithout<Frozen>;
```

A query yields the spans of each matching archetype that has entities, and `foreach` deconstructs them into named variables in the order of the type arguments. All spans have the same `Length`, and index `i` is the same entity in each:

```csharp
foreach ((ReadOnlySpan<Entity> entities, Span<Position> positions, ReadOnlySpan<Velocity> velocities) in movement)
{
    for (int i = 0; i < positions.Length; i++)
    {
        positions[i].Value += velocities[i].Value;
    }
}
```

- A query of one span has no deconstruction in C#. Enumerate `Columns<T>` and read its `Item1`. `Columns<...>.Count` is the number of entities in the archetype.
- A query without filters needs no class of its own: `Query<ReadOnlySpan<Velocity>>` works wherever a query class does.
- `Query<...>` takes 1 to 8 spans, and `IWith<...>` and `IWithout<...>` take 1 to 8 components. A query implements at most one `IWith<...>` and one `IWithout<...>`; two of a kind give the compiler error CS8705.
- A query filters on a tag with `IWith<T>` or `IWithout<T>`. A span of a tag is an error, because there is no data to read.

The library cannot get `T` from a span type argument without reflection, so the generator registers every `Span<T>` and `ReadOnlySpan<T>` it sees in a `Query<...>`, wherever it is written, through a module initializer of the assembly. A query can also derive from a generic base, as `Masses : ComponentQuery<Mass>` with `ComponentQuery<T> : Query<ReadOnlySpan<T>>`: the generator registers its spans through the derived class, or through the closed type that an `ISystem<...>` or `world.Query<T>()` names. A type argument that nothing registered throws when its query is created.

A system gets its queries through `Update`. Other code, such as a renderer, gets one from `world.Query<Movement>()`. The world creates one instance of each query class, so the class needs a public parameterless constructor. A query created with `new` belongs to no world and throws when it is enumerated.

The generator checks the spans wherever `Query<...>` is written, and the filters of every query a system names. The world checks a query again when it creates it, and throws `InvalidOperationException` when the query requires no component, reads a component in two spans, reads a tag, or requires and excludes the same component.

While a query runs, `Destroy`, `Add` and `Remove` on the world throw `InvalidOperationException`, because they would move rows under the spans. `Create` is allowed, because a new entity has no components and every query requires one. The query ends when `foreach` ends, including on `break` or an exception.

## Systems

A system implements `ISystem<...>`. Its type arguments are its contract, and the scheduler passes them to its `Update` once per update:

| Type argument | What the system does |
|----|----|
| A query class or `Query<...>` | Reads and writes the components of its spans, and filters on its `IWith<...>` and `IWithout<...>`. |
| `ComponentAdder<T>` | Adds `T` to entities. |
| `ComponentRemover<T>` | Removes `T` from entities. |
| `EntityDestroyer` | Destroys entities. |
| `Spawner<TBlueprint>` | Creates entities with the components of a blueprint. |

The constructor takes only services from the service provider. A system without queries or writers implements `ISystem`, whose `Update` takes nothing.

```csharp
internal sealed class Mortals : Query<ReadOnlySpan<Entity>, ReadOnlySpan<Health>>, IWithout<Dead>;

internal sealed class DeathSystem(ILogger logger) : ISystem<Mortals, ComponentAdder<Dead>>
{
    public void Update(Mortals mortals, ComponentAdder<Dead> dead)
    {
        foreach ((ReadOnlySpan<Entity> entities, ReadOnlySpan<Health> healths) in mortals)
        {
            for (int i = 0; i < entities.Length; i++)
            {
                if (healths[i].Value <= 0)
                {
                    dead.Add(entities[i], new Dead());
                }
            }
        }
    }
}

internal sealed class MovementSystem : ISystem<Movement>
{
    public void Update(Movement movement)
    {
        foreach ((_, Span<Position> positions, ReadOnlySpan<Velocity> velocities) in movement)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].Value += velocities[i].Value;
            }
        }
    }
}
```

- `ISystem<...>` takes 1 to 8 type arguments. A system implements exactly one `ISystem<...>` or `ISystem`.
- The compiler checks `Update` against the type arguments, and the IDE's "Implement interface" writes it.
- A writer records changes only during its system's `Update`. A writer kept past it throws `InvalidOperationException`, so a change cannot apply at a sync point nobody expects. A query has no such check, because renderers enumerate the same instance.

Register the world and the systems:

```csharp
services.AddEcs();
services.AddSystem<DeathSystem>();
services.AddSystem<MovementSystem>();
```

- `AddEcs` registers a `World` and a `SystemScheduler`. The scheduler is an `IUpdatable` at the default `UpdateOrder` and updates every system once per frame. A child collection that calls `AddEcs` keeps its parent's world and scheduler.
- `AddSystem<T>` is intercepted by the generator, also through `?.`. The generated code registers `T` as a singleton of its own type, created with services for its constructor, and adds it to the scheduler with the queries and writers of its `ISystem<...>` when the provider creates it, before any `OnBuilt` callback runs. The scheduler records them as the contract.
- Systems run in the order of their `AddSystem` calls, even when a service needs a system and the provider creates it early, and the systems of a stage run after those of its parent. Adding a system type twice adds two instances.
- A system has no `UpdateOrder`, and it must not also implement `IUpdatable`, or it would run twice. PXECS017 reports one that does.
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

internal sealed class Shooters : Query<ReadOnlySpan<Position>, ReadOnlySpan<Aim>>;

internal sealed class WeaponSystem : ISystem<Shooters, Spawner<Bullet>>
{
    public void Update(Shooters shooters, Spawner<Bullet> bullets)
    {
        foreach ((ReadOnlySpan<Position> positions, ReadOnlySpan<Aim> aims) in shooters)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                bullets.Spawn(new Bullet { Position = positions[i], Velocity = aims[i].Velocity, Damage = new Damage(10) });
            }
        }
    }
}
```

A blueprint can hold a tag. The spawned entity gets the tag, and the generated code writes no value for it.

A spawned entity goes directly into the archetype of its components, without the moves of `Create` followed by `Add`. `Spawn` returns the entity at once. It is not alive until the sync point, but the system's other writers can already target it.

`Spawner<TBlueprint>.EnsureRoomFor(count)` grows the blueprint's archetype at the next sync point, before any of the system's changes, to hold `count` entities more than it holds then. A wave spread over many frames then grows the arrays once. The requests of one system add up for each archetype, and each system's room adds to the entities of the systems before it, so every spawner asks for room for its own spawns. Any change of the system can fill the room: a system that also moves entities into the archetype with `ComponentAdder<T>` or `ComponentRemover<T>` asks for room for them too. A system that throws drops the request with its other changes.

## Sync points

Writers record their changes, and the scheduler applies them at sync points:

- After the last system of every update.
- Before a system whose queries name a component that an earlier system changed since the last sync point. A query names the components of its spans, `IWith<...>` and `IWithout<...>`. A `Spawner<TBlueprint>` changes every component of the blueprint.
- Before any system with a query, after a system with an `EntityDestroyer` ran since the last sync point, because a destroy changes every component.

At a sync point, the changes apply in system order, and within a system in the order they were recorded. A change to an entity that is no longer alive is skipped, because an earlier change may have destroyed it. `ComponentAdder<T>` replaces `T` when the entity has it by then, and `ComponentRemover<T>` skips an entity without `T`.

When a change throws, the exception leaves `Update`, and the rest of that sync point's changes are dropped, including those of later systems. When a system's `Update` throws, the changes recorded since the last sync point are dropped, including those of earlier systems, and the exception leaves the scheduler's `Update`; changes applied at earlier sync points stay. An entity whose spawn is dropped never becomes alive.

So with `DeathSystem`, `MovementSystem` and then a `CleanupSystem` whose query has `IWith<Dead>`:

- `MovementSystem` does not name `Dead`, so it runs without a sync point and does not see this update's deaths.
- A sync point runs before `CleanupSystem`, which sees them in the same update.
- A system before `DeathSystem` sees them in the next update.

Changes through a `Span<T>` are not structural. Later systems see them at once.

## Without dependency injection

`SystemScheduler.Add` takes a system that is already created. For an `ISystem<...>`, the generator intercepts the call like `AddSystem`, so tests need nothing else:

```csharp
World world = new World();
SystemScheduler scheduler = new SystemScheduler(world);
scheduler.Add(new DeathSystem(NullLogger.Instance));
scheduler.Add(new MovementSystem());

scheduler.Update();
```

- An `ISystem` needs no generated code, so `Add` works for it without the generator.
- `Add` returns the system. `Remove` takes a system out; a system added or removed during `Update` takes effect from the next `Update`.
- The generated code calls an overload of `Add` that also takes a function of a `SystemBuilder` and an order from `SystemScheduler.ReserveOrder()`. The function creates the queries and writers once, which the builder records as the contract, and returns the call of `Update` with them. The builder works only during that call. The scheduler runs its systems sorted by order, and `AddSystem` reserves the order when it is called, because the provider does not create singletons in call order.

## Diagnostics

The generator fails the build on these:

| Id | Rule |
|----|------|
| PXECS001 | A query that a system names is a non-abstract class without type parameters, accessible anywhere in its assembly, with a public parameterless constructor. |
| PXECS002 | Each type argument of `Query<...>` is `Span<T>` or `ReadOnlySpan<T>` of a struct component that is not a `ref struct` or a tag, or `ReadOnlySpan<Entity>`. |
| PXECS003 | No component appears in two type arguments of `Query<...>`. |
| PXECS004 | A query requires at least one component through `Span<T>`, `ReadOnlySpan<T>` or `IWith<...>`. |
| PXECS005 | No component is both required and excluded by a query. |
| PXECS006 | A blueprint is a non-generic partial struct that is not a `ref struct` or `file`-local, and every type that contains it is partial, non-generic and not `file`-local. |
| PXECS007 | Each member of a blueprint is a struct component, not `Entity`, a `ref struct` or a type parameter, and a blueprint captures no primary constructor parameter. |
| PXECS008 | No component appears in two members of a blueprint. |
| PXECS009 | A blueprint holds at least one component. |
| PXECS010 | `AddSystem<T>` and `SystemScheduler.Add` take a concrete class without type parameters that the whole assembly can access, so not `private`, `protected` or `file`-local. `SystemScheduler.Add` also takes a type parameter, unless its constraints make it an `ISystem<...>`, which only an intercepted call can add. |
| PXECS011 | The system of `AddSystem<T>` has exactly one `public` or `internal` constructor. The generated code calls it from outside the system. |
| PXECS012 | The constructor has no `ref` or `out` parameter, because the generated code creates every argument for the call. |
| PXECS013 | The constructor takes no query or writer. They go in the system's `ISystem<...>`, which passes them to `Update`. |
| PXECS014 | A tag has no instance fields, auto-properties or captured primary constructor parameters, because a tag has no column and their data would be dropped. |
| PXECS015 | A system implements exactly one of `ISystem` and `ISystem<...>`. |
| PXECS016 | Each type argument of `ISystem<...>` is a query, `ComponentAdder<T>`, `ComponentRemover<T>`, `EntityDestroyer` or `Spawner<TBlueprint>`. |
| PXECS017 | A system does not implement `IUpdatable`, which would update it a second time. |
| PXECS018 | `AddSystem<T>` and `SystemScheduler.Add` are called where the generator can intercept them, and not used as method groups, such as `Action add = services.AddSystem<T>;`, or they would throw. A method group of `SystemScheduler.Add` for an `ISystem` is allowed. |

PXECS002 and PXECS003 are checked wherever `Query<...>` is written, with closed type arguments. PXECS001, PXECS004 and PXECS005 are checked for the queries a system names. `world.Query<T>()` makes the same checks at runtime.

## Not supported yet

- Reading or writing a component of a given entity through the contract. A system that needs `Get<T>` injects `World` in its constructor, which the scheduler cannot see.
- Running systems in parallel. The contracts carry what a scheduler would need, but systems run one after another.
- Optional components, change detection, events on add or remove, relations between entities.
- Pixely.Fitness and the Peach architecture still expect Systems to be `IUpdatable`.
