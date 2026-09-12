# Input automation

`IInputAutomation` synchronously delivers synthetic mouse, keyboard, and text input through the ordinary Pixely input services. Existing view-scoped subscriptions, priorities, consumption, and device state apply to automated input. Handlers finish before an automation method returns.

It is not registered by default. Register it on the builder, then resolve the application-lifetime service from the app:

```csharp
builder.AddInputAutomation();

IInputAutomation input = app.GetRequiredService<IInputAutomation>();
```

Mouse positions use logical coordinates relative to the target window's top-left corner. `MouseMoveTo` moves to a window position and derives the relative motion from the synthetic mouse's previous position. `MouseMoveBy` applies a delta to that previous position.

```csharp
input.MouseMoveTo(new Vector2(320, 180));
input.MouseMoveBy(new Vector2(10, -5));
input.MouseClick(MouseButton.Left, new Vector2(330, 175));
input.MouseWheel(new Vector2(0, -1), new Vector2(330, 175));
```

Use explicit down and up calls for held input:

```csharp
input.KeyDown(Scancode.W);
input.KeyUp(Scancode.W);

input.MouseDown(MouseButton.Left, new Vector2(100, 100));
input.MouseMoveTo(new Vector2(200, 100));
input.MouseUp(MouseButton.Left, new Vector2(200, 100));
```

`KeyPress` dispatches a key down followed by a key up. `TextInput` delivers text directly and supports characters that do not have a corresponding keyboard scancode.

The default `ViewScope` targets the ordinary single-window application. Pass a scope explicitly for another registered window.

Automated input affects Pixely's event-derived synthetic device state. It does not move the operating-system cursor, change window focus, or modify SDL's physical/global device state.

## Driving the app from standard input

`AddInputAutomation()` also reads command lines from the process's standard input and runs each one on the frame loop at `UpdateOrders.Input`, after the frame's real events and before the game's updatables. Every command gets one reply line on standard output: `ok` or `error: <reason>` for a malformed line. Blank lines and lines starting with `#` are ignored and get no reply. Lines queued before a frame starts run in that frame; a chord written in one go usually lands in one frame but may split across two. An exception thrown by an input handler propagates out of the frame loop, the same as for real input.

| Command | Calls |
| --- | --- |
| `mouse move <x> <y>` | `MouseMoveTo` |
| `mouse moveby <dx> <dy>` | `MouseMoveBy` |
| `mouse down <button> <x> <y>` | `MouseDown` |
| `mouse up <button> <x> <y>` | `MouseUp` |
| `mouse click <button> <x> <y>` | `MouseClick` |
| `mouse wheel <dx> <dy> <x> <y>` | `MouseWheel` |
| `key down <scancode>` | `KeyDown` |
| `key up <scancode>` | `KeyUp` |
| `key press <scancode>` | `KeyPress` |
| `text <text>` | `TextInput` with the rest of the line |
| `screenshot <path>` | Writes the next rendered frame as a PNG, see below |

`<button>` is a `MouseButton` name and `<scancode>` a `Scancode` name, both case-insensitive. Numbers use invariant culture. Commands always target the default `ViewScope`.

A tool that cannot hold the pipe open, such as an LLM agent running one shell command at a time, drives the app through a file it appends to:

```sh
: > commands.txt
tail -f commands.txt | dotnet run --project MyGame > replies.txt &
printf 'key down LeftCtrl\nkey press E\nkey up LeftCtrl\n' >> commands.txt
cat replies.txt
```

Keep logging off standard output while doing this; replies share the stream.

### Screenshots without a window

`UseOffscreenRendering()` makes every window an `OffscreenWindow`: the SDL window stays hidden and each frame is rendered into a texture on the GPU instead of the swapchain, so nothing shows on the desktop and the machine stays usable while an agent drives the app. It works with `UseDefaultRendering()` and with custom render contexts alike, because the window's `TryWaitAndAcquireSwapchainTexture` is what hands out the texture. Synthetic input needs no focus, so the hidden window changes nothing for the commands above. Without a swapchain there is no vsync; frames are paced at the `frameInterval` argument, 60 per second by default.

```csharp
builder
    .UseDefaultRendering(new WindowConfig(Size: (1280, 720)))
    .UseOffscreenRendering()
    .AddInputAutomation();
```

`screenshot <path>` then captures the frame rendered after the command runs, writes it as a PNG and replies `ok` once the file exists, or `error: <reason>` when it cannot be written. The reply arrives one frame later than the replies of the other lines that ran in the same frame. The capture waits for the GPU, so that frame takes longer. Without `UseOffscreenRendering()` the command replies with an error, because a swapchain image cannot be read back.

The texture behind this is also available to code through `IFrameCapture`, and `IImageWriter` saves any `Image` as a PNG.
