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

Omitting `UseDefaultRendering` creates no window; `AddWindow` alone creates one without rendering (see below).

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

## The GPU device

The GPU device and the services that need it (`GpuMemorySystem`, the shader loaders, `ITextureLoader`, the
pipeline builders and `IFontSystem`) are registered by `UseGpu()`. `UseDefaultRendering`,
`UseWindowRendering<T>`, `UseUi`, `RegisterSpriteLoading` and `RegisterAtlas` call it, so an app that renders
never calls it directly. Call it yourself when the app needs the GPU without a window renderer, such as a
compute-only app. It is idempotent, and a registered `GpuDevice` from any source makes it a no-op, so it belongs
at the root when any stage renders: `IsRegistered` sees the parent, and a stage calling `UseDefaultRendering`
under a root without the device registers the device in that child, so each such stage creates its own device and
a root window created by `AddWindow` stays unclaimed.

Without `UseGpu()` there is no device: `AddWindow` alone creates an SDL window that is not claimed for a device,
the frame loop processes events and updates, and the window's `ColorTargetFormat` and
`TryWaitAndAcquireSwapchainTexture` throw `InvalidOperationException`. Nothing then waits for vsync, so such an
app spins the loop.

## The browser

In a browser the page is the screen: the window fills it and follows the browser window's size, so `WindowConfig.Size` is ignored, as are `Fullscreen`, `Resizable`, `Transparent`, `Borderless` and `AlwaysOnTop`. `Window.Size` reports the page size and resizes arrive through `ResolutionChanged` as on the desktop.

A browser owns the frame loop, so `Pixely.App.BrowserHost.RunAsync` calls `IPixelyApp.RunFrame()` once per animation frame instead of `Run()`, which loops over it until it returns false. The generated entry point awaits it for `browser-wasm` (see [Hosting](hosting.md)).

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
builder.AddAlias<RenderContextProvider<GameRenderContext>, GameRenderContextProvider>();
```

The provider uses ordinary dependency injection, including static factory registration. It does not
receive or resolve a window during construction. Deriving it from `BasicRenderContextProvider<T>` leaves the frame sequence
to Pixely: acquiring the command buffer and the swapchain texture, cancelling the command buffer when that fails, and handing
the same context out again every frame instead of allocating one. The provider only says how the context is created, once,
and what it needs each frame:

```csharp
public sealed class GameRenderContextProvider : BasicRenderContextProvider<GameRenderContext>
{
    private readonly DepthTarget _depthTarget;
    private readonly Camera _camera;

    private GameRenderContextProvider(GpuDevice gpuDevice, DepthTarget depthTarget, Camera camera)
        : base(gpuDevice)
    {
        _depthTarget = depthTarget;
        _camera = camera;
    }

    public static GameRenderContextProvider Create(GpuDevice gpuDevice, DepthTarget depthTarget, Camera camera)
    {
        return new GameRenderContextProvider(gpuDevice, depthTarget, camera);
    }

    protected override GameRenderContext CreateRenderContext()
    {
        return new GameRenderContext(_depthTarget, _camera);
    }

    protected override void PrepareRenderContext(GameRenderContext renderContext, Window window)
    {
        renderContext.RenderSizeInPixels = window.RenderSizeInPixels;
    }
}
```

A provider for a context that is not a `BasicRenderContext` derives from `RenderContextProvider<T>` and implements
`TryCreateRenderContext` itself. When the swapchain acquire fails or throws, it cancels the command buffer, which returns it to
the device's pool.

`RenderCoordinator` skips a window whose `IsRenderable` is false; by default that is `IsVisible`, since a hidden window has no swapchain image. `TryWaitAndAcquireSwapchainTexture` and `IsRenderable` are virtual. An override must not throw after its base implementation acquired the texture: the provider cancels the command buffer when the acquire throws, and SDL refuses to cancel once a swapchain texture is acquired. `OffscreenWindow`, which every window becomes under `PixelyConfig.Headless`, overrides them to hand out a texture instead of a swapchain image while the SDL window stays hidden, so a custom provider written against the window works offscreen unchanged. See headless.md.

### Reporting the colour target size

`GetColorTargetSize` says how big the colour target will be, without acquiring one. The default answers `window.RenderSizeInPixels`, which is correct whenever the context targets the swapchain.

Override it when the context draws somewhere else, such as a low resolution texture that is scaled up:

```csharp
public override ShortSize GetColorTargetSize(Window window)
{
    return _offscreenTarget.Size;
}
```

Systems that run in the update phase read this. They lay out against the target before any render context exists, so they cannot inspect one. `Pixely.Ui` builds its element tree this way. A provider that draws into a differently sized target and does not override this leaves the UI laid out for the window, and the UI renderer then refuses to draw it into a target of another size.

Extend `BasicRenderContext` to retain its swapchain texture, color target, command buffer, and submission behavior while adding application-specific state. The provider creates it once through the parameterless base constructor and sets the frame's swapchain texture and command buffer itself:

```csharp
public sealed class GameRenderContext : BasicRenderContext
{
    public DepthTarget DepthTarget { get; }
    public Camera Camera { get; }
    public ShortSize RenderSizeInPixels { get; set; }

    public GameRenderContext(DepthTarget depthTarget, Camera camera)
    {
        DepthTarget = depthTarget;
        Camera = camera;
    }
}
```

The framework coordinator passes its managed window to the provider for each frame, skips windows
whose `IsRenderable` is false, invokes renderers for the same `ViewScope`, and disposes the resulting context. Registration
order does not matter: `UseWindowRendering<T>` may appear before or after `AddWindow` and the provider
registration. `BasicRenderContext.Dispose` is virtual, so a derived context can add per-frame cleanup
and must call the base implementation, which submits the command buffer and marks the context free for the next frame. An
override that skips it, or throws before reaching it, leaves the acquired swapchain image unsubmitted, and rendering stalls once
the frames in flight run out. Window registration, event routing
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

The renderer registry preserves `IRenderer<T>.RenderOrder` and executes each renderer only for its matching
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
bool shown = inventoryWindow.Show();
bool raised = inventoryWindow.Raise();
bool hidden = inventoryWindow.Hide();
```

`Show()`, `Raise()`, and `Hide()` return whether the native window operation succeeded. Raising a window requests input focus, subject to the operating system's window-management policy.

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
    order: 0,
    eventArgs => HandleInventoryKey(eventArgs));
```

Scoped overloads exist for keyboard, mouse, and text-input subscriptions.

## Activating mouse clicks

Clicking an unfocused window activates it. SDL discards that click by default, so the first click after
switching windows is lost, including when switching between two windows of the same application. Pixely
delivers it instead: it enables `SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH` during initialization. Register
`PixelyConfig` with `DeliverActivatingMouseClicks: false` to restore the SDL default:

```csharp
builder.AddSingleton(new PixelyConfig(DeliverActivatingMouseClicks: false));
```

Either way, `IMouseService` only raises `ButtonRelease` for a button whose press it saw, so a suppressed
press never leaves a release behind it.

## User interface

See `docs/ui.md` for `Pixely.Ui`, which is scoped the same way: `UseUi(viewScope)` configures a root for another window, and a view says which one it belongs to through `IUiView.ViewScope`.

See `Pixely.Tutorials.MultiWindow` for two independently rendered windows and
`Pixely.Tutorials.MultiWindowTextInput` for independent UI focus and text input.
