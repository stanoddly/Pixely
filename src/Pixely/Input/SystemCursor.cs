using SDL;

namespace Pixely.Input;

// The shapes SDL 3.4 has. Browser archives are built from SDL 3.4, which rejects the shapes later SDL versions add.
public enum SystemCursor
{
    Default = SDL_SystemCursor.SDL_SYSTEM_CURSOR_DEFAULT,
    Text = SDL_SystemCursor.SDL_SYSTEM_CURSOR_TEXT,
    Wait = SDL_SystemCursor.SDL_SYSTEM_CURSOR_WAIT,
    Crosshair = SDL_SystemCursor.SDL_SYSTEM_CURSOR_CROSSHAIR,
    Progress = SDL_SystemCursor.SDL_SYSTEM_CURSOR_PROGRESS,
    NwseResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_NWSE_RESIZE,
    NeswResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_NESW_RESIZE,
    EwResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_EW_RESIZE,
    NsResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_NS_RESIZE,
    Move = SDL_SystemCursor.SDL_SYSTEM_CURSOR_MOVE,
    NotAllowed = SDL_SystemCursor.SDL_SYSTEM_CURSOR_NOT_ALLOWED,
    Pointer = SDL_SystemCursor.SDL_SYSTEM_CURSOR_POINTER,
    NwResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_NW_RESIZE,
    NResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_N_RESIZE,
    NeResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_NE_RESIZE,
    EResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_E_RESIZE,
    SeResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_SE_RESIZE,
    SResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_S_RESIZE,
    SwResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_SW_RESIZE,
    WResize = SDL_SystemCursor.SDL_SYSTEM_CURSOR_W_RESIZE
}
