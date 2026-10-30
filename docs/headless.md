# Headless mode

Headless mode runs the app without showing a window (SDL video and a GPU backend are still needed; the stub device of [testing.md](testing.md) renders nothing and is refused) and lets an agent drive it through synthetic mouse, keyboard, text, and gamepad input on standard input, step its game time frame by frame, and observe it through screenshots. Synthetic input goes through the ordinary Pixely input services, so existing view-scoped subscriptions, priorities, consumption, and device state apply to it. Handlers finish within the frame that runs the command.

Mouse positions use logical coordinates relative to the target window's top-left corner. `mouse move` moves to a window position and derives the relative motion from the synthetic mouse's previous position; `mouse moveby` applies a delta to that position. `mouse click` dispatches a motion, a press and a release. `key press` dispatches a key down followed by a key up. `text` delivers text directly and supports characters that do not have a corresponding keyboard scancode.

The first mouse command on a scope raises `WindowEnter` for it and makes `IMouseService.IsInWindow` true for that scope before the command's own event, the same order SDL uses when the pointer arrives in a window. `mouse leave` raises `WindowLeave` and resets that; it is the only way synthetic input leaves a window and works while a button is held, which real input cannot do, so it can drive a press cancellation. The next mouse command enters again.

Automated input affects Pixely's event-derived synthetic device state; every view scope has its own synthetic mouse and keyboard, so positions, held buttons and held keys never cross windows. Gamepads belong to no window, so there is one synthetic gamepad for the whole app. It does not move the operating-system cursor, change window focus, or modify SDL's physical/global device state. Screenshots do not show the cursor, and SDL animates an animated cursor (`docs/cursors.md`) in real time, not on the fixed-step game clock.

## Turning it on

Headless mode is in `Pixely.Headless`, which the Pixely package ships. An app adds it once with `AddHeadlessSupport()`, and the environment variable `PIXELY_HEADLESS` then turns it on:

- `1` or `true` runs the app headless.
- Unset, empty, `0` or `false` runs the app as usual, and `AddHeadlessSupport()` registers nothing.
- Any other value throws `InvalidOperationException` from `AddHeadlessSupport()`, naming the variable and the accepted values. Values are trimmed and matched case-insensitively.

The generated `Main` of a hosted app (`PixelyHosting`, see hosting.md) calls it on the desktop before `Configure`. An app with its own `Main` calls it itself. An app that never calls it ignores `PIXELY_HEADLESS`. For example, a game that keeps standard-input automation out of its Release builds calls it only in Debug:

```csharp
PixelyAppBuilder builder = new();
#if DEBUG
builder.AddHeadlessSupport();
#endif
```

A headless app is isolated from the machine it runs on: SDL's joystick and gamepad subsystems are not started, so only the synthetic gamepad exists, and `IPlatformSettings.Theme` is always `Unknown` (platform-settings.md). `PIXELY_COMMAND_CONSOLE` has no effect, because headless mode reads standard input with a console of its own.

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

A mouse `<button>` is a `MouseButton` name, a gamepad `<button>` a `GamepadButton` name, and `<scancode>` a `Scancode` name, all case-insensitive. A `<scancode>` also accepts common names from other naming schemes, such as `LeftControl`, `ControlLeft`, `LeftCommand`, `Enter`, `Esc`, `ArrowUp`, `KeyW`, `Digit1`, `1` and `Numpad1`. A side-less modifier, `Ctrl`, `Control`, `Shift`, `Alt`, `Option`, `Gui`, `Meta`, `Super`, `Windows`, `Win`, `Command` or `Cmd`, presses the left key. Numbers use invariant culture; `NaN` and infinities are malformed. `text` and `screenshot` cannot contain `;`. A mouse, key, text or screenshot command targets the default `ViewScope` unless it starts with `@<n>`, the value of the scope whose window it is for: `@7 mouse click Left 10 10;`, `@7 screenshot shot.png;`. A scope without a registered window is malformed. The other commands belong to no window and are malformed with `@<n>`.

`gamepad connect` adds the synthetic gamepad to `IGamepadService.Gamepads` and raises `GamepadConnected`. Any other gamepad command before it is malformed, and so is a second connect. `stick` moves the two axes as two events, X first, the way SDL reports a physical stick, so a handler can see the new X with the old Y. Stick values go through the same dead zone as a physical gamepad. It applies to the stick's length, not to each axis: a stick shorter than 0.2 reads (0, 0), and a longer one keeps its direction while its length is rescaled from 0.2–1 to 0–1 and capped at 1. Triggers have no dead zone. `gamepad disconnect` does what SDL does when a gamepad is unplugged: held buttons are released, then sticks and triggers return to 0, before `GamepadDisconnected`. Headless mode does not start SDL's gamepad subsystem, so physical gamepads are not detected and the synthetic gamepad is the only one; a scenario behaves the same whether or not a pad is plugged in. `IPlatformSettings.Theme` is always `Unknown` and `ThemeChanged` is never raised, so a scenario also behaves the same whatever theme the host desktop uses (`docs/platform-settings.md`).

