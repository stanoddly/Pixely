# Window rendering

Pixely uses `default(ViewScope)` for the ordinary single-window case. Applications only need to
name scopes when they render more than one window.

## Single-window rendering

`UseDefaultRendering` creates a DI-owned window and render coordinator:

```csharp
PixelyAppBuilder builder = new();
builder
    .UseDefaultRendering(
        new WindowConfig(
            Size: new Size<uint>(1280, 720),
            Title: "Game"));
```

Omitting `UseDefaultRendering` creates no window.

Window renderers use the ordinary `IRenderer<BasicRenderContext>` contract:

```csharp
public sealed class GameRenderer : IRenderer<BasicRenderContext>
{
    public void Render(BasicRenderContext renderContext)
    {
        // Record rendering commands.
    }
}
```

Register renderers normally through DI:

```csharp
builder.AddSingleton<IRenderer<BasicRenderContext>, GameRenderer>(GameRenderer.Create);
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

`AddWindow` creates a Pixely-managed window without selecting a render context. Combine it with
`UseWindowRendering<T>` when the application needs a context with resources such as depth targets
or cameras:

```csharp
PixelyAppBuilder builder = new();
builder
    .AddWindow(
        new WindowConfig(
            Size: new Size<uint>(1280, 720),
            Title: "Game"))
    .UseWindowRendering<GameRenderContext>();

builder.AddSingleton<GameRenderContextProvider>(GameRenderContextProvider.Create);
builder.AddAlias<IRenderContextProvider<GameRenderContext>, GameRenderContextProvider>();
```

The provider uses ordinary dependency injection, including static factory registration. It does not
receive or resolve a window during construction:

```csharp
public sealed class GameRenderContextProvider : IRenderContextProvider<GameRenderContext>
{
    private readonly GpuDevice _gpuDevice;
    private readonly DepthTarget _depthTarget;
    private readonly Camera _camera;

    private GameRenderContextProvider(GpuDevice gpuDevice, DepthTarget depthTarget, Camera camera)
    {
        _gpuDevice = gpuDevice;
        _depthTarget = depthTarget;
        _camera = camera;
    }

    public static GameRenderContextProvider Create(GpuDevice gpuDevice, DepthTarget depthTarget, Camera camera)
    {
        return new GameRenderContextProvider(gpuDevice, depthTarget, camera);
    }

    public bool TryCreateRenderContext(Window window, out GameRenderContext? renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out SwapchainTexture swapchainTexture))
        {
            commandBuffer.Dispose();
            renderContext = null;
            return false;
        }

        renderContext = new GameRenderContext(swapchainTexture, commandBuffer, _depthTarget, _camera, window.RenderSizeInPixels);
        return true;
    }
}
```

Extend `BasicRenderContext` to retain its swapchain texture, color target, command buffer, and submission behavior while adding application-specific state:

```csharp
public sealed class GameRenderContext : BasicRenderContext
{
    public DepthTarget DepthTarget { get; }
    public Camera Camera { get; }
    public Size<uint> RenderSizeInPixels { get; }

    public GameRenderContext(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer, DepthTarget depthTarget, Camera camera, Size<uint> renderSizeInPixels)
        : base(swapchainTexture, commandBuffer)
    {
        DepthTarget = depthTarget;
        Camera = camera;
        RenderSizeInPixels = renderSizeInPixels;
    }
}
```

The framework coordinator passes its managed window to the provider for each frame, skips hidden
windows, invokes renderers for the same `ViewScope`, and disposes the resulting context. Registration
order does not matter: `UseWindowRendering<T>` may appear before or after `AddWindow` and the provider
registration. `BasicRenderContext.Dispose` is virtual, so a derived context can add per-frame cleanup
and call the base implementation to submit its command buffer. Window registration, event routing
and disposal remain managed by Pixely.

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
PixelyAppBuilder builder = new();
builder
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
public sealed class InventoryRenderer : IRenderer<BasicRenderContext>
{
    ViewScope IRenderer<BasicRenderContext>.ViewScope => ViewScopes.Inventory;

    public void Render(BasicRenderContext renderContext)
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
