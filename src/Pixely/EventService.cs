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
    private readonly WindowRegistry _windowRegistry;
    private readonly AppControl _appControl;

    internal EventService(
        KeyboardService keyboardService,
        GamepadService gamepadService,
        MouseService mouseService,
        TextInputService textInputService,
        WindowRegistry windowRegistry,
        AppControl appControl)
    {
        _keyboardService = keyboardService;
        _gamepadService = gamepadService;
        _mouseService = mouseService;
        _textInputService = textInputService;
        _windowRegistry = windowRegistry;
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
                    if (_windowRegistry.TryGetWindow((uint)evt.key.windowID, out Window window))
                    {
                        _keyboardService.OnKeyEvent(window.ViewScope, evt.key.which, (Scancode)evt.key.scancode, (VirtualKey)evt.key.key, evt.key.down, evt.key.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_KEY_UP)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.key.windowID, out Window window))
                    {
                        _keyboardService.OnKeyEvent(window.ViewScope, evt.key.which, (Scancode)evt.key.scancode, (VirtualKey)evt.key.key, evt.key.down, evt.key.timestamp);
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
                    if (_windowRegistry.TryGetWindow((uint)evt.button.windowID, out Window window))
                    {
                        _mouseService.OnMouseButtonEvent(window.ViewScope, evt.button.which, (MouseButton)evt.button.button, new Vector2(evt.button.x, evt.button.y), evt.button.down, evt.button.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.button.windowID, out Window window))
                    {
                        _mouseService.OnMouseButtonEvent(window.ViewScope, evt.button.which, (MouseButton)evt.button.button, new Vector2(evt.button.x, evt.button.y), evt.button.down, evt.button.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_MOUSE_MOTION)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.motion.windowID, out Window window))
                    {
                        _mouseService.OnMouseMotionEvent(window.ViewScope, evt.motion.which, new Vector2(evt.motion.x, evt.motion.y), new Vector2(evt.motion.xrel, evt.motion.yrel), evt.motion.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_MOUSE_WHEEL)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.wheel.windowID, out Window window))
                    {
                        _mouseService.OnMouseWheelEvent(window.ViewScope, evt.wheel.which, new Vector2(evt.wheel.x, evt.wheel.y), new Vector2(evt.wheel.mouse_x, evt.wheel.mouse_y), evt.wheel.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.window.windowID, out Window window))
                    {
                        _mouseService.OnMouseWindowPresenceEvent(window.ViewScope, true, evt.window.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.window.windowID, out Window window))
                    {
                        _mouseService.OnMouseWindowPresenceEvent(window.ViewScope, false, evt.window.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_TEXT_INPUT)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.text.windowID, out Window window))
                    {
                        string text = Marshal.PtrToStringUTF8((IntPtr)evt.text.text) ?? string.Empty;
                        _textInputService.OnTextInputEvent(window.ViewScope, text, evt.text.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_TEXT_EDITING)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.edit.windowID, out Window window))
                    {
                        string text = Marshal.PtrToStringUTF8((IntPtr)evt.edit.text) ?? string.Empty;
                        _textInputService.OnTextEditingEvent(window.ViewScope, text, evt.edit.start, evt.edit.length, evt.edit.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.window.windowID, out Window pixelSizeWindow))
                    {
                        pixelSizeWindow.OnPixelSizeChanged(evt.window.timestamp);
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
                {
                    if (_windowRegistry.TryGetWindow((uint)evt.window.windowID, out Window closedWindow))
                    {
                        if (closedWindow.CloseBehavior == WindowCloseBehavior.QuitApplication)
                        {
                            _appControl.Quit();
                        }
                        else if (closedWindow.CloseBehavior == WindowCloseBehavior.HideWindow)
                        {
                            closedWindow.Hide();
                        }
                    }
                }
                else if (evt.Type == SDL_EventType.SDL_EVENT_QUIT)
                {
                    _appControl.Quit();
                }
            }
        }
    }
}
