namespace Pixely;

public enum WindowCloseBehavior
{
    QuitApplication,
    HideWindow
}

public sealed record WindowConfig(
    Size<uint>? Size = null,
    string? Title = null,
    bool Fullscreen = false,
    bool Resizable = false,
    bool Transparent = false,
    bool Borderless = false,
    bool AlwaysOnTop = false,
    bool InitiallyVisible = true,
    WindowCloseBehavior CloseBehavior = WindowCloseBehavior.QuitApplication,
    bool Utility = false);
