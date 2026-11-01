# Taskbar icons

`PixelyConfig.TaskbarIconPath` specifies the application's runtime icon:

```csharp
PixelyAppBuilder builder = new();
builder.AddSingleton(new PixelyConfig(
    ApplicationIdentifier: "com.example.mygame",
    TaskbarIconPath: "images/taskbar-icon.png"));
builder.UseDefaultContent();
```

The path is resolved through the configured `IImageLoader`, so the image can come from any Pixely virtual content source. Pixely loads the image and applies it to the app's window when `Build()` creates it.

`ApplicationIdentifier` is optional. When specified, Pixely supplies it to SDL before initialization so desktop compositors can group the application's window consistently.

`Window.SetIcon(Image)` remains available to change the icon at runtime and returns whether the active SDL video backend accepted the icon.

## Platform behavior

- Windows uses the icon for the native window and its running-window taskbar representation.
- macOS treats the SDL window icon as the application-wide Dock icon.
- Linux supports the runtime icon on X11 and on Wayland compositors implementing `xdg-toplevel-icon-v1`.

Wayland compositors without `xdg-toplevel-icon-v1` cannot accept a runtime window icon. An installed desktop entry can provide a fallback, but Pixely cannot solve that limitation through virtual content.

References:

- [SDL window icons](https://wiki.libsdl.org/SDL3/SDL_SetWindowIcon)
- [SDL Wayland guidance](https://wiki.libsdl.org/SDL3/README-wayland)
- [SDL application identifiers](https://wiki.libsdl.org/SDL3/SDL_SetAppMetadataProperty)

See [Pixely.Tutorials.TaskbarIcon](../tutorials/Pixely.Tutorials.TaskbarIcon/README.md) for a runnable example.
