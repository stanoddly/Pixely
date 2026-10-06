# Headless mode

Headless mode (`PixelyConfig.Headless`) runs the app without showing a window (SDL video and a GPU backend are still needed) and lets an agent drive it through synthetic mouse, keyboard, text, and gamepad input on standard input, step its game time frame by frame, and observe it through screenshots. Synthetic input goes through the ordinary Pixely input services, so existing view-scoped subscriptions, priorities, consumption, and device state apply to it. Handlers finish within the frame that runs the command.

Mouse positions use logical coordinates relative to the target window's top-left corner. `mouse move` moves to a window position and derives the relative motion from the synthetic mouse's previous position; `mouse moveby` applies a delta to that position. `mouse click` dispatches a motion, a press and a release. `key press` dispatches a key down followed by a key up. `text` delivers text directly and supports characters that do not have a corresponding keyboard scancode.

The first mouse command on a scope raises `WindowEnter` for it and makes `IMouseService.IsInWindow` true for that scope before the command's own event, the same order SDL uses when the pointer arrives in a window. `mouse leave` raises `WindowLeave` and resets that; it is the only way synthetic input leaves a window and works while a button is held, which real input cannot do, so it can drive a press cancellation. The next mouse command enters again.

Automated input affects Pixely's event-derived synthetic device state; every view scope has its own synthetic mouse and keyboard, so positions, held buttons and held keys never cross windows. Gamepads belong to no window, so there is one synthetic gamepad for the whole app. It does not move the operating-system cursor, change window focus, or modify SDL's physical/global device state. Screenshots do not show the cursor, and SDL animates an animated cursor (`docs/cursors.md`) in real time, not on the fixed-step game clock.

## Driving the app from standard input

A headless app reads commands from the process's standard input (on the desktop only; headless mode is not available in the browser, see below) and runs them on the frame loop at `UpdateOrders.Input`, after the frame's real events and before the game's updatables. Every command ends with `;`. A newline is whitespace like a space or a tab, so a whole scenario fits on one line and a long one can span several. Empty commands are ignored. The app runs in lockstep with its input: game time advances only through `wait`. A frame runs commands in order until a `wait`, and when no command is left it blocks until the next `;` arrives. Which frame a command runs in therefore depends only on the commands before it, never on when or in how many pieces the input arrives. The end of the input quits the app, the same as `quit`. A malformed command throws `FormatException` out of the frame loop, so a bad script ends the app; so does input that ends with a command without its `;`, once the commands before it have run. An exception thrown by an input handler propagates out of the frame loop, the same as for real input.

