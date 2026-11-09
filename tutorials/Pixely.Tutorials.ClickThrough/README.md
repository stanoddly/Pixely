# Click Through

Demonstrates `Window.SetShape`: a transparent, borderless window with a
sky-blue rectangle in the centre. Clicks on the rectangle are received by the
app; clicks in the surrounding transparent border pass through to the window
behind.

It needs transparent GPU windows and window shapes that pass input through, which
[XDL](https://github.com/stanoddly/XDL), the patched SDL that Pixely binds, provides
(see `tutorials/Pixely.Tutorials.TransparentWindow/README.md`).

Platform support for window shapes:

| Platform | Works? |
|---|---|
| Windows | Yes |
| macOS | Yes |
| X11 | Yes |
| Wayland | Yes — XDL's `0004-window-shape-passthrough` makes the shape the surface's input region, from the next present |
