# Taskbar icons

`PixelyConfig.TaskbarIconPath` specifies one runtime icon for every window created by the application:

```csharp
PixelyAppBuilder builder = new();
builder.AddSingleton(new PixelyConfig(
    ApplicationIdentifier: "com.example.mygame",
    TaskbarIconPath: "images/taskbar-icon.png"));
builder.UseDefaultContent();
```

The path is resolved through the configured `IImageLoader`, so the image can come from any Pixely virtual content source. Pixely loads the image once and applies it to every window created through `AddWindow` or `UseDefaultRendering`.

`ApplicationIdentifier` is optional. When specified, Pixely supplies it to SDL before initialization so desktop compositors can group the application's windows consistently.

`Window.SetIcon(Image)` remains available as a per-window override.

## Platform behavior

- Windows uses the icon for the native window and its running-window taskbar representation.
- macOS treats the SDL window icon as the application-wide Dock icon.
- Linux supports the runtime icon on X11 and on Wayland compositors implementing `xdg-toplevel-icon-v1`.

Wayland compositors without `xdg-toplevel-icon-v1` cannot accept a runtime window icon. An installed desktop entry can provide a fallback, but Pixely cannot solve that limitation through virtual content.

References:

- [SDL window icons](https://wiki.libsdl.org/SDL3/SDL_SetWindowIcon)
- [SDL Wayland guidance](https://wiki.libsdl.org/SDL3/README-wayland)
- [SDL application identifiers](https://wiki.libsdl.org/SDL3/SDL_SetAppMetadataProperty)

See [Pixely.Tutorials.TaskbarIcon](../tutorials/Pixely.Tutorials.TaskbarIcon/README.md) for a runnable two-window example.
