# Testing a game, its renderers, input and text without devices

`Pixely.Testing` runs a game's own app in its tests ([The game's app](#the-games-app)), and gives them a GPU device without a GPU, a keyboard and gamepads that a test presses ([Input](#input)), and a font system without font files ([Fonts](#fonts)). `StubGpuDevice` is a `GpuDevice`: the game's renderers, `GpuMemorySystem`, the shader and texture loaders, `GraphicsPipelineBuilder`, `RenderPassBuilder` and Pixely.Ui run on it unchanged, Pixely's validation runs as on the GPU, and the stub records what reaches it. Nothing is rendered. Under [peach-architecture.md](peach-architecture.md) `Foo.Game` knows no output, so its tests need none of this; it is for `Foo.Frontend.Rendering` and the frontend code that renders. It is built for the desktop only, and the assembly is marked unsupported in the browser, so a call to it there is a CA1416 warning.

## The game's app

`TestApp.CreateBuilder()` returns a `PixelyAppBuilder` that a test hands to the game's own `Configure` (hosting.md), so the test builds the app the game builds, without a GPU, a display, a sound card or a controller. `TestInput` sends it input:

```csharp
PixelyAppBuilder builder = TestApp.CreateBuilder();
Program.Configure(builder);
using IPixelyApp app = builder.Build();

TestInput input = app.GetRequiredService<TestInput>();
input.KeyPress(Scancode.Space);
input.MouseClick(MouseButton.Left, new Vector2(40, 20));
app.RunFrame();
```

- The game makes `Program` and `Configure` public, and its test project references the game's project, so the test can call `Configure`. The content, shaders included, loads from the test's output directory, so the test project needs the game's content there.
- The app renders on the stub GPU device ([The stub GPU device](#the-stub-gpu-device)), with every window a `StubWindow`. `CreateBuilder(options)` passes `StubGpuDeviceOptions` to it ([Options](#options)).
- SDL uses its dummy video and audio drivers, at default priority, so `SDL_VIDEO_DRIVER` and `SDL_AUDIO_DRIVER` in the environment override them. `Pixely.Audio` then plays into nothing.
- The app ignores the machine's gamepads and the OS's theme, as in headless mode (headless.md): a pad plugged into the machine and a dark theme do not reach the test, and `IPlatformSettings.Theme` is `Unknown`.
- Game time starts at 0 and advances by 1/30 s per frame, however fast the frames run. Three `RunFrame` calls make 2/30 s of `ElapsedNanoseconds`, since the first frame has no previous one.
- A frame that no window drew, for example one whose windows are hidden, does not wait for the display, so a test with a hidden window runs its frames at once.
- No console reads standard input: `AddHeadlessSupport()` in `Configure` adds nothing, even with `PIXELY_HEADLESS` set, and `PIXELY_COMMAND_CONSOLE` has no effect.

`TestInput` is the synthetic input of headless mode:

- A call is queued, and the queued calls reach the game at `UpdateOrders.Input` of the next `RunFrame`, in the order they were made. That frame changes the stage first, so input sent before a stage change reaches the new stage. Input sent from a handler during that frame waits for the frame after.
- Each view has its own mouse and keyboard, and a call without a `ViewScope` goes to the main view. Mouse calls take window coordinates, as headless mode's do.
- `KeyPress` presses and releases a key, `MouseClick` moves the mouse, presses and releases a button, and `TextInput` types text. `KeyDown`, `KeyUp`, `MouseDown`, `MouseUp`, `MouseMoveTo`, `MouseMoveBy`, `MouseWheel` and `MouseLeave` do one step each.
- There is one gamepad, which `GamepadConnect` connects and `GamepadDisconnect` disconnects. `GamepadButtonDown`, `GamepadButtonUp` and `GamepadButtonPress` press its buttons. `GamepadLeftStick` and `GamepadRightStick` take each axis from -1 to 1, with Y growing downwards, and `GamepadLeftTrigger` and `GamepadRightTrigger` take 0, at rest, to 1, fully pulled.
- A scancode, mouse button or gamepad button outside its enum, and a stick or trigger value out of range or NaN, throw `ArgumentOutOfRangeException` at the call. A view without a window, and a gamepad call without a connected gamepad or a second `GamepadConnect`, throw `InvalidOperationException` out of the `RunFrame` that runs them.
- Event timestamps follow game time.

## The stub GPU device

`TestApp.CreateBuilder()` registers the stub as the app's `GpuDevice`, with every GPU service on top of it, so `UseGpu`, `UseDefaultRendering` and the game's registrars that call them keep the stub. Every window is a `StubWindow`: an SDL window created as its `WindowConfig` says, without a swapchain. Each frame it hands out a new `SwapchainTexture` of its size at once, with no wait, so a frame submits only its own command buffer. Its events, size and visibility work as on any window, and a hidden window is not renderable. Building still initializes SDL video, with the dummy driver of [The game's app](#the-games-app); with another driver from `SDL_VIDEO_DRIVER` the windows show. The choice lasts as long as the app, so another app in the same test process, on the GPU or not, is unaffected.

```csharp
PixelyAppBuilder builder = TestApp.CreateBuilder();
builder.UseDefaultContent();
builder.AddFooRendering();   // the game's own registrar, which calls UseDefaultRendering
using IPixelyApp app = builder.Build();

app.RunFrame();

StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();
StubCommandBuffer frame = device.SubmittedCommandBuffers.Single(commandBuffer => commandBuffer.RenderPasses.Count > 0);
StubDraw draw = frame.RenderPasses.Single().Draws.Single();
Assert.That(draw.Pipeline, Is.SameAs(spriteRenderer.Pipeline));
Assert.That(draw.FragmentSamplers[0].Texture, Is.SameAs(spriteRenderer.Atlas));
Assert.That(draw.ReadVertexUniform<Matrix4x4>(0), Is.EqualTo(expectedCamera));
```

Shaders load from the content as in the game, so the test project needs the compiled shaders, metadata included. The stub reads the metadata, whose binding layout the validation checks draws against, and supports every shader format by default.

A renderer can also be called without a frame: `StubGpuDevice.CreateSwapchainTexture(size)` makes the target of a `BasicRenderContext`, and `device.AcquireCommandBuffer()` returns a `StubCommandBuffer` to record into and submit.

## What is recorded

- `SubmittedCommandBuffers`: every submitted `StubCommandBuffer`, oldest first, until `ClearSubmittedCommandBuffers()`. Besides the frames this includes the command buffers `GpuMemorySystem` submits its uploads with. A cancelled command buffer is not submitted; it has `IsCancelled`.
- A `StubCommandBuffer` holds its `RenderPasses`, `ComputePasses` and `Blits` in recording order.
- A `StubRenderPass` holds its `ColorTargets`, `ColorTargetSettings`, `ResolveTextures` (one per color target, null for none), `DepthBuffer`, `DepthBufferSettings` and `Draws`.
- A `StubDraw` holds the state current when it was recorded: the pipeline, the vertex buffers, sampler bindings and storage buffers by slot, the index buffer of an indexed draw, the vertex, index and instance counts, the scissor clipped to the pass's target (null when the pass never set one) and the stencil reference. `ReadVertexUniform<T>(slot)` and `ReadFragmentUniform<T>(slot)` return the value pushed last before the draw and throw when nothing was pushed or its size is not that of `T`. A slot bound earlier in the pass and not bound again keeps its binding, as on the GPU.
- A `StubComputePass` holds its read-write bindings and `Dispatches`; a `StubDispatch` holds the pipeline, the group counts or the indirect buffer, the bindings by slot, and `ReadUniform<T>(slot)`.

Only calls that pass Pixely's validation are recorded, and a call that fails it throws as on the GPU: a draw whose pipeline's shader expects a uniform the frame never pushed throws `ShaderBindingLayoutValidationException`, a sample count mismatch throws `InvalidOperationException`.

## Contents

The stub keeps what is uploaded. An upload reaches the contents when its command buffer is submitted, which `GpuMemorySystem.Submit()` and every frame do, and is dropped when it is cancelled. `ReadVertexBuffer`, `ReadStorageBuffer` and `ReadIndexBuffer` return the buffer's current `Size` elements from its start, 16-bit indices widened to `uint`. `ReadTexture(texture, layer)` returns the layer's uploaded bytes, or zeros for a layer nothing was uploaded to, such as a color target; `SubmitAndDownloadTexture` returns the same. Reading a released resource throws `ObjectDisposedException`.

## Options

`StubGpuDevice.Create(options)` and `TestApp.CreateBuilder(options)` take a `StubGpuDeviceOptions`. By default the stub supports every shader format, texture format and sample count; `ShaderFormats`, `IsTextureFormatSupported` and `IsSampleCountSupported` narrow them, for a test of a fallback.

## What the stub cannot tell

It runs no shader, so it cannot show what a frame looks like or catch a shader bug, SDL's own validation or a driver's. It does not check a texture's usage against its binding, which Pixely does not check either. Its fences are signalled at once. It runs on the desktop build of Pixely only, not the browser one.

## Input

`StubKeyboardService` is an `IKeyboardService` and `StubGamepadService` an `IGamepadService`, for frontend code that takes them. A test presses keys and buttons through Pixely's own keyboard and gamepad services, so the code under test gets the events the game gets:

```csharp
StubKeyboardService keyboard = new();
using PauseMenu pauseMenu = new(keyboard);

keyboard.Down(Scancode.LeftCtrl);
keyboard.Press(Scancode.G);

StubGamepadService gamepads = new();
using PlayerController controller = new(gamepads);
Gamepad pad = gamepads.Connect();

gamepads.Press(pad, GamepadButton.Start);
gamepads.MoveLeftStick(pad, new Vector2(1f, 0f));
gamepads.Disconnect(pad);
```

- `Down` presses and holds, `Up` releases, and `Press` does both. They raise the events at once, on the calling thread, with no frame.
- Handlers run in their order, and a consumed event stops at the handler that consumed it.
- `KeyEventArgs.Keyboard` holds the keys held, so `Ctrl` and `Shift` follow `Down` and `Up`. A `Down` on a held key is a repeat. A key's `VirtualKey` is SDL's for the scancode; before SDL starts, that is the US layout on every machine, for example `Scancode.G` is `VirtualKey.G`.
- A key goes to the main view unless the call names a `ViewScope`, which reaches only the handlers subscribed for that view.
- `Connect()` returns a new `Gamepad` with its own `DeviceId`, so a test can connect several. No gamepad is connected before it.
- A `Down` on a held button raises nothing, as on a real gamepad.
- `MoveLeftStick` and `MoveRightStick` take each axis from -1 to 1, with Y growing downwards. The game sees the position through Pixely's dead zone: a stick moved less than 0.2 from the center reads as at rest, and a full move reads as 1. A move raises one event, or none when what the game sees stays the same, for example `(0.1, 0)` from rest.
- `MoveLeftTrigger` and `MoveRightTrigger` take 0, at rest, to 1, fully pulled. A value the trigger already has raises nothing.
- `Disconnect` releases the held buttons and returns the sticks and triggers to rest before `GamepadDisconnected`, as SDL reports an unplug.
- A gamepad that is not connected to the stub throws `InvalidOperationException`. A scancode or button outside its enum, `GamepadButton.Invalid` and `GamepadButton.Count` among them, and a stick or trigger value out of range or NaN throw `ArgumentOutOfRangeException`.
- A call from one of the stub's own event handlers throws `InvalidOperationException`, because the handlers after it would get the new event in place of theirs.
- Every event's `Timestamp` is 0.

Neither stub needs an app, a window or SDL started, so a test creates it with `new` and passes it to the code under test.

## Fonts

`StubFontSystem` is an `IFontSystem` that reads no font file. Every character measures the same, so a test knows the size of any text:

```csharp
using StubFontSystem fontSystem = new(glyphWidth: 6, lineHeight: 11);
Font font = fontSystem.Load("fonts/Montserrat-Medium.ttf", 20);
UiRoot root = new() { Style = new UiStyle(font) };
```

- `Load` returns a real `Font`, the same one for the same path, size, rasterization, hinting and style, as Pixely's font system does. The path is never read, and the size does not change the metrics.
- Text is as wide as its longest line's characters times `glyphWidth`, and as high as its lines times `lineHeight`. Only `\n` starts a line; `\r`, spaces and tabs are characters like any other. A character is a Unicode scalar value, so an emoji is one. Empty text measures 0 × 0. The defaults are 8 and 16. "Score" measures 30 × 11 with the metrics above. A size over 65535 pixels throws `OverflowException`, where Pixely's font system would truncate it.
- `CreateTextSprite` returns a sprite of the measured size on a blank `R8G8B8A8UnormSrgb` texture, the same sprite for the same text and font, so a `Label` lays out and paints as in the game. As in Pixely's font system, the texture is borrowed, so disposing it does nothing, and empty text throws `PixelyException`.
- The textures are on the `StubGpuDevice` passed as `gpuDevice`, so a test reads a bound text texture back with `ReadTexture` on its own device. Without one, the stub creates a device of its own and disposes it with itself.
- `ReleaseFont`, which `Font.Dispose()` calls, records the font in `ReleasedFonts`. A later `Load` with its path, size and settings returns a new font. `ReleasedFonts` is a copy taken when it is read.
- Releasing a font again, including a second `Font.Dispose()` or one after the stub is disposed, does nothing. Releasing a font another font system loaded throws `InvalidOperationException`. Measuring or rasterizing with a released font throws `ObjectDisposedException`, because in the game its SDL font is closed.
- Disposing the stub disposes its textures, and its device if it created one. After that, `Load`, `CreateTextSprite` and measuring throw `ObjectDisposedException`; `ReleasedFonts` and releasing still work.

Its sizes are not a real font's. Real glyphs differ in width. SDL_ttf measures a trailing `\n` differently, and in text of several lines it leaves out a `\r` before `\n` and the spaces and tabs at the end of each line.

