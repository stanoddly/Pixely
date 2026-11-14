using static XdlSharp.Xdl;

namespace Pixely;

// Where a window opens: a point in desktop coordinates for its top-left corner, or centred on a display.
public readonly record struct WindowPosition
{
    private WindowPosition(int sdlX, int sdlY)
    {
        SdlX = sdlX;
        SdlY = sdlY;
    }

    // The values SDL_PROP_WINDOW_CREATE_X_NUMBER and SDL_PROP_WINDOW_CREATE_Y_NUMBER take, which encode centring in the coordinate.
    internal int SdlX { get; }

    internal int SdlY { get; }

    // Centred on a display, which SDL encodes in the high bits of the coordinate.
    internal bool IsCentered => ((uint)SdlX & 0xFFFF0000u) == SDL_WINDOWPOS_CENTERED_MASK;

    public static WindowPosition At(Vector2Int topLeft) => new(topLeft.X, topLeft.Y);

    public static WindowPosition Centered() => new((int)SDL_WINDOWPOS_CENTERED, (int)SDL_WINDOWPOS_CENTERED);

    // SDL centres a window on the primary display instead when this display has been disconnected. It keeps 16 bits of the display ID
    // in the coordinate, so a display whose ID does not fit, which only a run with many reconnected displays or devices reaches, throws.
    public static WindowPosition Centered(Display display)
    {
        ArgumentNullException.ThrowIfNull(display);
        uint displayId = (uint)display.Id.Value;
        if (displayId > 0xFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(display), displayId, "SDL cannot centre a window on a display whose ID is above 0xFFFF.");
        }

        int centered = (int)(SDL_WINDOWPOS_CENTERED_MASK | displayId);
        return new WindowPosition(centered, centered);
    }
}
