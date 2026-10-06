# Cursors

`ICursorService` creates mouse cursors, makes one of them active and shows or hides the cursor. `Build()` registers it, so any service or `OnBuilt` callback can take it:

```csharp
builder.OnBuilt((ICursorService cursorService) =>
{
    Cursor pointer = cursorService.CreateSystemCursor(SystemCursor.Pointer);
    cursorService.SetCursor(pointer);
});
```

SDL has one active cursor for the whole application, not one per window, so the service is a singleton without a `ViewScope`.

## Creating cursors

| Method | Cursor |
| --- | --- |
| `CreateSystemCursor(SystemCursor shape)` | A shape of the operating system, such as `Pointer`, `Text`, `Wait` or a resize arrow |
| `CreateCursor(Image image, Vector2Int hotspot)` | A static image |
| `CreateCursor(ReadOnlySpan<CursorFrame> frames, Vector2Int hotspot)` | An animated image |

The hotspot is the pixel of the image that counts as the click point. It must lie within the image.

`CursorFrame(Image Image, TimeSpan Duration)` is one frame of an animation. Frames play in order and the animation loops. A frame with a zero duration stops the animation on that frame, so a one-shot animation ends with one. Durations are whole milliseconds; a positive duration rounds up, so it never becomes the zero that stops the animation. All frames must have the same size.

An image must be tightly packed in a non-planar, non-indexed pixel format, the same rule `IImageWriter` has. Pixely copies the pixels, so the images can be disposed once the cursor exists.

`SystemCursor` has the shapes of SDL 3.4. Browser archives are built from SDL 3.4, which rejects the shapes later SDL versions add.

A method throws `PixelyException` when SDL fails, for example `CreateSystemCursor` with a video driver that has no system cursors, such as SDL's dummy driver.

## Using cursors

- `SetCursor(Cursor cursor)` makes the cursor active.
- `ResetCursor()` makes the platform's default cursor active again.
- `IsCursorVisible` shows or hides the cursor. It is the visibility the app asks for: relative mouse mode (`Window.WindowRelativeMouseMode`) hides the cursor whatever its value.

## Lifetime and threads

The service owns every cursor it creates. Disposing a cursor destroys it, and disposing the active cursor makes the default cursor active. Disposing a cursor twice does nothing. The service destroys the cursors still alive when the app is disposed, before SDL shuts down; disposing such a cursor afterwards does nothing. `SetCursor` throws `ObjectDisposedException` for a disposed cursor.

SDL allows cursor calls on the main thread only, so every member of the service, and `Cursor.Dispose()`, throws `PixelyException` on another thread.

## Platform behavior

- On Windows, macOS, Wayland and X11 with the Xcursor library, SDL's backend for the platform animates the cursor.
- On X11 without the Xcursor library, the cursor shows only its first frame.
- In the browser and on other platforms, SDL switches between the frames itself while it processes events, which Pixely does every frame.

Windows rounds frame durations to its own units of 1/60 of a second.

Custom cursors keep their pixel size on high-DPI displays. Images for other display scales, which SDL supports through `SDL_AddSurfaceAlternateImage`, are not supported yet.

In headless mode (`docs/headless.md`) cursors work, but screenshots do not show them and SDL animates them in real time, not on the fixed-step game clock.

See [Pixely.Tutorials.Cursor](../tutorials/Pixely.Tutorials.Cursor/README.md) for a runnable example.
