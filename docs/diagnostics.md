# Diagnostics

Pixely can report where a frame's time goes, which GPU device it runs on, and how much GPU memory it holds. The report goes to the log, so it can be read from another machine, for example over SSH from a kiosk that shows only the game.

## Turning it on

Set `PIXELY_DIAGNOSTICS` to `1` or `true`, or register `new PixelyConfig(EnableDiagnostics: true)`. The variable overrides the configured value, like the other variables in [headless.md](headless.md#environment-variables). Diagnostics are off by default. When they are off, each frame calls a recorder that does nothing, and no report is created.

The report is written to the logger category `Pixely.Diagnostics` at the `Information` level when the app registers a logger factory (see [logging.md](logging.md)), and to standard output otherwise.

## What it reports

At startup, the GPU device, when the root provider registers one:

```text
GPU device: AMD Radeon Graphics (RADV REMBRANDT), backend vulkan, driver radv 26.2.3
```

For each window, its swapchain on the first frame and whenever its size or format changes:

```text
Swapchain of view 0: 800x600, B8G8R8A8Unorm, present mode vsync
```

Pixely never changes SDL's swapchain parameters, so the present mode is always vsync. An offscreen window in headless mode reports `offscreen` instead, because it presents nothing.

Every 5 seconds of frames, or every 4096 frames if that comes first, and once more for the remaining frames when the app is disposed:

```text
75.2 FPS over 376 frames, ms average/95th percentile/maximum: frame 13.30/14.23/29.22, update 0.10/0.09/9.69, render 0.71/0.86/16.83, swapchain wait 12.49/13.50/14.49; gen0 collections 0.00 per frame; GPU memory 176 B, textures 0 B
```

| Part | Measured |
| --- | --- |
| `frame` | From the end of the previous frame to the end of this one, without the headless input wait. |
| `update` | Stage transitions, SDL events and every `IUpdatable`, without the headless input wait. |
| `render` | Every render coordinator, without the swapchain wait: recording commands, uploads and submits. |
| `swapchain wait` | Waiting for each window's swapchain texture, added up over the windows. |
| `gen0 collections` | Garbage collections of generation 0, per frame. |
| `GPU memory` | The textures and buffers Pixely created on the root GPU device, at the time of the report. In the report on dispose, stages have already released theirs. |

## Reading the numbers

- SDL GPU has no timestamp queries, so the swapchain wait stands in for GPU time. A long wait means the GPU or the display limits the frame rate. A long update or render means the CPU does.
- With vsync, the frame rate snaps to the display's rate divided by a whole number, for example 60, 30 or 20 on a 60 Hz display. A frame that misses a refresh by a little takes two.
- The frame time also includes the 16 ms wait of a frame that no window drew, and, in the browser, the time until the next animation frame.
- In headless mode, the time the app blocks on standard input for its next command is left out of the frame and the update.
- Only the render coordinator that `UseWindowRendering` or `UseDefaultRendering` registers measures the swapchain wait. A `RenderCoordinator<T>` an app creates itself, or a [custom render coordinator](custom-render-coordinators.md), adds its wait to `render`.
- The report is written during the update of the frame after a period ends, so that frame's update also counts writing it.
- In the browser, `performance.now()` is coarsened unless the page is cross-origin isolated, so times below a millisecond can read as 0 or jump in steps.
- Only the root provider's GPU device is reported. A stage that registers its own device is not.
