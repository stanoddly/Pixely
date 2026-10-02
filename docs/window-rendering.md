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

With `UseGpu()` but without window rendering, as in a compute-only app, or after a stage that rendered is unloaded,
no render coordinator runs:

- The frame loop submits the uploads `GpuMemorySystem` records, once per frame after the updates. An app that submits
  its own command buffer during an update, and that command buffer reads buffers updated since the last submit, must
  call `GpuMemorySystem.Submit()` before it; otherwise the command buffer reads undefined contents. This covers only the
  root's `GpuMemorySystem`: a stage that registers its own GPU device without window rendering must submit its own.
- A window is claimed for the device only when its swapchain is first used, by `ColorTargetFormat` or
  `TryWaitAndAcquireSwapchainTexture`. On the desktop, after a frame in which no window of a device requested a
  swapchain texture, every window claimed for that device is released. With a claimed window, SDL's Vulkan backend
  frees finished GPU work only on a submit that requested a swapchain texture, or on a fence wait, so a claimed window
  nothing renders would keep that work. SDL waits for the device to go idle when it releases a window, so this waits
  once each time the device's windows stop requesting textures, and the next coordinator's acquire claims the window
  again and recreates its swapchain. The browser keeps the claims, because its WebGPU backend frees finished work on
  every submit.
- While a window is released, `ColorTargetFormat` returns the format SDL reported last, so reading it does not claim
  the window again; while it is claimed, it returns SDL's current format. SDL picks the format again whenever it creates
  a swapchain, after a resize as well as when it claims the window again, so a pipeline built for the earlier format
  can stop matching.
- The acquire does not claim a minimized window and returns no texture instead: on drivers that report a zero size for
  a minimized window, such as NVIDIA on Win32, SDL's Vulkan backend reports such a claim as successful without claiming
  the window. A claim that SDL reports as successful but that leaves no swapchain format throws
  `PixelyInitializationException`; reading `ColorTargetFormat` for the first time while the window is minimized can
  hit it there. Each such claim leaks SDL's data for the window.

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
receive or resolve a window during construction:

```csharp
public sealed class GameRenderContextProvider : RenderContextProvider<GameRenderContext>
{
    private readonly DepthTarget _depthTarget;
    private readonly Camera _camera;

    private GameRenderContextProvider(DepthTarget depthTarget, Camera camera)
    {
        _depthTarget = depthTarget;
        _camera = camera;
    }

    public static GameRenderContextProvider Create(DepthTarget depthTarget, Camera camera)
    {
        return new GameRenderContextProvider(depthTarget, camera);
    }

    public override GameRenderContext CreateRenderContext(FrameContext frameContext)
    {
        return new GameRenderContext(frameContext.SwapchainTexture, frameContext.CommandBuffer, _depthTarget, _camera, frameContext.Window.RenderSizeInPixels);
    }
}
```

`RenderCoordinator`, not the provider, acquires each frame's command buffer and swapchain texture, and passes them to `CreateRenderContext` in a `FrameContext` together with the window. One provider can serve several windows, such as two that use `UseDefaultRendering`, so `FrameContext.Window` tells them apart. The coordinator disposes the context after the renderers and then submits the command buffer itself, so neither the context nor a renderer submits or cancels it. On the desktop the coordinator acquires a frame every frame, even for a window that is not renderable, but calls the provider and the renderers only while the window's `IsRenderable` is true; a frame nobody sees is submitted without a context. In the browser it skips a window that is not renderable: SDL's browser driver does not hide the canvas, so presenting an undrawn texture would blank it. By default a hidden or minimized window is not renderable. When no swapchain texture comes back, the coordinator does not call the provider. On the desktop it submits the command buffer instead of cancelling it: SDL's Vulkan backend frees finished GPU work only on a submit that requested a swapchain texture. In the browser it cancels the command buffer and leaves pending uploads for the next drawn frame: the WebGPU fork returns no texture while its submissions in flight reach the frame limit, so another submission would only hold a slot. A different acquire policy needs a custom `IRenderCoordinator`. `Window` is abstract, and `ColorTargetFormat` and `TryWaitAndAcquireSwapchainTexture` belong to the window that presents its frames. `SwapchainWindow`, the window of a normal run, hands out the swapchain image of a window claimed for the GPU device, and claims the window when its swapchain is first used. `OffscreenWindow`, which every window becomes under `PixelyConfig.Headless`, hands out a texture instead while the SDL window stays hidden and unclaimed, so a custom provider written against `Window` works offscreen unchanged. See headless.md.

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

Extend `BasicRenderContext` to retain its swapchain texture, color target and command buffer while adding application-specific state. The coordinator submits the command buffer, so a context never does:

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

For each frame it draws, where the window is renderable and a swapchain texture came back, the framework coordinator
passes its managed window and the acquired frame to the provider in a `FrameContext`, invokes renderers for the
same `ViewScope`, and disposes the resulting context. Registration
order does not matter: `UseWindowRendering<T>` may appear before or after `AddWindow` and the provider
registration. `BasicRenderContext.Dispose` is virtual and does nothing, so a derived context can add per-frame
cleanup; the coordinator submits the command buffer after disposing the context. Window registration, event routing
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

Hidden and minimized windows remain registered and retain their renderer and GPU resources. Their render
coordinators do not invoke renderers, and on the desktop they still request a swapchain texture every frame. When
no window draws a frame, the desktop frame loop waits up to 16 ms for an event, so the app does not spin; in the
browser, `requestAnimationFrame` paces the frames. They are disposed with the
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
