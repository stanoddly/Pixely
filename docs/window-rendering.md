# Window rendering

Pixely uses `default(ViewScope)` for the ordinary single-window case. Applications only need to
name scopes when they render more than one window.

## Single-window rendering

`UseDefaultRendering` creates a DI-owned window and render coordinator:

```csharp
PixelyAppBuilder builder = new PixelyAppBuilder()
    .UseDefaultRendering(
        new WindowConfig(
            Size: new Size<uint>(1280, 720),
            Title: "Game"));
```

Omitting `UseDefaultRendering` creates no window.

Window renderers use the ordinary `IRenderer<DefaultRenderContext>` contract:

```csharp
public sealed class GameRenderer : IRenderer<DefaultRenderContext>
{
    public void Render(DefaultRenderContext renderContext)
    {
        // Record rendering commands.
    }
}
```

Register renderers normally through DI:

```csharp
builder.AddSingleton<IRenderer<DefaultRenderContext>, GameRenderer>(GameRenderer.Create);
```

The default `IRenderer.ViewScope` implementation returns `default`, so single-window renderers do
not declare a scope.

The default window is available without a scope argument:

```csharp
Window window = windowRegistry.GetWindow();
graphicsPipelineBuilder.AddColorFormatFromDisplay();
textInputService.Start();
bool containsMouse = mouseService.IsInWindow();
```

## Custom render contexts

`AddWindow` creates a Pixely-managed window without registering a render coordinator. Combine it
with `UseRenderCoordinator<T>` when the application needs a context with resources such as depth
targets or cameras:

```csharp
PixelyAppBuilder builder = new PixelyAppBuilder()
    .AddWindow(
        new WindowConfig(
            Size: new Size<uint>(1280, 720),
            Title: "Game"))
    .UseRenderCoordinator<GameRenderContext>(
        static (provider, renderers) => new GameRenderCoordinator(
            provider.GetWindow(),
            provider.GetRequiredService<GpuDevice>(),
            provider.GetRequiredService<GpuMemorySystem>(),
            renderers));
```

`ServiceProvider.GetWindow` resolves the window belonging to that provider and its ancestors. It
activates reachable window registrations before selecting by `ViewScope`, so registration order
does not affect coordinator construction.

The custom coordinator receives the managed `Window` directly and creates the application-specific
context:

```csharp
protected override bool TryCreateRenderContext(out GameRenderContext? renderContext)
{
    if (!_window.IsVisible)
    {
        renderContext = null;
        return false;
    }

    CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
    if (!_window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out SwapchainTexture swapchainTexture))
    {
        commandBuffer.Dispose();
        renderContext = null;
        return false;
    }

    renderContext = new GameRenderContext(swapchainTexture, commandBuffer, _depthTarget, _camera);
    return true;
}
```

`GameRenderContext` implements `IRenderContext` and submits its command buffer when disposed in the
same way as other render contexts. Window registration, event routing and disposal remain managed by
Pixely. A hidden window remains active but the custom coordinator should skip context creation as
shown above.

## Multiple windows

Define stable scope values for additional windows:

```csharp
internal static class ViewScopes
{
    internal static readonly ViewScope Inventory = new(1);
}
```

The implicit window remains `default(ViewScope)` while additional windows receive explicit scopes:

```csharp
PixelyAppBuilder builder = new PixelyAppBuilder()
    .UseDefaultRendering(
        new WindowConfig(
            Size: new Size<uint>(1280, 720),
            Title: "Game"))
    .UseDefaultRendering(
        ViewScopes.Inventory,
        new WindowConfig(
            Size: new Size<uint>(480, 360),
            Title: "Inventory"));
```

A renderer for an additional window overrides the scope explicitly:

```csharp
public sealed class InventoryRenderer : IRenderer<DefaultRenderContext>
{
    ViewScope IRenderer<DefaultRenderContext>.ViewScope => ViewScopes.Inventory;

    public void Render(DefaultRenderContext renderContext)
    {
        // Render the inventory window.
    }
}
```

The renderer registry preserves `IOrderable.Order` and executes each renderer only for its matching
scope. A reusable renderer can receive its `ViewScope` through construction and be registered more
than once.

Resolve resources for additional windows through their scope:

```csharp
Window inventoryWindow = windowRegistry.GetWindow(ViewScopes.Inventory);
graphicsPipelineBuilder.AddColorFormatFromDisplay(ViewScopes.Inventory);
```

Secondary windows can be created hidden and shown on request. A reusable window can hide when its
close button is pressed instead of quitting the application:

```csharp
builder.UseDefaultRendering(
    ViewScopes.Inventory,
    new WindowConfig(
        Size: new Size<uint>(480, 360),
        Title: "Inventory",
        InitiallyVisible: false,
        CloseBehavior: WindowCloseBehavior.HideWindow));

Window inventoryWindow = windowRegistry.GetWindow(ViewScopes.Inventory);
inventoryWindow.Show();
inventoryWindow.Hide();
```

Hidden windows remain registered and retain their renderer and GPU resources, but their render
coordinators do not acquire a swapchain texture or invoke renderers. They are disposed with the
service provider that owns them. `InitiallyVisible` controls initial visibility; it does not defer
native window creation.

SDL window IDs remain internal and are used only to route native events. Windows registered by a
stage use the stage provider's lifetime. Disposing that provider unregisters and disposes its window
and render coordinator.

## Scoped input

Window-associated events and subscriptions target the implicit default scope. In a multi-window
application, use a scoped subscription when a handler belongs to another window:

```csharp
keyboardService.SubscribeKeyDown(
    ViewScopes.Inventory,
    priority: 0,
    eventArgs => HandleInventoryKey(eventArgs));
```

Scoped overloads exist for keyboard, mouse, and text-input subscriptions.

## Pencuil

The common case requires no scope:

```csharp
builder.UsePencuil();
```

Configure another Pencuil instance only for an additional window:

```csharp
builder.UsePencuil(ViewScopes.Inventory);
```

Pencuil's MVVM contracts use explicit names: `IPencuilView`, `IPencuilViewModel`, and
`PencuilView<TViewModel>`. Their default scope is implicit; views belonging to another window
override `IPencuilView.ViewScope` or pass a scope to the Pencuil view base class.

See `Pixely.Tutorials.MultiWindow` for two independently rendered windows and
`Pixely.Tutorials.MultiWindowTextInput` for independent Pencuil focus and text input.
