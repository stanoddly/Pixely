# Headless mode

Headless mode (`PixelyConfig.Headless`) runs the app without showing a window (SDL video and a GPU backend are still needed) and lets an agent drive it through synthetic mouse, keyboard, and text input on standard input, and observe it through screenshots. Synthetic input goes through the ordinary Pixely input services, so existing view-scoped subscriptions, priorities, consumption, and device state apply to it. Handlers finish within the frame that runs the command.

Mouse positions use logical coordinates relative to the target window's top-left corner. `mouse move` moves to a window position and derives the relative motion from the synthetic mouse's previous position; `mouse moveby` applies a delta to that position. `mouse click` dispatches a motion, a press and a release. `key press` dispatches a key down followed by a key up. `text` delivers text directly and supports characters that do not have a corresponding keyboard scancode.

The first mouse command on a scope raises `WindowEnter` for it and makes `IMouseService.IsInWindow` true for that scope before the command's own event, the same order SDL uses when the pointer arrives in a window. `mouse leave` raises `WindowLeave` and resets that; it is the only way synthetic input leaves a window and works while a button is held, which real input cannot do, so it can drive a press cancellation. The next mouse command enters again.

Automated input affects Pixely's event-derived synthetic device state; every view scope has its own synthetic mouse and keyboard, so positions, held buttons and held keys never cross windows. It does not move the operating-system cursor, change window focus, or modify SDL's physical/global device state.

## Driving the app from standard input

A headless app reads command lines from the process's standard input and runs each one on the frame loop at `UpdateOrders.Input`, after the frame's real events and before the game's updatables. Blank lines and lines starting with `#` are ignored. A malformed line throws `FormatException` out of the frame loop, so a bad script ends the app. Lines queued before a frame starts run in that frame; a chord written in one go usually lands in one frame but may split across two. An exception thrown by an input handler propagates out of the frame loop, the same as for real input.

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
| `text <text>` | Text input with the rest of the line |
| `screenshot <path>` | Writes the last rendered frame as a PNG to the rest of the line, see below |

`<button>` is a `MouseButton` name and `<scancode>` a `Scancode` name, both case-insensitive. Numbers use invariant culture. A command targets the default `ViewScope` unless it starts with `@<n>`, the value of the scope whose window it is for: `@7 mouse click Left 10 10`, `@7 screenshot shot.png`. A scope without a registered window is malformed.

A tool that cannot hold the pipe open, such as an LLM agent running one shell command at a time, drives the app through a file it appends to:

```sh
: > commands.txt
tail -f commands.txt | dotnet run --project MyGame &
printf 'key down LeftCtrl\nkey press E\nkey up LeftCtrl\n' >> commands.txt
```

### Headless mode and screenshots

`PixelyConfig.Headless` makes every window the app registers an `OffscreenWindow`, whether through `AddWindow` or `UseDefaultRendering`, and whatever render context draws into it: the SDL window stays hidden, `Show()` returns false without showing it, and each frame is rendered into a texture on the GPU instead of the swapchain, so nothing shows on the desktop and the machine stays usable while an agent drives the app. Of `WindowConfig` only `Size` and `Title` apply; the rest is about the desktop. Custom providers work unchanged, because the window's `TryWaitAndAcquireSwapchainTexture` is what hands out the texture. Synthetic input needs no focus, so the hidden window changes nothing for the commands above. Without a swapchain there is no vsync; frames are paced at `OffscreenWindow.FrameInterval`, 30 per second. The console and `IImageWriter` are registered only in headless mode. Headless mode renders into GPU textures, so it needs the GPU device: an app that registers a window but no rendering (`docs/window-rendering.md`) fails at build.

```csharp
builder.AddSingleton(new PixelyConfig(Headless: true));
builder.UseDefaultRendering(new WindowConfig(Size: (1280, 720), Title: "Hotbar"));
```

An agent usually needs no code change for this: `PIXELY_HEADLESS=1` switches any app to headless mode, see [Environment variables](#environment-variables).

`screenshot <path>` writes the last rendered frame as a PNG; the file exists once the command's frame has run, and a path that cannot be written throws out of the frame loop. Commands run before that frame's render, so a screenshot in the same write as the commands it should show is one frame too early; send it in a later write. The capture waits for the GPU, so that frame takes longer.

The frame is also available to code through `OffscreenWindow.CaptureLastFrame()`, and `IImageWriter` saves a tightly packed, non-planar, non-indexed `Image` as a PNG; `Build()` registers the SDL one unless the app registered its own.

### Environment variables

`PixelyAppBuilder` overrides the registered `PixelyConfig`, whether the app registered one or `Build()` supplied the default, from these variables when the provider is built, before any service receives it (an `OnActivated` callback, see class-registration.md), so automation can reconfigure an app without touching its code:

| Variable | Overrides | Values |
| --- | --- | --- |
| `PIXELY_HEADLESS` | `PixelyConfig.Headless` | `1`, `true`, `0`, `false` |
| `PIXELY_GRAPHICS` | `PixelyConfig.GpuBackend` | `automatic`, `vulkan`, `direct3d12`, `metal` |
| `PIXELY_SDL_LOGGING` | `PixelyConfig.EnableSdlLogging` | `1`, `true`, `0`, `false` |
| `PIXELY_GPU_VALIDATION` | `PixelyConfig.EnableGpuValidation` | `1`, `true`, `0`, `false` |

Values are trimmed and matched case-insensitively. An unset, empty, or whitespace-only variable leaves the configured value in effect. Any other value stops `Build()` with an `InvalidOperationException` that names the variable and lists the accepted values.

```shell
PIXELY_HEADLESS=1 dotnet run --project tutorials/Pixely.Tutorials.UiScrollView
```
