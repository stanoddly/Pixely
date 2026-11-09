# Transparent Window

Stock SDL's GPU API refuses every transparent window in `SDL_ClaimWindowForGPUDevice`, because Direct3D 12 has no transparent HWND
swap chain (https://github.com/libsdl-org/SDL/issues/12410). Pixely runs on [XDL](https://github.com/stanoddly/XDL), the patched SDL
it binds through XdlSharp, which carries the patches this tutorial needs, so it runs without building SDL:

- `0003-d3d12-transparent-window` removes the check for every GPU backend. Direct3D 12 presents a transparent window through
  DirectComposition, without tearing.
- `0005-x11-transparent-visual` gives a transparent X11 window a 32-bit visual, so a compositor can blend a Vulkan window.

XDL has no patch for the opacity of the Metal layer, which an earlier version of this guide set by hand in
`METAL_INTERNAL_CreateSwapchain`, and the tutorial has not been run on macOS with XDL.

To try a change to XDL itself, build it as its README describes and put the built `libXDL3` in place of the one from
`XdlSharp.Native.<rid>` in the output folder, `bin/Debug/net11.0/runtimes/<rid>/native/`, then run with `dotnet run --no-build`,
since a build copies the package's library again.
