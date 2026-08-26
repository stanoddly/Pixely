# Click Through

Demonstrates `Window.SetShape`: a transparent, borderless window with a
sky-blue rectangle in the centre. Clicks on the rectangle are received by the
app; clicks in the surrounding transparent border pass through to the window
behind.

Requires a patched SDL3 build — see
`tutorials/Pixely.Tutorials.TransparentWindow/README.md` for setup
instructions.

Platform support for window shapes:

| Platform | Works? |
|---|---|
| Windows | Yes |
| macOS | Yes |
| X11 | Yes |
| Wayland | No — SDL does not apply window shapes |
