# Frame order

A frame runs every registered `IUpdatable`, then every registered `IRenderer<TRenderContext>`. Both
are ordered by an integer the type reports: `IUpdatable.UpdateOrder` and `IRenderer.RenderOrder`,
lowest first. Both default to `0`.

## Equal orders keep registration order

Updatables and renderers live in a `ServiceRegistry<T>`, which sorts by that integer whenever newly
activated services are published. The sort is stable, so:

- Two updatables with the same `UpdateOrder` run in the order they were registered.
- Registering something later never moves things registered earlier past each other.
- Removing something never moves the rest.

This holds for every registry built with an order key, not just these two. See
[class-registration.md](class-registration.md) for the registry contract.

## Where the framework puts its own

`UpdateOrders` and `RenderOrders` name the bands the framework occupies, so a game can place its own
work relative to them instead of guessing.

| Constant | Value | Runs there |
| --- | --- | --- |
| `UpdateOrders.Diagnostics` | -20 000 | `PerformanceTracker` |
| `UpdateOrders.Default` | 0 | `TimerSystem`, `UpdateSystem` |
| `UpdateOrders.Ui` | 10 000 | `UiUpdateSystem<T>` (Pixely.Ui) |
| `UpdateOrders.Maintenance` | 20 000 | `FontSystem` |

| Constant | Value | Draws there |
| --- | --- | --- |
| `RenderOrders.Default` | 0 | nothing by default, the band a game draws in |
| `RenderOrders.Ui` | 10 000 | `UiRenderer<T>` |

Components that register with `UpdateSystem` run inside `UpdateOrders.Default`, in the order they
called `Add`.

## Placing your own

Write the position against a constant rather than a bare number:

```csharp
public class CameraFollow : IUpdatable
{
    // Follow the player after game logic has moved it, but before the UI is built against it.
    public int UpdateOrder => UpdateOrders.Ui - 1;

    public void Update()
    {
    }
}
```

```csharp
public class DebugOverlayRenderer : IRenderer<BasicRenderContext>
{
    public int RenderOrder => RenderOrders.Ui + 1;

    public void Render(BasicRenderContext renderContext)
    {
    }
}
```

`UseUi` takes `updateOrder` and `renderOrder` if a game needs the UI somewhere other than its
default band.

## Input order is a separate axis

Mouse, keyboard and text input handlers are ordered by the `order` argument passed to their
`Subscribe` call, not by `UpdateOrder`. That order decides who sees an event first and therefore who
can consume it. `UseUi` subscribes at `-10 000` so its controls get the click before the game does.
