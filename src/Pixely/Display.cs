using XdlSharp;

namespace Pixely;

// Identifies a connected display for as long as it stays connected. SDL gives a reconnected display a new ID, so an ID does not
// identify a display across runs.
public readonly record struct DisplayId
{
    internal DisplayId(SDL_DisplayID value)
    {
        Value = value;
    }

    internal SDL_DisplayID Value { get; }
}

// A display as it was when read. Bounds and usable bounds are in desktop coordinates, the ones Window.Position uses. Usable bounds
// leave out what the desktop reserves, such as a taskbar or a menu bar. At 200 % a platform reports the scale either as pixel density
// (macOS, Wayland, the browser) or as content scale (Windows, X11), and Scale is their product, 2 on every platform.
public sealed record Display(DisplayId Id, Rectangle Bounds, Rectangle UsableBounds, float PixelDensity, float ContentScale)
{
    // The Window.DisplayScale that a window with high pixel density gets on this display.
    public float Scale => PixelDensity * ContentScale;
}
