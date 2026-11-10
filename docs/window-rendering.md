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

`new WindowConfig(Utility: true)` creates a utility window, which does not show in the task bar and window list. This works on Windows and X11. On Wayland and macOS the window stays an ordinary window.

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

A window claimed for the device gets an sRGB swapchain (`SDR_LINEAR` with vsync): what shaders write is linear light, and the GPU encodes it to sRGB on write (see [shaders.md](shaders.md#color-spaces)). Its `ColorTargetFormat` is `B8G8R8A8UnormSrgb` or `R8G8B8A8UnormSrgb`, whichever the driver has. A driver without an sRGB swapchain for the window fails window creation with `PixelyInitializationException`; there is no fallback, because it would need every shader to encode as well.

Without `UseGpu()` there is no device: `AddWindow` alone creates an SDL window that is not claimed for a device,
the frame loop processes events and updates, and the window's `ColorTargetFormat` and
`TryWaitAndAcquireSwapchainTexture` throw `InvalidOperationException`. Nothing then waits for vsync, so such an
app spins the loop.

With `UseGpu()` but without window rendering, as in a compute-only app, no render coordinator runs. Only a render
coordinator submits the uploads `GpuMemorySystem` records, so the app must call `GpuMemorySystem.Submit()` before the
GPU work that reads them. Until then the uploads do not run, and on every backend each buffer update makes a new
full-size copy of the buffer, because the unsubmitted uploads keep the earlier copies in use.

`AddWindow` still claims the window for the device, but nothing requests its swapchain texture. With a claimed window,
SDL's Vulkan backend defragments GPU memory and frees empty allocations only on a submit that requested one, so on
Vulkan neither happens in such an app.

## Waiting for uploads

Render coordinators submit the frame's uploads before the frame's command buffer. On the Raspberry Pi 5's V3DV driver that
is not enough. Its GPU first sorts each draw's triangles into the 64×64 px screen tiles they cover ("binning"), and it can
do that before the uploads submitted ahead of the draw have finished. Geometry that a vertex shader reads from a buffer
updated every frame then has holes in the tiles its triangles have moved into. SDL ends the copy with the barrier Vulkan
requires, so this is a driver bug.

`PixelyConfig.UploadWait` works around it. When the wait is on, `GpuMemorySystem.Submit()` submits the uploads with a
fence and waits for it, so the frame is submitted only after the uploads have finished. The CPU then waits for the GPU's
copies every frame, and the copies no longer overlap with the work submitted before them.

| Value | Wait |
| --- | --- |
| `Automatic` (default) | On when the graphics driver's name starts with `V3DV`, off everywhere else. Off in the browser. |
| `On` | Always. In the browser, `Build()` of an app with the GPU device fails with `PixelyInitializationException`, because it cannot wait on a fence. |
| `Off` | Never. |

`PIXELY_UPLOAD_WAIT` overrides it (see [headless.md](headless.md#environment-variables)), for example to check whether a
newer Mesa still needs the wait. The diagnostics' `GPU device:` line says whether it is on (see [diagnostics.md](diagnostics.md)).

## Pixel density and display scale

Take a window created with `Size: (1280, 720)` on a display set to 200 %:

| | macOS, Wayland, browser | macOS, Wayland, browser with `HighPixelDensity: false` | Windows, X11 |
| --- | --- | --- | --- |
| `Size` | 1280x720 | 1280x720 | 1280x720 |
| `RenderSizeInPixels` | 2560x1440 | 1280x720 | 1280x720 |
| `PixelDensity` | 2 | 1 | 1 |
| `DisplayScale` | 2 | 1 | 2 |
| Looks on screen | as big as 1280x720 at 100 %, sharp | as big as 1280x720 at 100 %, scaled up by the compositor | half as big as 1280x720 at 100 % |

`WindowConfig.HighPixelDensity` is on by default. On macOS, Wayland and in the browser the window then gets a render pixel for every pixel of the screen, so a 16 px font loaded at `16 * window.PixelDensity` renders at 32 render pixels and stays sharp. Without it the window renders at its `Size` and the compositor stretches it to the screen. The compositor picks the filter, so the result is often soft. Windows and X11 always size windows in pixels, so the option changes nothing there.

The rule: `Size`, `Position`, the mouse and the display bounds are in window coordinates. `RenderSizeInPixels` is in render pixels, the pixels of the colour target. `PixelDensity` is render pixels per window coordinate. `DisplayScale` is how many render pixels the user wants a unit of content to take: 2 at 200 % on every platform, except for a window without high pixel density on macOS, Wayland and in the browser, where the compositor does the scaling and it is 1.

- A game that draws in render pixels, such as a pixel art game that scales its scene by an integer factor, reads `RenderSizeInPixels` as before and gets more pixels on a scaled display.
- A game that wants its content as big on every display multiplies its sizes by `DisplayScale`. On Windows and X11 that includes the window size: `window.Size = new Size<uint>((uint)(1280 * window.DisplayScale), (uint)(720 * window.DisplayScale))` after the window exists.
- The mouse reports window coordinates. A game that hit-tests it against something drawn in render pixels converts it first: `Vector2 renderPosition = mouseState.Position * window.PixelDensity;`.
- `Pixely.Ui` maps the mouse from window coordinates to render pixels itself (see [ui.md](ui.md)). Its `scale` is render pixels per logical pixel, so `UseUi(scale: 2f)` makes it as big at 200 % as at 100 %, drawn as 2x2 blocks.

`Window.ScaleChanged` is raised when `PixelDensity` or `DisplayScale` changes, such as when the user moves the window to a display with another scale or changes the scale in the settings. `ResolutionChanged` is raised as well when the render size changes with it.

Headless mode ignores the option, so a screenshot has the window's `Size` in pixels on every platform, and its window's `DisplayScale` is always 1.

## Displays

`IDisplayService` reads the connected displays from the app's service provider, with or without a window:

```csharp
IDisplayService displayService = provider.GetRequiredService<IDisplayService>();
Display primary = displayService.GetPrimaryDisplay();
Rectangle bounds = primary.Bounds;
Rectangle usableBounds = primary.UsableBounds;
float scale = primary.Scale;
IReadOnlyList<Display> displays = displayService.GetDisplays();
Display current = window.Display;
```

- `Bounds` is the display's area in desktop coordinates, the ones `Window.Position` uses. `UsableBounds` leaves out what the desktop reserves, such as a taskbar, a dock or a menu bar. SDL cannot read what Wayland reserves, so there `UsableBounds` equals `Bounds`.
- `Scale` is the scale the user set for the display: 2 at 200 % on every platform. It is the `DisplayScale` a window with high pixel density gets on the display. Platforms report it in one of two parts: macOS, Wayland and the browser as `PixelDensity`, Windows and X11 as `ContentScale`. `Scale` is their product. In the browser SDL reads `PixelDensity` once at startup, so after a page zoom or a move to another monitor only the window's `PixelDensity` and `DisplayScale` have the new value.
- `Window.Display` is the display SDL says the window is on. That is usually the one holding the window's centre; on Wayland it is the last display the window entered.
- A `Display` is a snapshot. Each call reads the displays again, because the user can connect, move or rescale a display while the app runs. `Display.Id` tells two snapshots of one display apart from another display's. SDL gives a reconnected display a new ID, so an ID lasts one run at most.
- Displays are read on the main thread only. Another thread gets a `PixelyException`.

A root window is created when the app is built, so its `WindowConfig` cannot come from `IDisplayService`. A window registered by a stage can, because the root's provider exists by then.

## Window placement

`WindowConfig.Position` says where a window opens. Without it the platform decides.

```csharp
// Centred on the primary display.
new WindowConfig(Size: (1280, 720), Position: WindowPosition.Centered());

// Centred on a given display.
new WindowConfig(Size: (1280, 720), Position: WindowPosition.Centered(display));

// Top-left corner at a point in desktop coordinates.
new WindowConfig(Size: (1280, 720), Position: WindowPosition.At(new Vector2Int(100, 100)));
```

A centred window sits in the middle of the display's usable bounds, or of its full bounds when it is larger than the usable ones. A display disconnected since it was read centres the window on the primary display instead. A fullscreen window opens on the display its position names.

Wayland does not let a window place itself, so it ignores `Position` and setting `Window.Position`; `Window.SupportsSetWindowPosition` is false there. Headless mode ignores the position too.

X11 and Wayland can apply a new `Size` or `Position` later than the call that set it, so reading it right away can return the old value. `Window.Sync()` waits until the platform has applied the window's pending changes, and returns false when the wait timed out:

```csharp
window.Size = new Size<uint>(1600, 900);
window.Sync();
Size<uint> size = window.Size;
```

## Dragging and minimising a borderless window

A window created with `Borderless: true` has no title bar, so it draws its own controls.

`Window.Minimize()` minimises the window, as a minimise button would. The user restores it from the task bar or the Dock. Windows, macOS and X11 minimise it. Wayland minimises it only when the compositor offers minimising; otherwise nothing happens and the call still returns true. It returns false where SDL cannot minimise, such as in the browser and on the dummy video driver of Pixely.Testing. A hidden window is minimised when it is shown. A minimised window is not renderable, so its renderers do not run until it is restored. In headless mode the window stays hidden, so `Minimize()` changes nothing and the window keeps rendering.

`Window.Draggable = true` lets the user drag the window by any point. `Window.SetHitTest` decides point by point instead. Its callback gets a point and returns `HitTestResult.Draggable` or `HitTestResult.Normal`. This one drags the window by its top 24 rows:

```csharp
window.SetHitTest(point => point.Y < 24 ? HitTestResult.Draggable : HitTestResult.Normal);
```

Setting either one replaces the other. `Draggable = false` and `SetHitTest(null)` both turn dragging off.

- Only the left button drags. The platform moves the window itself, so a left press on a draggable point never reaches the app: `IMouseService` raises no press and no release for it, and a UI never sees it. Presses of other buttons reach the app as usual.
- The point is in window coordinates, the same as the positions of mouse events: units of `Window.Size`, not render pixels.
- The callback runs on the main thread while SDL processes events, which is mostly at the start of a frame, before the updatables. SDL calls it whenever the pointer moves over the window, not only on a press, so keep it cheap.
- An exception thrown from the callback ends the process, because it cannot pass through SDL.
- On Windows and macOS the app gets no pointer motion over a draggable point. Windows reports the pointer as leaving the window instead. macOS reports nothing, so whatever was hovered last stays hovered until the pointer comes back.
- `Draggable` and `SetHitTest` throw `PixelyException` where SDL has no hit testing, such as in the browser and on the dummy video driver of Pixely.Testing. Synthetic input in headless mode does not go through the hit test, so a synthetic press on a draggable point reaches the app.

A window that draws its controls with Pixely.Ui can drag from every point where the UI would not take a press. `UiRoot.TakesPointerAt` answers that for a point in window coordinates and accounts for `UiRoot.Scale`:

```csharp
builder.OnBuilt(provider =>
{
    Window window = provider.GetWindow();
    UiRoot root = provider.GetUiRoot();
    window.SetHitTest(point => root.TakesPointerAt(window, point) ? HitTestResult.Normal : HitTestResult.Draggable);
});
```

A press on a button then reaches the button, and a press on empty space drags the window. `TakesPointerAt` reads the layout of the UI's last build and routes nothing. It counts only pointer targets that can take input: a disabled or hidden button, a label, a panel and a scroll view's background all drag the window. While the UI holds a press, such as a button pressed and dragged off, it takes the pointer everywhere, so the release reaches the UI. A press on empty space normally takes focus away and commits a text field; a press that drags the window does not. A minimise button calls `window.Minimize()` from its `Clicked` handler.

## The browser

In a browser the page is the screen: the window fills it and follows the browser window's size, so `WindowConfig.Size` is ignored, as are `Position`, `Fullscreen`, `Resizable`, `Transparent`, `Borderless`, `AlwaysOnTop` and `Utility`. `HighPixelDensity` applies: the canvas then has the page's device pixels, its CSS size times `window.devicePixelRatio`. `Window.Size` reports the page size and resizes arrive through `ResolutionChanged` as on the desktop.

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

`RenderCoordinator`, not the provider, acquires each frame's command buffer and swapchain texture, and passes them to `CreateRenderContext` in a `FrameContext` together with the window. One provider can serve several windows, such as two that use `UseDefaultRendering`, so `FrameContext.Window` tells them apart. The coordinator disposes the context after the renderers and then submits the command buffer itself, so neither the context nor a renderer submits or cancels it. The coordinator skips a window whose `IsRenderable` is false: it acquires no frame for it, calls neither the provider nor the renderers, and submits only the pending uploads, so their buffers are not cycled into new copies on every update while the window is hidden. By default a hidden or minimized window is not renderable. When no swapchain texture comes back, the coordinator does not call the provider. On the desktop it submits the command buffer instead of cancelling it: with a claimed window, SDL's Vulkan backend defragments GPU memory and frees empty allocations only on a submit that requested a swapchain texture. In the browser it cancels the command buffer and leaves pending uploads for the next drawn frame: the WebGPU fork returns no texture while its submissions in flight reach the frame limit, so another submission would only hold a slot. A different acquire policy needs a custom `IRenderCoordinator`. `Window` is abstract, and `ColorTargetFormat` and `TryWaitAndAcquireSwapchainTexture` belong to the window that presents its frames. `SwapchainWindow`, the window of a normal run, hands out the swapchain image of a window claimed for the GPU device. `OffscreenWindow`, which every window becomes in headless mode (`Pixely.Headless`), hands out a texture instead while the SDL window stays hidden and unclaimed, so a custom provider written against `Window` works offscreen unchanged. See headless.md.

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
coordinators request no swapchain texture and do not invoke renderers. When
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
