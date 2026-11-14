# Testing a game, its renderers, input and text without devices

`Pixely.Testing` runs a game's own app in its tests ([The game's app](#the-games-app)), and gives them a GPU device without a GPU, a keyboard and gamepads that a test presses ([Input](#input)), a font system without font files ([Fonts](#fonts)), and an audio system that plays nothing ([Audio](#audio)). `StubGpuDevice` is a `GpuDevice`: the game's renderers, `GpuMemorySystem`, the shader and texture loaders, `GraphicsPipelineBuilder`, `RenderPassBuilder` and Pixely.Ui run on it unchanged, Pixely's validation runs as on the GPU, and the stub records what reaches it. Nothing is rendered. Under [peach-architecture.md](peach-architecture.md) `Foo.Game` knows no output, so its tests need none of this; it is for `Foo.Frontend.Rendering` and the frontend code that renders. Under [orange-architecture.md](orange-architecture.md) a test of a system alone needs none of this ([Testing a system](#testing-a-system)); it is for the renderers, the views and the game's app as a whole. It is built for the desktop only, and the assembly is marked unsupported in the browser, so a call to it there is a CA1416 warning.

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
- The app never starts SDL. Its windows, display, cursors, clipboard and platform settings are stubs without a platform ([Windows](#windows)), its text uses `StubFontSystem` ([Fonts](#fonts)) and its sound `StubAudioSystem` ([Audio](#audio)). A test that needs a real window, real fonts or real sound uses headless mode (headless.md).
- The app ignores the machine's gamepads, theme and locales: a pad plugged into the machine and a dark theme do not reach the test, `IPlatformSettings.Theme` is `Unknown`, and `GetPreferredLocales()` is empty. Its clipboard is its own, so a test never reads or replaces the machine's.
- Game time starts at 0 and advances by 33,333,334 ns per frame, 1/30 s rounded up, however fast the frames run. Three `RunFrame` calls make 66,666,668 ns of `ElapsedNanoseconds`, since the first frame has no previous one.
- A frame that no window drew, for example one whose windows are hidden, does not wait for the display, so a test with a hidden window runs its frames at once.
- No console reads standard input: `AddHeadlessSupport()` in `Configure` adds nothing, even with `PIXELY_HEADLESS` set, and `PIXELY_COMMAND_CONSOLE` has no effect.
- Several apps can exist at once, each on its own thread, so tests that build them can run in parallel. One app is used by one thread at a time; an `async` test may continue on another thread after an `await`.
- NUnit runs fixtures one at a time unless the test assembly says otherwise, so a game's test project adds `[assembly: Parallelizable(ParallelScope.Fixtures)]` to run them in parallel, as Pixely's own do.
- A fixture that uses state of the whole process is `[NonParallelizable]`, so NUnit runs it alone: one that starts real SDL, as a headless app or an app from a plain `PixelyAppBuilder` does; one that uses Pixely's font system, because SDL_ttf closes fonts without a lock; and one that sets an environment variable an app reads, such as `PIXELY_DIAGNOSTICS`. While real SDL shuts down it also clears state that stub apps read, such as its keyboard layout.

`TestInput` is the synthetic input of headless mode:

- A call is queued, and the queued calls reach the game at `UpdateOrders.Input` of the next `RunFrame`, in the order they were made. That frame changes the stage first, so input sent before a stage change reaches the new stage. Input sent from a handler during that frame waits for the frame after.
- Each view has its own mouse and keyboard, and a call without a `ViewScope` goes to the main view. Mouse calls take window coordinates, as headless mode's do.
- `KeyPress` presses and releases a key, `MouseClick` moves the mouse, presses and releases a button, and `TextInput` types text. `KeyDown`, `KeyUp`, `MouseDown`, `MouseUp`, `MouseMoveTo`, `MouseMoveBy`, `MouseWheel` and `MouseLeave` do one step each.
- There is one gamepad, which `GamepadConnect` connects and `GamepadDisconnect` disconnects. `GamepadButtonDown`, `GamepadButtonUp` and `GamepadButtonPress` press its buttons. `GamepadLeftStick` and `GamepadRightStick` take each axis from -1 to 1, with Y growing downwards, and `GamepadLeftTrigger` and `GamepadRightTrigger` take 0, at rest, to 1, fully pulled.
- A scancode, mouse button or gamepad button outside its enum, and a stick or trigger value out of range or NaN, throw `ArgumentOutOfRangeException` at the call. A view without a window, and a gamepad call without a connected gamepad or a second `GamepadConnect`, throw `InvalidOperationException` out of the `RunFrame` that runs them.
- Event timestamps follow game time.

## The stub GPU device

`TestApp.CreateBuilder()` registers the stub as the app's `GpuDevice`, with every GPU service on top of it, so `UseGpu`, `UseDefaultRendering` and the game's registrars that call them keep the stub. Every window is a `StubWindow` ([Windows](#windows)). Each frame it hands out a new `SwapchainTexture` of its size at once, with no wait, so a frame submits only its own command buffer.

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

## Windows

A `StubWindow` is a window without a platform, on one display of 1024 × 768 at (0, 0) and scale 1, as SDL's dummy video driver reports it:

- It opens at its `WindowConfig`'s `Size`, 640 × 480 without one, or at the display's size when `Fullscreen` is set. It opens at its `Position`, or centred on the display without one or with `WindowPosition.Centered`. Its `PixelDensity` and `DisplayScale` are 1, so `RenderSizeInPixels` equals `Size`.
- A new `Size` reads back at once. `ResolutionChanged` follows in the next `RunFrame`, before any update, as on the desktop.
- `SetFullscreenBorderless(true)` covers the display, with a `ResolutionChanged` in the next frame, and `false` restores the window's size and position. A window created fullscreen restores to 640 × 480 or its configured size.
- It is renderable while it is visible. `Show` and `Hide` change that at once. `Minimize` returns false and does nothing, so a test hides a window to make it not renderable.
- `AlwaysOnTop`, `MouseGrab`, `WindowRelativeMouseMode`, `Draggable`, `Position` and text input keep what was set, also on a hidden window. `SetHitTest` and `SetIcon` are accepted. `SetShape` throws `PixelyException` unless the window was created with `Transparent`.
- `ShowModalMessageBox` throws `PixelyException`, and the file dialogs return a result with `FileDialogStatus.Error`.
- `ICursorService` checks cursors as on the desktop and keeps the cursor's visibility. It creates system cursors too, which SDL's dummy driver cannot. `IMouseService.GetGlobalState()` is at (0, 0) with no buttons.

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

It runs no shader, so it cannot show what a frame looks like or catch a shader bug, SDL's own validation or a driver's. Text sprites of the app's `StubFontSystem` are created directly on the device, so no upload of text reaches `GpuMemorySystem` or its contents. It does not check a texture's usage against its binding, which Pixely does not check either. Its fences are signalled at once. It records blits without carrying them out, so a texture a blit writes to reads back without the blit's result. It runs on the desktop build of Pixely only, not the browser one.

## Input

`StubKeyboardService` is an `IKeyboardService` and `StubGamepadService` an `IGamepadService`, for views, renderers and other code outside systems that take them. A test presses keys and buttons through Pixely's own keyboard and gamepad services, so the code under test gets the events the game gets:

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

`StubFontSystem` is an `IFontSystem` that reads no font file. Every character measures the same, so a test knows the size of any text. An app from `TestApp.CreateBuilder()` has one with the default metrics on its `StubGpuDevice`, so its text measures 8 × 16 per character, and a test can also create one itself:

```csharp
using StubFontSystem fontSystem = new(glyphWidth: 6, lineHeight: 11);
Font font = fontSystem.Load("fonts/Montserrat-Medium.ttf", 20);
UiRoot root = new() { Style = new UiStyle(font) };
```

- `Load` returns a real `Font`, the same one for the same path, size and settings, as Pixely's font system does. The path is never read. It checks the size, variations, features and letter spacing as Pixely's font system does, but not against a font file: a variation of an axis the font lacks, or a fractional size of a bitmap font, loads here and throws in the game. Neither the size nor any setting changes the metrics.
- Text is as wide as its longest line's characters times `glyphWidth`, and as high as its lines times `lineHeight`. Only `\n` starts a line; `\r`, spaces and tabs are characters like any other. A character is a Unicode scalar value, so an emoji is one. Empty text measures 0 × 0. The defaults are 8 and 16. "Score" measures 30 × 11 with the metrics above. A size over 65535 pixels throws `OverflowException`, where Pixely's font system would truncate it.
- `CreateTextSprite` returns a sprite of the measured size on a blank `R8G8B8A8UnormSrgb` texture, the same sprite for the same text and font, so a `Label` lays out and paints as in the game. As in Pixely's font system, the texture is borrowed, so disposing it does nothing, and empty text throws `PixelyException`.
- The textures are on the `StubGpuDevice` passed as `gpuDevice`, so a test reads a bound text texture back with `ReadTexture` on its own device. Without one, the stub creates a device of its own and disposes it with itself.
- `Font.GetScaled` returns a scaled font as Pixely's font system does, the same one for the same size, so a UI that scales smoothly paints its text as in the game. It measures its `glyphWidth` and `lineHeight` times its `Scale`, each rounded half away from zero as the UI rounds its edges, so at 1.5 the metrics above are 9 and 17.
- `ReleaseFont`, which `Font.Dispose()` calls, records the font in `ReleasedFonts`. A later `Load` with its path, size and settings returns a new font. Releasing a loaded font releases its scaled fonts first, as Pixely's font system does, and records them before it. `ReleasedFonts` is a copy taken when it is read.
- Releasing a font again, including a second `Font.Dispose()` or one after the stub is disposed, does nothing. Releasing a font another font system loaded throws `InvalidOperationException`. Measuring or rasterizing with a released font throws `ObjectDisposedException`, because in the game its SDL font is closed.
- Disposing the stub disposes its textures, and its device if it created one. After that, `Load`, `CreateTextSprite` and measuring throw `ObjectDisposedException`; `ReleasedFonts` and releasing still work.

Its sizes are not a real font's. Real glyphs differ in width. SDL_ttf measures a trailing `\n` differently, and in text of several lines it leaves out a `\r` before `\n` and the spaces and tabs at the end of each line.


## Audio

`StubAudioSystem` is an `AudioSystem` that reads no file and plays nothing. An app from `TestApp.CreateBuilder()` has one, and `RegisterAudio()` in the game's `Configure` keeps it. A test can also create one with `new`:

```csharp
using StubAudioSystem audio = new();
// What the game's code does with its IAudioSystem.
AudioSource jump = audio.CreateSource();
jump.Clip = audio.LoadBuffer("sounds/jump.wav");
jump.Play();

Assert.That(audio.LoadedPath(jump.Clip!), Is.EqualTo("sounds/jump.wav"));
audio.Finish(jump);
Assert.That(jump.State, Is.EqualTo(AudioSourceState.Stopped));
```

- A source keeps the state SDL_mixer gives it. `Play` plays it from any state. `Pause` pauses only a playing source, and `Resume` resumes only a paused one. `Stop` stops it. A group's `Pause`, `Resume` and `Stop` do the same to each of its sources.
- A sound has no length, so it plays until the game stops it or the test calls `Finish(source)`, which stops it as its end would, looping or not. Clearing the clip of a playing source, or disposing the buffer or stream it plays, stops it, where SDL_mixer stops it once its mixer finds nothing to play. A paused source without a clip stays paused and stops when it resumes. A new clip in place of the old one keeps the source's state, as in SDL_mixer.
- What the game set reads back through the objects it already has: a source's `Clip`, `Gain`, `Group`, `Looping` and `Position`, a group's `Gain`, and the listener's `Position` and `Gain`. `LoadedPath(clip)` returns the path a buffer was loaded from or a stream opened from.
- The checks of the audio system apply as in the game: a negative gain, a clip from another audio system, playing without a clip and a disposed object throw. `Finish` and `LoadedPath` throw `ArgumentException` for an object of another audio system.
- No path is read, so a test does not catch a path missing from the content, which throws in the game. `LoadedPath` of a disposed buffer or stream throws `ObjectDisposedException`.
- The app has an `IAudioSystem` even when the game's `Configure` never calls `RegisterAudio()`, so a game that needs audio without registering it passes its tests and fails to build in the game.

## Testing a system

Under [orange-architecture.md](orange-architecture.md) a test of one system builds a world in a `ServiceCollection`, with no app and none of the stubs above. For a game's `MoveSystem(Movers movers, FrameClock clock)`, which moves each body by its `Velocity` times `TimeDelta`:

```csharp
// Spawns what the test asks for in its next Update. It sets up state and applies no rule of the game.
internal sealed class BodySetup(Spawner<Position, Velocity> spawner) : ISystem, IBefore<MoveSystem>
{
    private readonly List<(Position, Velocity)> _pending = [];

    public void Add(Position position, Velocity velocity) => _pending.Add((position, velocity));

    public void Update()
    {
        foreach ((Position position, Velocity velocity) in _pending)
        {
            spawner.Spawn(position, velocity);
        }
        _pending.Clear();
    }
}

internal sealed class Positions : Query<ReadOnlySpan<Position>>;

internal sealed class MoveSystemTests
{
    [Test]
    public void BodyMovesByItsVelocityOverTheStep()
    {
        TestFrameClock clock = new TestFrameClock();
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddSingleton<FrameClock>(clock);
        services.AddQuery<Positions>();
        services.AddSystem<BodySetup>();
        services.AddSystem<MoveSystem>();
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<BodySetup>().Add(new Position(0), new Velocity(2));
        clock.StartTestFrame(500);
        provider.GetRequiredService<SystemScheduler>().Update();

        List<float> positions = [];
        foreach (Columns<ReadOnlySpan<Position>> columns in provider.GetRequiredService<Positions>())
        {
            foreach (Position position in columns.Item1)
            {
                positions.Add(position.Value);
            }
        }
        Assert.That(positions, Is.EqualTo(new[] { 1f }));
    }
}
```

- The test project is on the Pixely SDK like the game ([sdk.md](sdk.md)), so `AddSystem<T>` and the test's queries get the generator. It reaches the game's internal types through `InternalsVisibleTo`.
- The test registers what `AddGame` would for this system: `AddEcs`, the uniques, the messages and the services it takes, and the system. Each `SystemScheduler.Update` is one update.
- A test cannot write components itself. `Unique<T>` has an internal constructor. A writer, a lookup and `Unique<T>.Value` work only during their system's `Update`. A query created with `new` throws when enumerated. So a test sets up state in one of two ways:
  - A test-only system such as `BodySetup` writes what the test asked for. `IBefore<MoveSystem>` runs it first, and since both name `Position`, a sync point between them gives `MoveSystem` the new entity in the same update. Where the game has a spawner class, the test-only system takes it, so the entity gets the tags the system's query asks for. An entity the query skips lets a test that expects no change pass whatever the system does.
  - The instance a test passes to `AddUnique` is the world's unique, so the test keeps it and changes it between updates.
- A test reads the world between updates through `AddQuery` and `AddReadOnlyComponentLookup`, which read at any time, unlike a system's lookup. `AddQuery` throws for a query with a `Span<T>`, so the test declares its own read-only one, such as `Positions`.
- A test writes the intents a system reads through `MessageWriter<T>` from the provider, before `Update`. It reads observations through a `MessageReader<T>` taken from the provider before the first `Update`, because a reader starts after the last message written ([messages.md](messages.md)). A reader taken after `Update` finds nothing.
- `TestFrameClock` is a `FrameClock` that the test advances. `StartTestFrame(500)` sets game time to 500 ms since the start, and `TimeDelta` to the time since the previous call. The test registers it as `FrameClock`, the type a system takes.
- A helper is a static function ([orange-architecture.md](orange-architecture.md)). A test calls one that takes values or spans directly, for example `Movement.Next(new Position(1), new Velocity(2), 0.5f)`, or on arrays for spans. One that takes a query or a lookup is tested through its system.
- The fixture is `internal`, because a public test method cannot take an internal type of the game as a parameter, for example from `[TestCaseSource]`. NUnit runs internal fixtures.
- Test-only code sets up state and reads it, and never applies a rule of the game. A helper that moves bodies itself and asserts where they end up tests its own copy of the rule, so the test passes whatever `MoveSystem` does. Expected values are literals, such as `1f`, and never the result of the game's rule or a copy of it.