### Game commands

A game adds its own commands by deriving from `AutomationCommand` and registering it through the service collection of the app or of a stage (`IStageManager.Load`), one singleton registration per command:

```csharp
public sealed class SpawnCommand(World world) : AutomationCommand("spawn")
{
    public override void Execute(string arguments, ViewScope? viewScope)
    {
        if (viewScope != null)
        {
            throw new FormatException("'spawn' takes no view scope");
        }

        world.Spawn(arguments);
    }
}

stage.AddSingleton<AutomationCommand, SpawnCommand>();
```

A command whose first word is the `Name` runs the game command, matched case-sensitively, in the same frame and order as the built-in commands around it. `Execute` receives the rest of the command after the name, trimmed, and parses it itself. It also receives the scope of an `@<n>` prefix, or null without one; the interpreter does not check that the scope has a window, so the command decides what the scope means and whether it accepts one. A game command cannot hold frames; a scenario uses `wait` for that. A game command reports a malformed command by throwing `FormatException`; any exception from `Execute` ends the app, the same as for a built-in command. A command that produces output writes it wherever it chooses, such as a file named in its arguments.

A command is active from when its provider creates it until that provider disposes it: the app's commands for the whole run, a stage's commands from the stage's load until it is unloaded or reloaded. After that its name is an unknown command again. A failed build of the app or load of a stage disposes the commands it created, so their names are free again. Register commands as singletons: a transient command is created only when something resolves it, so the interpreter does not see it before then, and a second resolution creates a second command with the same name, which fails. The service provider creates singletons when it is built (see class-registration.md), so a registered command and its dependencies are created in every run, headless or not; only headless mode or the command console (see below) runs it.

A name is checked when the command is created, in every run. The `AutomationCommand` constructor throws `ArgumentException` for a name that is null or empty, contains whitespace or `;`, or starts with `@`, because it would never match, and for a name of a built-in command (`wait`, `quit`, `gamepad`, `mouse`, `key`, `text`, `screenshot`), because one would hide the other. A command with the name of another active command, of the app or of the current stage, throws `InvalidOperationException` that names both types, so the build of the app or the load of the stage fails.

### Game commands outside headless mode

`PixelyConfig.EnableCommandConsole` runs game commands typed on standard input while the app runs normally, with its windows shown, physical input and the real clock, for example cheats while testing a game. A game usually turns it on and registers its cheats only in its own Debug build. An app that already registers a `PixelyConfig` passes the flag to that constructor instead of registering a second one, which would replace the first:

```csharp
#if DEBUG
bool enableCommandConsole = true;
builder.AddSingleton<AutomationCommand, GodModeCommand>();
#else
bool enableCommandConsole = false;
#endif
builder.AddSingleton(new PixelyConfig(ApplicationIdentifier: "com.example.game", EnableCommandConsole: enableCommandConsole));
```

