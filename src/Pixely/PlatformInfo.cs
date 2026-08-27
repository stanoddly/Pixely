namespace Pixely;

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
