namespace Pixely;

public sealed record WindowConfig(
    Size<uint>? Size = null,
    string? Title = null,
    bool Fullscreen = false,
    bool Resizable = false,
    bool Transparent = false,
    bool Borderless = false,
    bool AlwaysOnTop = false,
    bool InitiallyVisible = true);