With `GodModeCommand` named `god`, typing `god` and Enter in the terminal of `dotnet run` runs it in the next frame. `PIXELY_COMMAND_CONSOLE=1` turns the console on without a rebuild, also in a Release build, see [Environment variables](#environment-variables); there it runs only `quit` and the commands registered in every build, not the cheats.

The console differs from headless mode:

- A background thread reads the input, so a frame never waits for it. Each frame runs, at `UpdateOrders.Input`, the commands read since the frame before.
- A newline ends a command as well as `;`, so a command is a line.
- Only game commands, with an optional `@<n>`, and `quit` run. The built-in input, `screenshot` and `wait` commands are unknown commands here.
- A malformed command, such as an unknown one, one with an invalid `@<n>` or one whose `Execute` throws `FormatException`, is written to standard error and the app goes on. Any other exception from `Execute` ends the app, the same as in headless mode.
- The end of the input ends only the console; the app keeps running. A command left without its newline or `;` at the end still runs.
- A failed read of the input, such as an `IOException` when its terminal has gone away, is written to standard error and ends the console as the end of the input does.
- The reading thread starts with the first frame. A read it is blocked in cannot be cancelled, so the thread does not keep the process alive after the app ends.

Headless mode reads standard input with its own console and turns this setting off. Neither console exists in the browser, which has no standard input, so the setting does nothing there either.

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

Headless mode makes every window the app registers an `OffscreenWindow`, whether through `AddWindow` or `UseDefaultRendering`, and whatever render context draws into it: the SDL window stays hidden, `Show()` returns false without showing it, and each frame is rendered into a texture on the GPU instead of the swapchain, so nothing shows on the desktop and the machine stays usable while an agent drives the app. Of `WindowConfig` only `Size` and `Title` apply; the rest is about the desktop. Custom providers work unchanged, because the window's `TryWaitAndAcquireSwapchainTexture` is what hands out the texture. The SDL window is not claimed for the GPU device, so it has no swapchain to take a format from. Its colour target format is `B8G8R8A8UnormSrgb`, the format of an sRGB swapchain on Vulkan and D3D12, or `R8G8B8A8UnormSrgb` when the device cannot render to and sample the first; a device with neither fails at build with `PixelyInitializationException`. A Vulkan driver can give a desktop window `R8G8B8A8UnormSrgb` while the offscreen window has `B8G8R8A8UnormSrgb`, so headless and desktop runs can then render in different byte orders of the same color space. Screenshots contain the stored sRGB bytes. Synthetic input needs no focus, so the hidden window changes nothing for the commands above. Without a swapchain there is no vsync. Instead, acquiring the window's texture waits for the GPU to finish all work submitted before it, so a headless app keeps at most about one frame in flight however fast `wait` runs frames. Only a render into an offscreen window waits, so a headless app without a window does not. The console and `IImageWriter` are registered only in headless mode. Headless mode renders into GPU textures, so it needs the GPU device: an app that registers a window but no rendering (`docs/window-rendering.md`) fails at build. The stub GPU device of [testing.md](testing.md) renders nothing to read back, so an app on it fails at build when it creates its first window, with `PixelyInitializationException`. It is not available in the browser: a page has no standard input, and reading a frame back needs a GPU wait the browser build cannot do (`docs/hosting.md`, WebGPU in the browser). `Pixely.Headless` is built for the desktop only, and the assembly is marked unsupported in the browser, so a call to it there is a CA1416 warning.

```csharp
builder.AddHeadlessSupport();
builder.UseDefaultRendering(new WindowConfig(Size: (1280, 720), Title: "Hotbar"));
```

An agent needs no code change for this: `PIXELY_HEADLESS=1` switches any app that calls `AddHeadlessSupport()`, which every hosted app does, see [Turning it on](#turning-it-on).

`screenshot <path>` writes the last rendered frame as a PNG; the file exists once the command's frame has run. Missing directories of the path are created. A path that still cannot be written, such as one whose parent is a file, throws out of the frame loop. So does a screenshot before the first frame has rendered. Commands run before that frame's render, so a screenshot right after the commands it should show is one frame too early; put `wait 1;` before it. The capture waits for the GPU, so that frame takes longer.

The frame is also available to code through `OffscreenWindow.CaptureLastFrame()`, and `IImageWriter` saves a tightly packed, non-planar, non-indexed `Image` as a PNG, creating missing directories. `AddHeadlessSupport()` registers the SDL one unless the app registered its own before the call; one registered after the call replaces it.

### Environment variables

`PIXELY_HEADLESS` is read by `AddHeadlessSupport()`, see [Turning it on](#turning-it-on). `PixelyAppBuilder` overrides the registered `PixelyConfig`, whether the app registered one or `Build()` supplied the default, from these variables when the provider is built, before any service receives it (an `OnActivated` callback, see class-registration.md), so automation can reconfigure an app without touching its code:

| Variable | Overrides | Values |
| --- | --- | --- |
| `PIXELY_GRAPHICS` | `PixelyConfig.GpuBackend` | `automatic`, `vulkan`, `direct3d12`, `metal`, `webgpu` |
| `PIXELY_PREFER_LOW_POWER_GPU` | `PixelyConfig.PreferLowPowerGpu` | `1`, `true`, `0`, `false` |
| `PIXELY_SDL_LOGGING` | `PixelyConfig.EnableSdlLogging` | `1`, `true`, `0`, `false` |
| `PIXELY_GPU_VALIDATION` | `PixelyConfig.EnableGpuValidation` | `1`, `true`, `0`, `false` |
| `PIXELY_DIAGNOSTICS` | `PixelyConfig.EnableDiagnostics` (see diagnostics.md) | `1`, `true`, `0`, `false` |
| `PIXELY_UPLOAD_WAIT` | `PixelyConfig.UploadWait` (see window-rendering.md) | `automatic`, `on`, `off` |
| `PIXELY_COMMAND_CONSOLE` | `PixelyConfig.EnableCommandConsole` | `1`, `true`, `0`, `false` |

Values are trimmed and matched case-insensitively. An unset, empty, or whitespace-only variable leaves the configured value in effect. Any other value stops `Build()` with an `InvalidOperationException` that names the variable and lists the accepted values.

```shell
PIXELY_HEADLESS=1 dotnet run --project tutorials/Pixely.Tutorials.UiScrollView
```