| Command | Dispatches |
| --- | --- |
| `mouse move <x> <y>` | Mouse motion to the position |
| `mouse moveby <dx> <dy>` | Mouse motion by the delta |
| `mouse down <button> <x> <y>` | Button press at the position |
| `mouse up <button> <x> <y>` | Button release at the position |
| `mouse click <button> <x> <y>` | Motion, press and release at the position |
| `mouse wheel <dx> <dy> <x> <y>` | Wheel delta at the position |
| `mouse leave` | Window leave for the scope |
| `key down <scancode>` | Key down |
| `key up <scancode>` | Key up |
| `key press <scancode>` | Key down then key up |
| `text <text>` | Text input with the rest of the command, trimmed |
| `screenshot <path>` | Writes the last rendered frame as a PNG to the rest of the command, trimmed, see below |
| `gamepad connect` | Connects the synthetic gamepad |
| `gamepad disconnect` | Releases its buttons, recenters it and disconnects it |
| `gamepad down <button>` | Gamepad button press |
| `gamepad up <button>` | Gamepad button release |
| `gamepad press <button>` | Gamepad button press then release |
| `gamepad stick left\|right <x> <y>` | Stick motion to the position, -1 to 1 on each axis |
| `gamepad trigger left\|right <value>` | Trigger motion to the value, 0 to 1 |
| `wait <frames>` | Holds the commands after it for that many frames, see [Game time](#game-time) |
| `quit` | Ends the app after the frame's updatables, the same as `AppControl.Quit()`; the commands after it do not run |

A mouse `<button>` is a `MouseButton` name, a gamepad `<button>` a `GamepadButton` name, and `<scancode>` a `Scancode` name, all case-insensitive. Numbers use invariant culture; `NaN` and infinities are malformed. `text` and `screenshot` cannot contain `;`. A mouse, key, text or screenshot command targets the default `ViewScope` unless it starts with `@<n>`, the value of the scope whose window it is for: `@7 mouse click Left 10 10;`, `@7 screenshot shot.png;`. A scope without a registered window is malformed. The other commands belong to no window and are malformed with `@<n>`.

`gamepad connect` adds the synthetic gamepad to `IGamepadService.Gamepads` and raises `GamepadConnected`. Any other gamepad command before it is malformed, and so is a second connect. `stick` moves the two axes as two events, X first, the way SDL reports a physical stick, so a handler can see the new X with the old Y. Stick values go through the same dead zone as a physical gamepad, which turns values between -0.2 and 0.2 into 0; triggers have none. `gamepad disconnect` does what SDL does when a gamepad is unplugged: held buttons are released, then sticks and triggers return to 0, before `GamepadDisconnected`. Headless mode does not start SDL's gamepad subsystem, so physical gamepads are not detected and the synthetic gamepad is the only one; a scenario behaves the same whether or not a pad is plugged in.

### Scenarios

A scenario is a list of commands. This one holds the right arrow key for 2 seconds of game time, waits one frame so that the frame shows the release, saves a screenshot and ends the app at the end of its input:

```sh
printf 'key down Right; wait 60; key up Right; wait 1; screenshot shot.png;' | PIXELY_HEADLESS=1 dotnet run --project MyGame
```

A longer scenario can live in a file, one command per line or several:

```sh
PIXELY_HEADLESS=1 dotnet run --project MyGame < scenario.txt
```

A tool that cannot hold the pipe open, such as an LLM agent running one shell command at a time, drives the app through a file it appends to. `tail -f` never ends its output, so the app waits between appends and game time stands still until the next one:

```sh
: > commands.txt
tail -f commands.txt | PIXELY_HEADLESS=1 dotnet run --project MyGame &
printf 'key down LeftCtrl; key press E; key up LeftCtrl;' >> commands.txt
```

### Game time

In headless mode game time does not follow the real clock. The first frame has an `ElapsedTime` and a `TimeDelta` of 0, and every later frame adds exactly one step of 1/30 second, rounded up to 33,333,334 nanoseconds. Every run of a scenario therefore sees the same frame times, and animations driven by `ElapsedTime` or `TimeDelta` give the same screenshots, as long as its commands reach the same frames (see above).

`wait <frames>` runs that many frames before the commands after it, as fast as the machine can: frames are not paced to the real clock. `wait 30` is one second of game time however long it takes. `wait 0` does nothing. Real time still passes while frames run and while the app waits for input, so work that follows the real clock, such as content loading on a background thread, can finish in a different frame from run to run; a scenario waits for it with a larger `wait`.

While the app waits for input it does not process SDL events. The windows are hidden, so nothing on the desktop waits for them.

Synthetic input carries the `ElapsedNanoseconds` of the frame that runs it as its `Timestamp`, so every synthetic event of one frame has the same timestamp and handlers tell them apart only by order. Events that come from SDL keep SDL's clock, so their timestamps cannot be compared with synthetic ones.

### Headless mode and screenshots

`PixelyConfig.Headless` makes every window the app registers an `OffscreenWindow`, whether through `AddWindow` or `UseDefaultRendering`, and whatever render context draws into it: the SDL window stays hidden, `Show()` returns false without showing it, and each frame is rendered into a texture on the GPU instead of the swapchain, so nothing shows on the desktop and the machine stays usable while an agent drives the app. Of `WindowConfig` only `Size` and `Title` apply; the rest is about the desktop. Custom providers work unchanged, because the window's `TryWaitAndAcquireSwapchainTexture` is what hands out the texture. The SDL window is not claimed for the GPU device, so its colour target format is the fixed `B8G8R8A8Unorm`, the format SDL gives an SDR swapchain on Vulkan and D3D12. A Vulkan driver without that format gives the desktop run `R8G8B8A8Unorm` instead, so headless and desktop runs can then render in different formats. Synthetic input needs no focus, so the hidden window changes nothing for the commands above. Without a swapchain there is no vsync. Instead, acquiring the window's texture waits for the GPU to finish all work submitted before it, so a headless app keeps at most about one frame in flight however fast `wait` runs frames. Only a render into an offscreen window waits, so a headless app without a window does not. The console and `IImageWriter` are registered only in headless mode. Headless mode renders into GPU textures, so it needs the GPU device: an app that registers a window but no rendering (`docs/window-rendering.md`) fails at build. It is not available in the browser: a page has no standard input, and reading a frame back needs a GPU wait the browser build cannot do (`docs/hosting.md`, WebGPU in the browser), so `PixelyConfig.Headless` or `PIXELY_HEADLESS` there fails at build with `PixelyInitializationException`.

```csharp
builder.AddSingleton(new PixelyConfig(Headless: true));
builder.UseDefaultRendering(new WindowConfig(Size: (1280, 720), Title: "Hotbar"));
```

An agent usually needs no code change for this: `PIXELY_HEADLESS=1` switches any app to headless mode, see [Environment variables](#environment-variables).

`screenshot <path>` writes the last rendered frame as a PNG; the file exists once the command's frame has run. Missing directories of the path are created. A path that still cannot be written, such as one whose parent is a file, throws out of the frame loop. So does a screenshot before the first frame has rendered. Commands run before that frame's render, so a screenshot right after the commands it should show is one frame too early; put `wait 1;` before it. The capture waits for the GPU, so that frame takes longer.

The frame is also available to code through `OffscreenWindow.CaptureLastFrame()`, and `IImageWriter` saves a tightly packed, non-planar, non-indexed `Image` as a PNG, creating missing directories; `Build()` registers the SDL one unless the app registered its own.

### Environment variables

`PixelyAppBuilder` overrides the registered `PixelyConfig`, whether the app registered one or `Build()` supplied the default, from these variables when the provider is built, before any service receives it (an `OnActivated` callback, see class-registration.md), so automation can reconfigure an app without touching its code:

| Variable | Overrides | Values |
| --- | --- | --- |
| `PIXELY_HEADLESS` | `PixelyConfig.Headless` | `1`, `true`, `0`, `false` |
| `PIXELY_GRAPHICS` | `PixelyConfig.GpuBackend` | `automatic`, `vulkan`, `direct3d12`, `metal`, `webgpu` |
| `PIXELY_PREFER_LOW_POWER_GPU` | `PixelyConfig.PreferLowPowerGpu` | `1`, `true`, `0`, `false` |
| `PIXELY_SDL_LOGGING` | `PixelyConfig.EnableSdlLogging` | `1`, `true`, `0`, `false` |
| `PIXELY_GPU_VALIDATION` | `PixelyConfig.EnableGpuValidation` | `1`, `true`, `0`, `false` |
| `PIXELY_DIAGNOSTICS` | `PixelyConfig.EnableDiagnostics` (see diagnostics.md) | `1`, `true`, `0`, `false` |
| `PIXELY_UPLOAD_WAIT` | `PixelyConfig.UploadWait` (see window-rendering.md) | `automatic`, `on`, `off` |

Values are trimmed and matched case-insensitively. An unset, empty, or whitespace-only variable leaves the configured value in effect. Any other value stops `Build()` with an `InvalidOperationException` that names the variable and lists the accepted values.

```shell
PIXELY_HEADLESS=1 dotnet run --project tutorials/Pixely.Tutorials.UiScrollView
```
