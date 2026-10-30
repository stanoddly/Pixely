using SDL;

namespace Pixely;

// SDL3 loaded into the process, which needs no SDL_Init. On Linux the libraries of SDL_ttf and SDL_image link against
// libSDL3.so.0, which the native package ships only as libSDL3.so, so they load only once SDL3 has. A service whose first
// native call goes to one of them depends on this one.
internal sealed class SdlLibrary
{
    private SdlLibrary()
    {
    }

    internal static SdlLibrary Load()
    {
        // Any SDL3 call loads the library.
        _ = SDL3.SDL_GetVersion();
        return new SdlLibrary();
    }
}
