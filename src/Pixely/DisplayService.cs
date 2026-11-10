using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

public sealed class DisplayService : IDisplayService
{
    internal DisplayService()
    {
    }

    public Display GetPrimaryDisplay()
    {
        ThrowIfNotMainThread();
        SDL_DisplayID displayId = SDL_GetPrimaryDisplay();
        if (displayId == 0)
        {
            throw new PixelyException($"SDL_GetPrimaryDisplay failed: {SdlError.GetError()}");
        }

        return Read(displayId);
    }

    public IReadOnlyList<Display> GetDisplays()
    {
        ThrowIfNotMainThread();
        unsafe
        {
            int count;
            Pointer<SDL_DisplayID> displayIds = SDL_GetDisplays(&count);
            if (displayIds.IsNull)
            {
                throw new PixelyException($"SDL_GetDisplays failed: {SdlError.GetError()}");
            }

            try
            {
                SDL_DisplayID* ids = displayIds;
                List<Display> displays = new(count);
                for (int i = 0; i < count; i++)
                {
                    displays.Add(Read(ids[i]));
                }

                return displays;
            }
            finally
            {
                SDL_free((nint)(SDL_DisplayID*)displayIds);
            }
        }
    }

    internal static Display Read(SDL_DisplayID displayId)
    {
        SDL_Rect bounds;
        SDL_Rect usableBounds;
        unsafe
        {
            if (!SDL_GetDisplayBounds(displayId, &bounds))
            {
                throw new PixelyException($"SDL_GetDisplayBounds failed: {SdlError.GetError()}");
            }

            if (!SDL_GetDisplayUsableBounds(displayId, &usableBounds))
            {
                throw new PixelyException($"SDL_GetDisplayUsableBounds failed: {SdlError.GetError()}");
            }
        }

        float contentScale = SDL_GetDisplayContentScale(displayId);
        if (contentScale == 0)
        {
            throw new PixelyException($"SDL_GetDisplayContentScale failed: {SdlError.GetError()}");
        }

        float pixelDensity;
        unsafe
        {
            SDL_DisplayMode* desktopMode = SDL_GetDesktopDisplayMode(displayId);
            if (desktopMode == null)
            {
                throw new PixelyException($"SDL_GetDesktopDisplayMode failed: {SdlError.GetError()}");
            }

            pixelDensity = desktopMode->pixel_density;
        }

        return new Display(new DisplayId(displayId), ToRectangle(bounds), ToRectangle(usableBounds), pixelDensity, contentScale);
    }

    internal static void ThrowIfNotMainThread()
    {
        if (!SDL_IsMainThread())
        {
            throw new PixelyException("Displays must be read from the main thread.");
        }
    }

    private static Rectangle ToRectangle(SDL_Rect rect) => new(rect.x, rect.y, rect.w, rect.h);
}
