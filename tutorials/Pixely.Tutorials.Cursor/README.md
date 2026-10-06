# Cursor

This tutorial switches the mouse cursor between a system cursor, a static image and an animated image. The images are drawn in code, so the tutorial has no content files.

```csharp
builder.OnBuilt((ICursorService cursorService) =>
{
    Cursor ring = cursorService.CreateCursor(ringImage, new Vector2Int(16, 16));
    Cursor pulsingRing = cursorService.CreateCursor(
        [new CursorFrame(smallRing, TimeSpan.FromMilliseconds(80)), new CursorFrame(largeRing, TimeSpan.FromMilliseconds(80))],
        new Vector2Int(16, 16));
    cursorService.SetCursor(pulsingRing);
});
```

| Key | Cursor |
| --- | --- |
| 1 | The system pointer |
| 2 | A red ring |
| 3 | A pulsing blue ring |
| 0 | The default cursor |
| H | Hides or shows the cursor |
| Escape | Quits |

Run from the repository root:

```bash
dotnet run --project tutorials/Pixely.Tutorials.Cursor
```

See `docs/cursors.md` for the API and platform behavior.
