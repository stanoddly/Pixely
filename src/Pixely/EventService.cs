using System.Numerics;
using System.Runtime.InteropServices;
using Pixely.Input;
using SDL;

namespace Pixely;

public class EventService
{
    private readonly KeyboardService _keyboardService;
    private readonly GamepadService _gamepadService;
    private readonly MouseService _mouseService;
    private readonly TextInputService _textInputService;
    private readonly PlatformSettings _platformSettings;
    private readonly Window _window;
    private readonly AppControl _appControl;

    internal EventService(
        KeyboardService keyboardService,
        GamepadService gamepadService,
        MouseService mouseService,
        TextInputService textInputService,
        PlatformSettings platformSettings,
        Window window,
        AppControl appControl)
    {
        _keyboardService = keyboardService;
        _gamepadService = gamepadService;
        _mouseService = mouseService;
        _textInputService = textInputService;
        _platformSettings = platformSettings;
        _window = window;
        _appControl = appControl;
    }

    public void Process()
    {
        unsafe
        {
            SDL_Event evt;
            while (SDL3.SDL_PollEvent(&evt) == true)
            {
                if (evt.Type == SDL_EventType.SDL_EVENT_KEY_DOWN)
                {
                    if (IsWindow(evt.key.windowID))
                    {
                        _keyboardService.OnKeyEvent(evt.key.which, (Scancode)evt.key.scancode, (VirtualKey)evt.key.key, evt.key.down, evt.key.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_KEY_UP)
                {
                    if (IsWindow(evt.key.windowID))
                    {
                        _keyboardService.OnKeyEvent(evt.key.which, (Scancode)evt.key.scancode, (VirtualKey)evt.key.key, evt.key.down, evt.key.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_GAMEPAD_ADDED)
                {
                    _gamepadService.OnGamepadAdded(evt.gdevice.which);
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED)
                {
                    _gamepadService.OnGamepadRemoved(evt.gdevice.which);
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION)
                {
                    _gamepadService.OnGamepadStickMotion(in evt.gaxis);
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN)
                {
                    _gamepadService.OnGamepadButtonPressed(evt.gbutton);
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP)
                {
                    _gamepadService.OnGamepadButtonReleased(evt.gbutton);
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN)
                {
                    if (IsWindow(evt.button.windowID))
                    {
                        _mouseService.OnMouseButtonEvent(evt.button.which, (MouseButton)evt.button.button, new Vector2(evt.button.x, evt.button.y), evt.button.down, evt.button.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP)
                {
                    if (IsWindow(evt.button.windowID))
                    {
                        _mouseService.OnMouseButtonEvent(evt.button.which, (MouseButton)evt.button.button, new Vector2(evt.button.x, evt.button.y), evt.button.down, evt.button.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_MOUSE_MOTION)
                {
                    if (IsWindow(evt.motion.windowID))
                    {
                        _mouseService.OnMouseMotionEvent(evt.motion.which, new Vector2(evt.motion.x, evt.motion.y), new Vector2(evt.motion.xrel, evt.motion.yrel), evt.motion.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_MOUSE_WHEEL)
                {
                    if (IsWindow(evt.wheel.windowID))
                    {
                        _mouseService.OnMouseWheelEvent(evt.wheel.which, new Vector2(evt.wheel.x, evt.wheel.y), new Vector2(evt.wheel.mouse_x, evt.wheel.mouse_y), evt.wheel.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER)
                {
                    if (IsWindow(evt.window.windowID))
                    {
                        _mouseService.OnMouseWindowPresenceEvent(true, evt.window.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE)
                {
                    if (IsWindow(evt.window.windowID))
                    {
                        _mouseService.OnMouseWindowPresenceEvent(false, evt.window.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_TEXT_INPUT)
                {
                    if (IsWindow(evt.text.windowID))
                    {
                        string text = Marshal.PtrToStringUTF8((IntPtr)evt.text.text) ?? string.Empty;
                        _textInputService.OnTextInputEvent(text, evt.text.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_TEXT_EDITING)
                {
                    if (IsWindow(evt.edit.windowID))
                    {
                        string text = Marshal.PtrToStringUTF8((IntPtr)evt.edit.text) ?? string.Empty;
                        _textInputService.OnTextEditingEvent(text, evt.edit.start, evt.edit.length, evt.edit.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED)
                {
                    if (IsWindow(evt.window.windowID))
                    {
                        _window.OnPixelSizeChanged(evt.window.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
                {
                    if (IsWindow(evt.window.windowID))
                    {
                        _appControl.Quit();
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_SYSTEM_THEME_CHANGED)
                {
                    _platformSettings.OnSystemThemeChanged(evt.common.timestamp);
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_LOCALE_CHANGED)
                {
                    _platformSettings.OnLocaleChanged(evt.common.timestamp);
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_QUIT)
                {
                    _appControl.Quit();
                }
            }
        }
    }

    // An event for another SDL window ID, such as 0 for no window, is ignored.
    private bool IsWindow(SDL_WindowID windowId) => (uint)windowId == _window.SdlId;
}
