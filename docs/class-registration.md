# Dependency Injection

Pixely's DI container (`Pixely.DependencyInjection`) supports singleton and transient lifetimes. Singletons are instantiated eagerly during `BuildServiceProvider`; transients are constructed lazily each time they are requested. A Roslyn source generator intercepts specific registration overloads at each call site to emit type-safe construction code — several overloads throw at runtime if the generator is not active.

## Overview

- Singleton services have one instance per `ServiceProvider`.
- Transient services create a new instance for each resolution or injection site.
- `BuildServiceProvider` resolves singleton services immediately before returning and records transient factories for later resolution.
- Factory registrations may return `null` to contribute no service. Resolution skips null results.
- `ServiceProvider.CreateServiceCollection()` creates child collections whose providers inherit from the parent.
- `ServiceProvider` itself is automatically registered and resolvable.
- Registration is done through `ServiceCollection`; the built `ServiceProvider` is immutable after `BuildServiceProvider` returns.

## Registration API

### `AddSingleton<T>()` — requires source generator

Registers `T` by constructing it via its single public constructor. Dependencies are resolved from the provider.

```csharp
services.AddSingleton<AudioSystem>();
services.AddSingleton<RenderPipeline>();
```

Use when:
- `T` has exactly one public constructor (or an implicit parameterless constructor).
- Constructor parameters follow the [injected dependency rules](#optional-injected-dependencies).

Constraints: `T` must be a named concrete type at the call site — not a type parameter (see [Source Generator Caveats](#source-generator-caveats)).

---

### `AddSingleton<TService, TImplementation>()` — requires source generator

Registers `TImplementation` under the service type `TService`. `TImplementation` is constructed the same way as the single-type overload.

```csharp
services.AddSingleton<IInputService, KeyboardInputService>();
```

Use when:
- You want to resolve a service by an interface or base class.
- `TImplementation` has exactly one public constructor.

---

### `AddSingleton<TService, TFactory>()` — instance factory, requires source generator

When the second type argument is **not** assignable to the first, the source generator treats it as a factory type. The generator finds the single accessible instance method on `TFactory` that returns `TService`, resolves `TFactory` and the method's parameters from the provider, and calls the method to produce the `TService` instance.

```csharp
services.AddSingleton<AudioManager>();
services.AddSingleton<AudioDevice, AudioManager>();
// equivalent to: services.AddSingleton<AudioDevice>(static sp => sp.GetRequiredService<AudioManager>().CreateDevice())
```

Requirements:
- `TFactory` must already be registered (the generator emits `sp.GetRequiredService<TFactory>()`).
- `TFactory` must have exactly one accessible instance method whose return type is assignable to `TService`. Zero matches produce a compile-time error `GK0003`. Multiple matches produce `GK0004`.
- The method's parameters are resolved as services from the provider, the same way constructor parameters are for `AddSingleton<T>()`.

Use when:
- A registered object has a factory method that produces the desired service.
- The service type is different from the factory type (not assignable).

---

### `AddSingleton<T>(T instance)`

Registers an already-constructed instance. No source generator required.

```csharp
services.AddSingleton(new GameSettings { StartingLives = 3, EnableHints = true });
services.AddSingleton<ILogger>(new ConsoleLogger());
```

Use when:
- You have an existing object to hand to the container.
- The instance requires setup that cannot be expressed as a constructor.

---

### `AddSingleton<T>(Delegate factory)` — requires source generator

Registers a factory delegate whose parameters are resolved according to the [injected dependency rules](#optional-injected-dependencies). The delegate may be a static method group or a lambda.

```csharp
services.AddSingleton<Camera>(Camera.CreateDefault);
services.AddSingleton<RenderConfig>(RenderConfig.Create);
```

If the delegate returns `null`, that registration contributes no service.

Use when:
- Construction logic lives in a static factory method.
- The factory has service dependencies as parameters.

---

### `AddSingleton<T>(Func<ServiceProvider, T?> factory)`

Registers a typed factory that receives the `ServiceProvider` directly. No source generator required.

```csharp
services.AddSingleton<WorldMap>(static sp =>
{
    MapLoader loader = sp.GetRequiredService<MapLoader>();
    return loader.LoadDefault();
});
```

Use when:
- You need full control over construction, including conditional logic.
- The `Delegate` overload is not usable because `T` is a type parameter at the call site.

---

### `AddSingleton<TService, TImpl>(Func<ServiceProvider, TImpl?> factory)`

Registers a typed factory that produces `TImpl` instances under the service type `TService`. Activation and disposal callbacks receive `typeof(TImpl)` rather than `typeof(TService)`. No source generator required.

```csharp
services.AddSingleton<IRenderer, SpriteRenderer>(static sp =>
    new SpriteRenderer(sp.GetRequiredService<GpuDevice>()));
```

Use when:
- The service type is an interface or base class but the concrete implementation type should drive activation/disposal callbacks (e.g. for `EventBus.Subscribe` interface discovery).

---

### `AddTransient<T>()` — requires source generator

Registers `T` as a transient by constructing it via its single public constructor. Dependencies are resolved from the provider each time `T` is requested.

```csharp
services.AddTransient<DomainEventCursor>();
```

Use when:
- Each consumer needs its own instance.
- The instance is short-lived or has per-consumer state.

---

### `AddTransient<TService, TImplementation>()` — requires source generator

Registers `TImplementation` under `TService` as a transient.

```csharp
services.AddTransient<IWidget, HealthBarWidget>();
```

Single-service resolution returns a new instance from the last registration. `GetServices<TService>()` includes transient registrations in registration order and creates fresh transient entries for each collection resolution.

---

### `AddTransient<TService, TFactory>()` — instance factory, requires source generator

When the second type argument is not assignable to the first, the source generator treats it as a factory type, the same way as `AddSingleton<TService, TFactory>()`. The factory method runs for each transient resolution.

---

### `AddTransient<T>(Delegate factory)` — requires source generator

Registers a transient factory delegate whose parameters are resolved as services each time the service is requested.

```csharp
services.AddTransient<ParticleEmitter>(ParticleEmitter.Create);
```

If the delegate returns `null`, that registration contributes no service for that resolution.

---

### `AddTransient<T>(Func<ServiceProvider, T?> factory)`

Registers a typed transient factory that receives the provider directly. No source generator required.

```csharp
services.AddTransient<DomainEventCursor>(static sp =>
    sp.GetRequiredService<IDomainEventStream>().CreateCursor());
```

---

### `AddTransient<TService, TImpl>(Func<ServiceProvider, TImpl?> factory)`

Registers a typed transient factory under an interface or base service type. Activation and disposal callbacks receive `typeof(TImpl)`.

### Optional injected dependencies

Source-generated constructor registrations, delegate factories, instance factory methods, and `OnStart` callbacks use nullable reference annotations to choose how each parameter is resolved:

- Non-nullable and nullable-oblivious reference parameters use `GetRequiredService<T>()` and throw when no service is available.
- Nullable reference parameters use `GetService<T>()` and receive `null` when no service is available.
- `IEnumerable<T>` parameters use `GetServices<T>()` and receive an empty collection when no services are available, regardless of the collection's outer nullability.

Only reference types are supported as injected dependencies; nullable value types are not treated as optional services. For reference types, only the parameter's top-level annotation controls optionality. For example, `Handler<Input?>` is required, while `Handler<Input?>?` is optional.

Nullability is read from the parameter declaration, including metadata from another assembly. Parameters declared by code compiled without nullable annotations are therefore treated as required. Explicit parameter default values are not used because generated activation always supplies every argument.

### Nullable factory results

All singleton and transient factory overloads may return `null`. A null result means that descriptor contributes no service:

```csharp
services.AddSingleton<IWindowPositioningStrategy>((PlatformInfo platformInfo) =>
    platformInfo.SupportsSetWindowPosition
        ? new ProgrammaticWindowPositioningStrategy()
        : null);
```

- `GetRequiredService<T>()` returns the latest non-null result and throws if none exists.
- `GetService<T>()` returns the latest non-null result or `null` if none exists.
- `GetServices<T>()` excludes null results while preserving registration order.
- Resolution falls back through earlier registrations and then parent providers.
- Singleton factories are evaluated once during provider construction, including null results.
- Transient factories are evaluated for every resolution.
- Activation, disposal, registry, and alias behavior ignores null results.

`IsRegistered<T>()` reports whether a descriptor was registered; it remains `true` even when that descriptor's factory eventually returns `null`.

---

### `AddAlias<TService, TImplementation>()`

Makes `TService` resolve through the already-registered `TImplementation`. No source generator required. When `TImplementation` is a singleton, the alias resolves to the same instance. When `TImplementation` is transient, the alias creates a transient implementation instance per alias resolution.

```csharp
services.AddSingleton<AudioManager>();
services.AddAlias<IAudioService, AudioManager>();
```

`TImplementation` must be registered before `AddAlias` is called, or an `InvalidOperationException` is thrown.

Use when:
- A concrete type should be resolvable under one or more interface types.
- You want `GetServices<TService>()` to include the implementation instance.

---

### `AddRegistry<TService>(Comparison<TService>? comparison = null)`

Registers a `ServiceRegistry<TService>` singleton that tracks activated services assignable to
`TService`. The registry does not create services by itself; it observes normal service activation.
Singletons appear during `BuildServiceProvider`, and transients appear when they are resolved.
Tracked services are removed from the registry when the owning provider disposes them.
The optional comparison is applied when pending services are published before an outermost iteration.
Removing services preserves the existing order, and changing comparison state alone does not reorder the registry.

The registry is enumerable but is not a list. Activated services remain pending until the next
outermost iteration begins. Services activated during iteration are therefore excluded from that
iteration and become visible to the next one. Services removed during iteration are skipped
immediately. Nested iterations use the same published service generation as their outer iteration.
Enumeration uses a struct enumerator without creating a snapshot. `Version` changes when the
published service generation changes, allowing consumers to avoid rescanning an unchanged registry.
Pending additions do not affect it until an outermost iteration publishes them.

```csharp
services.AddRegistry<IUpdatable>(static (left, right) =>
{
    int leftOrder = left is IOrderable leftOrderable ? leftOrderable.Order : 0;
    int rightOrder = right is IOrderable rightOrderable ? rightOrderable.Order : 0;
    return leftOrder.CompareTo(rightOrder);
});
services.AddSingleton<PlayerController>();

ServiceRegistry<IUpdatable> registry =
    provider.GetRequiredService<ServiceRegistry<IUpdatable>>();
```

Use when:
- A subsystem needs a live list of activated services implementing a role.
- You do not want to force construction by depending on `IEnumerable<TService>`.
- Implementations should be discovered by implemented interfaces rather than explicit aliases.

---

### `OnStart(Action<ServiceProvider> action)`

Registers a callback that runs after all services are constructed but before the provider is frozen. No source generator required.

```csharp
services.OnStart(sp =>
{
    sp.GetRequiredService<IStageManager>().Load(stage =>
    {
        stage.AddSingleton<IView, GameplayView>();
    });
});
```

---

### `OnStart(Delegate action)` — requires source generator

Convenience overload that resolves the delegate's parameters as services.

```csharp
services.OnStart((IStageManager stages) =>
{
    stages.Load(stage =>
    {
        stage.AddSingleton<IView, GameplayView>();
    });
});
```

---

### `OnActivated(ServiceActivatedCallback callback)`

Registers a callback invoked immediately after each singleton or transient is constructed. For pre-constructed instances registered with `AddSingleton<T>(T instance)`, it runs when the provider is built.

### `OnDisposing(ServiceDisposingCallback callback)`

Registers a callback invoked during `ServiceProvider.Dispose()`, immediately before a provider-owned service's own `IDisposable.Dispose()` call.

Both delegates receive:

- `object instance` — the service instance.
- `Type type` — the concrete implementation type. Annotated with `DynamicallyAccessedMemberTypes.Interfaces`.

`OnActivated` callbacks fire in the order services are constructed. `OnDisposing` callbacks fire in reverse construction order, matching service disposal. Transient `IDisposable` instances created by the provider are tracked and disposed by the provider that created them. Multiple callbacks of the same kind run in registration order for each service.

The annotated `Type` parameter is important for NativeAOT and trimming. Generator-emitted registrations pass a `typeof(T)` value from an annotated generic type parameter into the callback path, so consumers can inspect interface metadata without falling back to `instance.GetType()`. This is what allows integrations such as `Pixely.Events.AddEvents()` to discover `IEventHandler<T>` implementations in an AOT-clean way.

```csharp
services.OnActivated(static (instance, type) =>
{
    Console.WriteLine($"Activated {type.Name}");
});
```

---

### `IsRegistered<T>()`

Returns `true` if the type has been registered in the collection or its parent provider hierarchy.

```csharp
if (!services.IsRegistered<DebugOverlay>())
{
    services.AddSingleton<DebugOverlay>();
}
```

---

### `BuildServiceProvider()`

Resolves all services, fires `OnStart` callbacks, freezes the provider, and returns it. A collection
created by `ServiceProvider.CreateServiceCollection()` builds a child provider of that provider.

```csharp
ServiceProvider provider = services.BuildServiceProvider();

// Child provider with fallback to a parent
ServiceCollection childServices = provider.CreateServiceCollection();
ServiceProvider child = childServices.BuildServiceProvider();
```

## Parent/Child Providers

`ServiceProvider.CreateServiceCollection()` binds a new collection to its parent before registration
begins. `IsRegistered<T>()` can therefore see inherited registrations while the child is configured.
Building the collection creates a child provider, and disposing the child tears down only its services.

### Service resolution

A child provider flattens the parent's singleton service array and registration lists into its own at freeze time. Child registrations take precedence for single-service resolution, while a null result falls back through earlier child registrations and then parent registrations. After freezing, singleton-only resolution uses a single array lookup; types containing transient registrations use the flattened registration list.

```csharp
ServiceCollection rootCollection = new();
rootCollection.AddSingleton(new GameSettings());
ServiceProvider root = rootCollection.BuildServiceProvider();

ServiceCollection stageCollection = root.CreateServiceCollection();
stageCollection.AddSingleton<IView>(new GameplayView());
ServiceProvider stage = stageCollection.BuildServiceProvider();

// stage can resolve both its own and parent services
GameSettings config = stage.GetRequiredService<GameSettings>();
IView view = stage.GetRequiredService<IView>();
```

### Service collections (`GetServices<T>`)

Multi-registrations compose across the hierarchy: parent entries appear first, followed by child entries. This is the opposite of single-service resolution (where the latest non-null child registration wins) — collections accumulate. Null results are excluded. Singleton-only collections are cached as `T[]` and returned without allocation. Collections containing any transient registration are rebuilt on each `GetServices<T>()` call so transient entries are fresh per collection resolution.

### Callback merging

`OnActivated` and `OnDisposing` callbacks registered on the parent's `ServiceCollection` are **merged into the child provider**. When the child provider constructs a service, the parent's `OnActivated` callbacks fire first, then the child's own. When the child provider disposes, its `OnDisposing` callbacks fire first, then the parent's.

This means child services automatically participate in any lifecycle hooks the parent set up. `AddRegistry<TService>()` is built on these callbacks, so a child provider contributes matching services to registries created by the parent and removes them on disposal. Some higher-level systems still use callbacks directly when they need richer behavior than a plain role list.

```csharp
// Root sets up a registry-backed role list
ServiceCollection rootCollection = new();
rootCollection.AddRegistry<IUpdatable>();
ServiceProvider root = rootCollection.BuildServiceProvider();
ServiceRegistry<IUpdatable> updatables =
    root.GetRequiredService<ServiceRegistry<IUpdatable>>();

// Child inherits the registry callbacks — PhysicsSystem appears in the root registry
ServiceCollection stageCollection = root.CreateServiceCollection();
stageCollection.AddSingleton<IUpdatable, PhysicsSystem>();
ServiceProvider stage = stageCollection.BuildServiceProvider();
Assert.That(updatables, Has.Some.InstanceOf<PhysicsSystem>());

// Disposing the child removes PhysicsSystem from the registry
stage.Dispose();
```

### Disposal

Disposing a child provider:

1. Detaches from the parent (clears the parent reference).
2. Disposes its own children recursively (deepest first).
3. Disposes transient `IDisposable` instances it created, then walks its own singleton services in reverse creation order — `OnDisposing` callbacks fire, then `IDisposable.Dispose()`.

Parent-owned services are **not** disposed by the child. If a child resolves a transient registration inherited from a parent, the child creates and owns that transient instance. Disposing a parent cascades to all children before disposing its own services.

## Resolution API

### `GetRequiredService<T>()`

Returns the service or throws `InvalidOperationException` if not registered.

```csharp
AudioSystem audio = provider.GetRequiredService<AudioSystem>();
```

When `T` is `IEnumerable<TElement>`, the source generator intercepts the call and redirects it to `GetServices<TElement>()`.

---

### `GetService<T>()`

Returns the service or `null` if not registered.

```csharp
DebugOverlay? overlay = provider.GetService<DebugOverlay>();
```

When `T` is `IEnumerable<TElement>`, the source generator intercepts the call and redirects it to `GetServices<TElement>()`.

---

### `GetServices<T>()`

Returns all non-null instances registered under `T` as `IReadOnlyList<T>`. If every entry is a singleton, the list is a real `T[]` built at `BuildServiceProvider` time and returned without allocation or copying. If any entry is transient, `GetServices<T>()` returns a new `T[]` each call; singleton entries are reused and transient entries are newly constructed.

```csharp
IReadOnlyList<IRenderer> renderers = provider.GetServices<IRenderer>();
foreach (IRenderer renderer in renderers)
{
    renderer.Draw(commandBuffer);
}
```

Returns an empty list if no services of type `T` are registered. Falls back to the parent provider if one is set.

---

### `Dispose()`

Disposes provider-owned services in reverse creation order. Transient `IDisposable` instances created by the provider are disposed before singleton services, so singleton dependencies remain available while transients are torn down. For each disposed service, `OnDisposing` callbacks fire first, then the service's own `IDisposable.Dispose()` runs. Services that are aliased to multiple types are disposed exactly once (deduplicated by reference).

## Lifecycle

1. **Registration** — call `AddSingleton`, `AddTransient`, `AddAlias`, `OnStart`, `OnActivated`, `OnDisposing` on `ServiceCollection`.
2. **`BuildServiceProvider`** — singleton services are instantiated in dependency order; `OnActivated` callbacks fire per singleton instance.
3. **`OnStart` callbacks** — fire in registration order after all singleton services exist.
4. **Freeze** — the provider becomes immutable; build-time resolvers are cleared.
5. **Runtime resolution** — `GetRequiredService`, `GetService`, `GetServices` serve singletons from frozen arrays and construct transients on demand.
6. **`Dispose`** — transient disposables and singleton disposables are visited in reverse creation order: `OnDisposing` callbacks fire, then `IDisposable.Dispose()` runs.

## Multi-Registration and `GetServices<T>`

Registering the same type more than once is allowed. For single-service resolution (`GetRequiredService`, `GetService`), the latest non-null registration wins. All non-null results are preserved in registration order in the collection returned by `GetServices<T>`.

```csharp
services.AddSingleton<IRenderer>(new BackgroundRenderer());
services.AddTransient<IRenderer, SpriteRenderer>();
services.AddSingleton<IRenderer>(new UiRenderer());

// GetRequiredService returns only UiRenderer (latest non-null wins)
IRenderer last = provider.GetRequiredService<IRenderer>();

// GetServices returns all three in registration order; SpriteRenderer is fresh per call
IReadOnlyList<IRenderer> all = provider.GetServices<IRenderer>();
```

## Aliases

`AddAlias<TService, TImplementation>()` points `TService` at the already-registered `TImplementation`. The implementation must be registered first. Singleton aliases resolve to the same instance; transient aliases create a transient implementation instance per alias resolution.

```csharp
services.AddSingleton<PhysicsEngine>();
services.AddAlias<IPhysicsService, PhysicsEngine>();
services.AddAlias<ICollisionQuery, PhysicsEngine>();

// All three resolve to the same PhysicsEngine instance
PhysicsEngine engine   = provider.GetRequiredService<PhysicsEngine>();
IPhysicsService svc    = provider.GetRequiredService<IPhysicsService>();
ICollisionQuery query  = provider.GetRequiredService<ICollisionQuery>();
```

Aliases appear in `GetServices<TService>()` collections alongside any direct registrations under `TService`.

## Source Generator Caveats

The following overloads are **intercepted at each call site** by the Roslyn source generator (`Pixely.DependencyInjection.Generator`). Their runtime bodies throw `InvalidOperationException` when a call is not intercepted, such as when the generator is absent. With the generator active, unsupported constructor implementation types produce `GK0001`:

| Overload | Interception requirement |
|---|---|
| `AddSingleton<T>()` | `T` must be a named concrete type that does not contain a type parameter from a generic calling scope |
| `AddSingleton<TService, TImplementation>()` | Constructor implementations must be named concrete types that do not contain type parameters from a generic calling scope |
| `AddSingleton<T>(Delegate factory)` | `T` must be a named concrete type; delegate argument must be resolvable at compile time |
| `AddTransient<T>()` | `T` must be a named concrete type that does not contain a type parameter from a generic calling scope |
| `AddTransient<TService, TImplementation>()` | Constructor implementations must be named concrete types that do not contain type parameters from a generic calling scope |
| `AddTransient<T>(Delegate factory)` | `T` must be a named concrete type; delegate argument must be resolvable at compile time |
| `OnStart(Delegate action)` | Delegate argument must be resolvable at compile time |

**The generic-scope failure mode.** Constructor registration cannot be used when the implementation type is a type parameter, or is a known generic type containing a type parameter from an enclosing method or type. The generator reports `GK0001` at the registration call and does not emit an interceptor:

```csharp
// Does NOT work — T is a type parameter
void Register<T>(ServiceCollection services) where T : class
{
    services.AddSingleton<T>(); // GK0001
}

// Does NOT work — Handler<T> still contains the helper's type parameter
void RegisterHandler<T>(ServiceCollection services) where T : class
{
    services.AddTransient<IHandler<T>, Handler<T>>(); // GK0001
}

// Works — concrete type visible at each call site
services.AddSingleton<AudioSystem>();
services.AddSingleton<RenderPipeline>();
```

If you need a generic registration helper, use the non-intercepted overload with a factory:

```csharp
void Register<T>(ServiceCollection services, Func<ServiceProvider, T?> factory) where T : class
{
    services.AddSingleton<T>(factory); // Func<ServiceProvider, T?> overload — no generator needed
}

void RegisterHandler<T>(
    ServiceCollection services,
    Func<ServiceProvider, Handler<T>?> factory)
    where T : class
{
    // Preserves Handler<T> as the concrete type used by lifecycle callbacks
    services.AddTransient<IHandler<T>, Handler<T>>(factory);
}
```

**Constructor requirements.** `AddSingleton<T>()`, `AddSingleton<TService, TImplementation>()`, `AddTransient<T>()`, and `AddTransient<TService, TImplementation>()` require the implementation type to have exactly one public constructor (or an implicit parameterless constructor). Multiple public constructors produce a compile-time error `GK0002`.

**`IEnumerable<T>` injection.** The generator intercepts `GetRequiredService<IEnumerable<T>>()` and `GetService<IEnumerable<T>>()` at call sites and rewrites them to `GetServices<T>()`. Constructor injection of `IEnumerable<T>` via generated registrations is handled the same way — the generated constructor call uses `sp.GetServices<T>()` for any `IEnumerable<T>` parameter. An outer nullable annotation does not change this behavior; `IEnumerable<T>?` still receives a non-null collection. Other collection types such as `IReadOnlyList<T>`, `List<T>`, and arrays use ordinary required or optional single-service resolution. If a singleton receives an `IEnumerable<T>` containing transient entries, those transient instances are created during singleton construction and captured by that singleton, matching Microsoft.Extensions.DependencyInjection semantics.
