namespace Pixely;

// SDL video driver names are low-ASCII identifiers: https://wiki.libsdl.org/SDL3/SDL_GetVideoDriver
public sealed record PlatformInfo(string? SdlVideoDriver)
{
    public bool SupportsAlwaysOnTopWindows
    {
        get { return SdlVideoDriver != "wayland"; }
    }

    public bool SupportsSetWindowPosition
    {
        get { return SdlVideoDriver != "wayland"; }
    }

    public bool SupportsClickThrough
    {
        get { return SdlVideoDriver != "wayland"; }
    }
}
