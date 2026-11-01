# Window rendering

Every Pixely app has exactly one window and one GPU device. `PixelyAppBuilder.Build()` creates both.

## The window

`ConfigureWindow` sets the window's size, title and other options. Without it the window gets the defaults of
`WindowConfig`. A later call replaces the settings of an earlier one:

```csharp
PixelyAppBuilder builder = new();
builder
    .ConfigureWindow(new WindowConfig(Size: new Size<uint>(1280, 720), Title: "Game"))
    .UseDefaultRendering();
```

Code that needs the window takes `Window` from the container:

```csharp
public static GameSystem Create(Window window, IMouseService mouseService) => new(window, mouseService);
```

The window's services need no window argument either:

```csharp
graphicsPipelineBuilder.AddColorFormatFromDisplay();
textInputService.Start();
bool containsMouse = mouseService.IsInWindow();
```

Closing the window quits the app.

`Window.Show()`, `Raise()` and `Hide()` return whether the native window operation succeeded. Raising a window requests
input focus, subject to the operating system's window-management policy. `WindowConfig.InitiallyVisible` controls the
initial visibility; it does not defer native window creation.

## Rendering

`UseDefaultRendering()` registers the render coordinator, which draws the window every frame with
`IRenderer<BasicRenderContext>` renderers:

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

Exactly one render coordinator runs each frame. Either the app's `Configure` registers it, and stages only add
renderers, or every stage registers its own, and the root registers none. The frame loop checks this at the start of each
frame, right after loading a stage, and throws `PixelyInitializationException` when there is no coordinator or more than
one. A stage that draws nothing, such as a loading screen, still registers one:

```csharp
stageManager.Load(static stage => stage.UseDefaultRendering());
```

The coordinator is what keeps the GPU healthy, so it runs even for a window that draws nothing. It submits the uploads
`GpuMemorySystem` records, and on the desktop it requests a swapchain texture every frame. SDL's Vulkan backend frees
finished GPU work only on the submit of a command buffer that requested a swapchain texture, or on a fence wait.

## The GPU device

The GPU device and the services that need it (`GpuMemorySystem`, the shader loaders, `ITextureLoader`, the pipeline
builders and `IFontSystem`) are registered by `Build()`. A device registered before `Build()`, such as the stub of
`Pixely.Testing`, is kept, and the services are added next to it when they are missing.

The window is claimed for the device and gets an sRGB swapchain (`SDR_LINEAR` with vsync): what shaders write is linear light, and the GPU encodes it to sRGB on write (see [shaders.md](shaders.md#color-spaces)). Its `ColorTargetFormat` is `B8G8R8A8UnormSrgb` or `R8G8B8A8UnormSrgb`, whichever the driver has. A driver without an sRGB swapchain for the window fails window creation with `PixelyInitializationException`; there is no fallback, because it would need every shader to encode as well.

## Waiting for uploads

The render coordinator submits the frame's uploads before the frame's command buffer. On the Raspberry Pi 5's V3DV driver that
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
| `On` | Always. In the browser, `Build()` fails with `PixelyInitializationException`, because it cannot wait on a fence. |
| `Off` | Never. |

`PIXELY_UPLOAD_WAIT` overrides it (see [headless.md](headless.md#environment-variables)), for example to check whether a
newer Mesa still needs the wait. The diagnostics' `GPU device:` line says whether it is on (see [diagnostics.md](diagnostics.md)).

## The browser

In a browser the page is the screen: the window fills it and follows the browser window's size, so `WindowConfig.Size` is ignored, as are `Fullscreen`, `Resizable`, `Transparent`, `Borderless` and `AlwaysOnTop`. `Window.Size` reports the page size and resizes arrive through `ResolutionChanged` as on the desktop.

A browser owns the frame loop, so `Pixely.App.BrowserHost.RunAsync` calls `IPixelyApp.RunFrame()` once per animation frame instead of `Run()`, which loops over it until it returns false. The generated entry point awaits it for `browser-wasm` (see [Hosting](hosting.md)).

## Custom render contexts

`UseWindowRendering<T>` registers the coordinator with a render context of the application's own, with resources such as
depth targets or cameras:

```csharp
PixelyAppBuilder builder = new();
builder
    .ConfigureWindow(new WindowConfig(Size: new Size<uint>(1280, 720), Title: "Game"))
    .UseWindowRendering<GameRenderContext>();

builder.AddSingleton<GameRenderContextProvider>(GameRenderContextProvider.Create);
builder.AddAlias<RenderContextProvider<GameRenderContext>, GameRenderContextProvider>();
```

The provider uses ordinary dependency injection, including static factory registration:

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

`RenderCoordinator`, not the provider, acquires each frame's command buffer and swapchain texture, and passes them to `CreateRenderContext` in a `FrameContext` together with the window. The coordinator disposes the context after the renderers and then submits the command buffer itself, so neither the context nor a renderer submits or cancels it. On the desktop the coordinator acquires a frame every frame, even while the window is not renderable, but calls the provider and the renderers only while the window's `IsRenderable` is true; a frame nobody sees is submitted without a context. In the browser it skips the frame while the window is not renderable: SDL's browser driver does not hide the canvas, so presenting an undrawn texture would blank it. By default a hidden or minimized window is not renderable. When no swapchain texture comes back, the coordinator does not call the provider. On the desktop it submits the command buffer instead of cancelling it: SDL's Vulkan backend frees finished GPU work only on a submit that requested a swapchain texture. In the browser it cancels the command buffer and leaves pending uploads for the next drawn frame: the WebGPU fork returns no texture while its submissions in flight reach the frame limit, so another submission would only hold a slot. When the window draws no frame, the desktop frame loop waits up to 16 ms for an event, so the app does not spin; in the browser, `requestAnimationFrame` paces the frames.

`Window` is abstract, and `ColorTargetFormat` and `TryWaitAndAcquireSwapchainTexture` belong to the window that presents its frames. `SwapchainWindow`, the window of a normal run, hands out the swapchain image of the window claimed for the GPU device. `OffscreenWindow`, the window in headless mode (`Pixely.Headless`), hands out a texture instead while the SDL window stays hidden and unclaimed, so a custom provider written against `Window` works offscreen unchanged. See headless.md.

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

`BasicRenderContext.Dispose` is virtual and does nothing, so a derived context can add per-frame cleanup; the coordinator
submits the command buffer after disposing the context.

## Custom render coordinators

A different acquire policy needs a custom `IRenderCoordinator`, registered in place of `UseDefaultRendering()` or
`UseWindowRendering<T>()`. It takes over everything the built-in coordinator does. On the desktop, every frame it acquires a
command buffer, requests a swapchain texture on it, submits the pending uploads of `GpuMemorySystem`, and then submits that
same command buffer, even when no texture comes back. Otherwise the uploads never run, every buffer update makes a new
full-size copy of the buffer, and SDL's Vulkan backend keeps finished GPU work until a fence wait.

## Activating mouse clicks

Clicking an unfocused window activates it. SDL discards that click by default, so the first click after switching to the
app's window is lost. Pixely delivers it instead: it enables `SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH` during initialization.
Register `PixelyConfig` with `DeliverActivatingMouseClicks: false` to restore the SDL default:

```csharp
builder.AddSingleton(new PixelyConfig(DeliverActivatingMouseClicks: false));
```

Either way, `IMouseService` only raises `ButtonRelease` for a button whose press it saw, so a suppressed
press never leaves a release behind it.

## User interface

See `docs/ui.md` for `Pixely.Ui`, which draws into the window through a renderer of its own.
