# Scrolling and clipping

Pencuil clips content to a rectangle and scrolls it with `ScrollView`, or drives an offset
directly with `ScrollBar`. Both keep the offset in the caller's variable, like `TextField` and
`NumberField` keep their value.

## ScrollView

`ScrollView` opens a scope: content built inside it is clipped to the viewport, shifted by the
offset, and a scrollbar is reserved along the trailing edge.

```csharp
using (pencil.ScrollView(id: 1, width: 320, height: 420, ref ViewModel.ScrollOffset, contentExtent))
{
    for (int index = 0; index < itemCount; index++)
    {
        pencil.HoverRectangle(itemWidth, ItemHeight, itemColor, pencil.Style.ActiveColor);
    }
}
```

`contentExtent` is the size of the content along the scrolling axis, in pixels — the caller
supplies it because only the caller knows how tall the content is before it is built. Pass
`Orientation.Horizontal` to scroll sideways; the bar is then reserved along the bottom edge.

Inside the scope the layout cursor starts at the content origin minus the offset, and the layout
direction is set to `Bottom` (or `Right` when horizontal). On disposal the clip, direction and
gap are restored, and the layout cursor advances as if the whole view had been placed as one
element.

The offset is resolved *before* the content is built, so a wheel notch is reflected in the same
frame rather than one frame later. It is clamped to `[0, contentExtent - viewportExtent]`, and
the viewport excludes the reserved scrollbar strip.

## ScrollBar

`ScrollBar` is the bar on its own, for cases where the content is not built through a
`ScrollView` — a virtualised list that only builds visible rows, for instance.

```csharp
pencil.ScrollBar(id: 1, ref offset, contentExtent, viewportExtent, length: 400);
```

It returns `true` when the offset changed. It supports dragging the thumb, clicking the bare
track to page by one viewport, and the wheel while the cursor is over it. When the content fits
the viewport it draws the track without a thumb and still occupies its rectangle, so layouts do
not jump as content grows and shrinks.

## Clipping

`WithClip` is the primitive underneath, usable on its own:

```csharp
using (pencil.WithClip(new Rectangle(0, 0, 100, 100)))
{
    pencil.Rectangle(200, 200, Colors.Red);
}
```

Nested scopes intersect. Clipping happens as instructions are emitted: an instruction fully
outside the clip is never recorded, and a partially visible one is trimmed — textures get their
UVs trimmed to match, so glyphs are cut mid-stroke rather than disappearing. Hit tests are
clipped too, so content scrolled out of view stops responding to clicks and stops taking part in
hover repainting.

This is CPU-side clipping rather than a GPU scissor. Everything Pencuil draws is an axis-aligned
rectangle with a linear UV mapping, so trimming is exact, and rows scrolled out of view cost no
draw calls at all.

## Pointer capture

Dragging a thumb needs the pointer to keep reaching one control while the button is held, which
`Pencil` tracks as capture:

- `CapturedControlId` / `HasCapture` / `IsCapturedBy(id)` — capture is separate from
  `FocusedControlId`, so dragging a scrollbar does not blur a text field being edited.
- Capture is taken on press over the thumb and released when the button is released, or when the
  capturing control stops being built (`FinishBuild`).
- While captured, every cursor motion invalidates, and a cursor leaving the window no longer
  clears the cursor position — a drag that wanders outside keeps working.

## Wheel input

`PencilSystem` subscribes to the mouse wheel and latches the delta for the next build, the same
way clicks are latched through `CursorJustReleased`. The delta is only latched, and the event is
only consumed, when the cursor is over a registered scroll area, so the wheel still reaches the
game when it is not over a scrollable panel.

`GuiStyle.ScrollStep` sets pixels per wheel notch. Vertical scrolling inverts the wheel axis
(wheel-up is positive in SDL, offsets grow downwards); horizontal scrolling uses the wheel's
horizontal axis, which comes from tilt wheels and trackpads. Shift+wheel is deliberately not
mapped to horizontal: macOS performs that swap in the OS while Windows and X11 do not, so doing
it here would apply it twice on one platform.

The first consumer of a wheel delta clears it, so two overlapping scroll areas cannot both act
on the same notch. With nested scroll views the outer one wins, since it is built first.

## Style

`GuiStyle` carries `ScrollBarThickness`, `ScrollStep` and `MinimumThumbLength`. They have
defaults, so existing positional `GuiStyle` construction keeps working.

See `tutorials/Pixely.Tutorials.ScrollList` for a vertical list and a horizontal panel.
