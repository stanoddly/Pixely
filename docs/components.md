# Components

Pixely provides a lightweight component system for game logic through `Pixely.Componentize`.

## Core Types

### GameWorld

Container for all game objects. Register it and create objects at startup:

```csharp
builder.RegisterType<GameWorld>();

builder.OnStart((GameWorld gameWorld) =>
{
    GameObjectBuilder builder = gameWorld.CreateGameObjectBuilder();
    builder
        .With<MovementComponent>()
        .With<HealthComponent>()
        .Build();
});
```

### GameObject

Entity that holds components, identified by `Handle<GameObject>`:

```csharp
GameObject player = gameWorld.CreateGameObject();
player.Attach<MovementComponent>();
player.Attach(new HealthComponent(100)); // Instance attachment

// Lookup
MovementComponent movement = player.Get<MovementComponent>();
HealthComponent? health = player.TryGet<HealthComponent>();

// Removal
player.Detach<HealthComponent>();
gameWorld.RemoveGameObject(player); // Detaches all components
```

### GameComponent and ComponentBase

There are two base classes for components:

- **`GameComponent`** — standard base. Caches the owner and service provider at attach, exposes `Owner`, `ServiceProvider`, sibling helpers, `World`, and `GetRequiredService`. Provides parameterless `OnAttach()` / `OnReady()` / `OnDetach()` overrides.
- **`ComponentBase`** — minimal base. Lifecycle hooks receive `GameObject` and `ServiceProvider` as parameters. No cached fields.

Use `GameComponent` for most components. Use `ComponentBase` only when you explicitly don't want owner/sibling access — for example, a component that only needs to interact during its lifecycle hooks and caches what it needs in its own fields.

#### GameComponent

```csharp
public class MovementComponent : GameComponent
{
    private Handle<UpdateTag> _updateHandle;

    protected override void OnAttach()
    {
        _updateHandle = GetRequiredService<UpdateSystem>().Add(Update);
    }

    protected override void OnReady()
    {
        // Safe to access siblings here
        HealthComponent health = GetSibling<HealthComponent>();
    }

    protected override void OnDetach()
    {
        GetRequiredService<UpdateSystem>().Remove(_updateHandle);
    }

    private void Update()
    {
        // Called each frame
    }
}
```

#### ComponentBase

```csharp
public class MovementComponent : ComponentBase
{
    private Handle<UpdateTag> _updateHandle;
    private UpdateSystem _updateSystem;

    protected override void OnAttach(GameObject owner, ServiceProvider services)
    {
        // Cache services you need past attach
        _updateSystem = services.GetRequiredService<UpdateSystem>();
        _updateHandle = _updateSystem.Add(Update);
    }

    protected override void OnDetach(GameObject owner, ServiceProvider services)
    {
        _updateSystem.Remove(_updateHandle);
    }

    private void Update()
    {
        // Called each frame
    }
}
```

Lifecycle hooks:

- **`OnAttach`** — component is placed on the GameObject. Set up self-contained state. When using `GameObjectBuilder`, sibling `OnAttach` may not have run yet.
- **`OnReady`** — all siblings are attached and their `OnAttach` has completed. Safe to resolve sibling references and subscribe to sibling events.
- **`OnDetach`** — component is being removed. Clean up subscriptions and resources.

When attaching to a live GameObject via `Attach`, both `OnAttach` and `OnReady` are called immediately in sequence.

## Services Access

`GameComponent` provides `GetRequiredService<T>()` and `GetService<T>()` delegating to the cached `ServiceProvider`. For `ComponentBase`, capture services from the `services` parameter in `OnAttach` and store them in fields.

## Update Registration

Updates are **not automatic**. Components must explicitly register with `UpdateSystem`:

```csharp
private Handle<UpdateTag> _updateHandle;

protected override void OnAttach()
{
    _updateHandle = GetRequiredService<UpdateSystem>().Add(Update);
}

protected override void OnDetach()
{
    GetRequiredService<UpdateSystem>().Remove(_updateHandle);
}
```

The handle must be stored to unregister later.

Registered actions run inside the `UpdateOrders.Default` band, in the order they were added. See
[frame-order.md](frame-order.md) for placing work before or after the framework's own systems.

## Sibling Components

Sibling access requires `GameComponent`:

```csharp
// Get sibling (throws if not found)
HealthComponent health = GetSibling<HealthComponent>();

// Try get sibling (returns null if not found)
HealthComponent? health = TryGetSibling<HealthComponent>();

// Attach/detach siblings
AttachSibling(new BuffComponent());
DetachSibling<BuffComponent>();
```

## GameObjectBuilder

Use `GameObjectBuilder` to create GameObjects with multiple components. This provides a two-phase lifecycle: `OnAttach` runs for all components first, then `OnReady` runs for all, guaranteeing siblings exist during `OnReady`.

Create one builder and reuse it for multiple GameObjects — each `Build` resets the builder's internal state. Avoid creating a new builder per GameObject, as that defeats the purpose of reuse.

```csharp
GameObjectBuilder builder = gameWorld.CreateGameObjectBuilder();

builder
    .With(new TransformComponent { Position = pos })
    .With<AnimatedSpriteComponent>()
    .With<SilhouetteComponent>()
    .Build();

// Reuse the same builder for the next GameObject
builder
    .With(new TransformComponent { Position = otherPos })
    .With<CreatureAnimationComponent>()
    .Build();
```

Extension methods on `GameObjectBuilder` are a convenient way to bundle related components:

```csharp
public static class GameObjectBuilderExtensions
{
    public static GameObjectBuilder WithUnitComponents(this GameObjectBuilder builder, Vector2 position)
    {
        return builder
            .With(new TransformComponent { Position = position })
            .With<AnimatedSpriteComponent>()
            .With<SilhouetteComponent>();
    }
}

// Usage
builder.WithUnitComponents(pos).With<ArcherAIComponent>().Build();
```

## Key Points

- `GameComponent` caches owner and services, providing sibling helpers, `World`, and `GetRequiredService`
- `ComponentBase` receives `GameObject` and `ServiceProvider` as lifecycle parameters; cache what you need in fields
- Use `GameObjectBuilder` when creating GameObjects with interdependent components
- `Attach` on a live GameObject returns the attached component
- Updates require manual registration with `UpdateSystem`
- Always clean up subscriptions in `OnDetach`
