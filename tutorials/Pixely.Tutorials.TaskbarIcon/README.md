# Taskbar icon

This tutorial configures the application's runtime taskbar icon. The PNG is normal Pixely virtual content; no platform package icon or desktop icon is involved.

```csharp
PixelyAppBuilder builder = new();
builder.AddSingleton(new PixelyConfig(
    ApplicationIdentifier: "com.example.mygame",
    TaskbarIconPath: "images/taskbar-icon.png"));
builder
    .UseDefaultContent()
    .ConfigureWindow(new WindowConfig(Title: "Taskbar Icon"))
    .UseDefaultRendering();
```

Pixely loads `TaskbarIconPath` through `IImageLoader` and calls `Window.SetIcon` for the window it creates.

The same project and content asset are used on all three platforms:

| Platform | Runtime behavior |
| --- | --- |
| Windows | SDL assigns the image to the native window's small and large icons, which Windows uses for its running-window taskbar representation. |
| macOS | SDL assigns the image to `NSApplication`, changing the application-wide Dock icon. |
| Linux | SDL assigns the image to the window on X11 and on Wayland compositors supporting `xdg-toplevel-icon-v1`. |

Wayland compositors without `xdg-toplevel-icon-v1` cannot accept a programmatic window icon. They may use an installed desktop entry as a fallback, but that is a packaging mechanism and is deliberately outside this tutorial.

Run from the repository root:

```bash
dotnet run --project tutorials/Pixely.Tutorials.TaskbarIcon
```

`Window.SetIcon(Image)` remains available to change the icon at runtime and returns whether the active SDL video backend accepted it.
