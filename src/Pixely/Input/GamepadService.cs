using System.Numerics;
using SDL;

namespace Pixely.Input;

public class Gamepad
{
    internal Gamepad(uint deviceId)
    {
        DeviceId = deviceId;
    }

    public uint DeviceId { get; }
    public Vector2 LeftStick { get; set; }
    public Vector2 RightStick { get; set; }
    public float LeftTrigger { get; set; }
    public float RightTrigger { get; set; }
    internal Vector2 RawLeftStick { get; set; }
    internal Vector2 RawRightStick { get; set; }
    internal int ButtonFlags { get; set; }

    public bool IsPressed(GamepadButton button)
    {
        return (ButtonFlags & (1 << (int)button)) != 0;
    }
}

public class GamepadButtonEventArgs : ConsumableInputEventArgs
{
    public Gamepad Gamepad { get; internal set; } = null!;
    public GamepadButton Button { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public class GamepadStickEventArgs : ConsumableInputEventArgs
{
    public Gamepad Gamepad { get; internal set; } = null!;
    public Vector2 Value { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public class GamepadTriggerEventArgs : ConsumableInputEventArgs
{
    public Gamepad Gamepad { get; internal set; } = null!;
    public float Value { get; internal set; }
    public ulong Timestamp { get; internal set; }
}

public delegate void GamepadConnectionEventHandler(Gamepad gamepad);

public class GamepadService : IGamepadService
{
    private readonly Dictionary<SDL_JoystickID, Gamepad> _gamepads = new();

    private const float JoystickMinDivisor = -1 * SDL3.SDL_JOYSTICK_AXIS_MIN;
    private const float JoystickMaxDivisor = SDL3.SDL_JOYSTICK_AXIS_MAX;
    private const float StickDeadZone = 0.2f;

    private static readonly SDL_GamepadAxis[] Axes =
    [
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX,
        SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER
    ];

    // Cached to avoid per-event allocations. Do not hold references to event args beyond the callback.
    private readonly GamepadButtonEventArgs _buttonEventArgs = new();
    private readonly GamepadStickEventArgs _stickEventArgs = new();
    private readonly GamepadTriggerEventArgs _triggerEventArgs = new();

    private readonly OrderedEventHandlers<GamepadStickEventArgs> _leftStickMotionHandlers = new();
    private readonly OrderedEventHandlers<GamepadStickEventArgs> _rightStickMotionHandlers = new();
    private readonly OrderedEventHandlers<GamepadTriggerEventArgs> _leftTriggerMotionHandlers = new();
    private readonly OrderedEventHandlers<GamepadTriggerEventArgs> _rightTriggerMotionHandlers = new();
    private readonly OrderedEventHandlers<GamepadButtonEventArgs> _buttonPressHandlers = new();
    private readonly OrderedEventHandlers<GamepadButtonEventArgs> _buttonReleaseHandlers = new();

    public IReadOnlyCollection<Gamepad> Gamepads => _gamepads.Values;

    public event InputEventHandler<GamepadStickEventArgs> LeftStickMotion
    {
        add => _leftStickMotionHandlers.Add(0, value);
        remove => _leftStickMotionHandlers.Remove(value);
    }

    public event InputEventHandler<GamepadStickEventArgs> RightStickMotion
    {
        add => _rightStickMotionHandlers.Add(0, value);
        remove => _rightStickMotionHandlers.Remove(value);
    }

    public event InputEventHandler<GamepadTriggerEventArgs> LeftTriggerMotion
    {
        add => _leftTriggerMotionHandlers.Add(0, value);
        remove => _leftTriggerMotionHandlers.Remove(value);
    }

    public event InputEventHandler<GamepadTriggerEventArgs> RightTriggerMotion
    {
        add => _rightTriggerMotionHandlers.Add(0, value);
        remove => _rightTriggerMotionHandlers.Remove(value);
    }

    public event InputEventHandler<GamepadButtonEventArgs> ButtonPress
    {
        add => _buttonPressHandlers.Add(0, value);
        remove => _buttonPressHandlers.Remove(value);
    }

    public event InputEventHandler<GamepadButtonEventArgs> ButtonRelease
    {
        add => _buttonReleaseHandlers.Add(0, value);
        remove => _buttonReleaseHandlers.Remove(value);
    }

    public event GamepadConnectionEventHandler? GamepadConnected;
    public event GamepadConnectionEventHandler? GamepadDisconnected;

    public void SubscribeLeftStickMotion(int order, InputEventHandler<GamepadStickEventArgs> handler)
    {
        _leftStickMotionHandlers.Add(order, handler);
    }

    public void SubscribeRightStickMotion(int order, InputEventHandler<GamepadStickEventArgs> handler)
    {
        _rightStickMotionHandlers.Add(order, handler);
    }

    public void SubscribeLeftTriggerMotion(int order, InputEventHandler<GamepadTriggerEventArgs> handler)
    {
        _leftTriggerMotionHandlers.Add(order, handler);
    }

    public void SubscribeRightTriggerMotion(int order, InputEventHandler<GamepadTriggerEventArgs> handler)
    {
        _rightTriggerMotionHandlers.Add(order, handler);
    }

    public void SubscribeButtonPress(int order, InputEventHandler<GamepadButtonEventArgs> handler)
    {
        _buttonPressHandlers.Add(order, handler);
    }

    public void SubscribeButtonRelease(int order, InputEventHandler<GamepadButtonEventArgs> handler)
    {
        _buttonReleaseHandlers.Add(order, handler);
    }

    internal void SetupGamepads()
    {
        SdlBoolInterop.SDL_SetGamepadEventsEnabled(true);
        unsafe
        {
            int count;
            SDL_JoystickID* gamepads = SDL3.SDL_GetGamepads(&count);

            if (gamepads == null)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                SDL_Gamepad* gamepad = SDL3.SDL_OpenGamepad(gamepads[i]);

                if (gamepad == null)
                {
                    continue;
                }

                _gamepads.Add(gamepads[i], new Gamepad((uint)gamepads[i]));
            }

            SDL3.SDL_free(gamepads);
        }
    }

    internal unsafe void OnGamepadAdded(SDL_JoystickID joystickId)
    {
        if (_gamepads.ContainsKey(joystickId))
        {
            return;
        }

        SDL_Gamepad* gamepad = SDL3.SDL_OpenGamepad(joystickId);

        if (gamepad == null)
        {
            return;
        }

        AddGamepad(joystickId);
    }

    // Also the connect of a synthetic gamepad, which SDL does not know and so cannot open.
    internal Gamepad AddGamepad(SDL_JoystickID joystickId)
    {
        Gamepad pad = new Gamepad((uint)joystickId);
        _gamepads.Add(joystickId, pad);
        GamepadConnected?.Invoke(pad);
        return pad;
    }

    internal void OnGamepadRemoved(SDL_JoystickID joystickId)
    {
        if (!_gamepads.Remove(joystickId, out Gamepad? pad))
        {
            return;
        }

        GamepadDisconnected?.Invoke(pad);
    }

    // The unplug of a synthetic gamepad, as SDL reports a real one: every held button is released and every axis returns to 0, in that
    // order, before the gamepad goes.
    internal void RemoveGamepad(SDL_JoystickID joystickId, ulong timestamp)
    {
        Gamepad gamepad = _gamepads[joystickId];
        foreach (GamepadButton button in Enum.GetValues<GamepadButton>())
        {
            if (gamepad.IsPressed(button))
            {
                OnGamepadButtonEvent(joystickId, button, false, timestamp);
            }
        }

        foreach (SDL_GamepadAxis axis in Axes)
        {
            OnGamepadAxisMotion(joystickId, axis, 0, timestamp);
        }

        OnGamepadRemoved(joystickId);
    }

    internal void OnGamepadButtonPressed(SDL_GamepadButtonEvent gamepadButtonEvent)
    {
        OnGamepadButtonEvent(gamepadButtonEvent.which, (GamepadButton)gamepadButtonEvent.Button, true, gamepadButtonEvent.timestamp);
    }

    internal void OnGamepadButtonReleased(SDL_GamepadButtonEvent gamepadButtonEvent)
    {
        OnGamepadButtonEvent(gamepadButtonEvent.which, (GamepadButton)gamepadButtonEvent.Button, false, gamepadButtonEvent.timestamp);
    }

    internal void OnGamepadButtonEvent(SDL_JoystickID joystickId, GamepadButton button, bool pressed, ulong timestamp)
    {
        Gamepad gamepad = _gamepads[joystickId];

        if (button == GamepadButton.Invalid)
        {
            return;
        }

        if (gamepad.IsPressed(button) == pressed)
        {
            return;
        }

        gamepad.ButtonFlags ^= 1 << (int)button;

        _buttonEventArgs.Gamepad = gamepad;
        _buttonEventArgs.Button = button;
        _buttonEventArgs.Timestamp = timestamp;
        (pressed ? _buttonPressHandlers : _buttonReleaseHandlers).Invoke(_buttonEventArgs);
    }

    internal void OnGamepadStickMotion(in SDL_GamepadAxisEvent gamepadAxisEvent)
    {
        short value = gamepadAxisEvent.value;

        float normalizedValue = value switch
        {
            < 0 => value / JoystickMinDivisor,
            > 0 => value / JoystickMaxDivisor,
            _ => 0
        };

        OnGamepadAxisMotion(gamepadAxisEvent.which, (SDL_GamepadAxis)gamepadAxisEvent.axis, normalizedValue, gamepadAxisEvent.timestamp);
    }

    // Sticks range from -1 to 1 and triggers from 0 to 1.
    internal void OnGamepadAxisMotion(SDL_JoystickID joystickId, SDL_GamepadAxis axis, float value, ulong timestamp)
    {
        Gamepad gamepad = _gamepads[joystickId];

        switch (axis)
        {
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX or SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY:
            {
                MoveStick(gamepad, true, WithAxis(gamepad.RawLeftStick, axis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, value), timestamp);
                break;
            }
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX or SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY:
            {
                MoveStick(gamepad, false, WithAxis(gamepad.RawRightStick, axis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX, value), timestamp);
                break;
            }
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER:
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (gamepad.LeftTrigger == value)
                {
                    return;
                }

                gamepad.LeftTrigger = value;
                InvokeTriggerMotion(_leftTriggerMotionHandlers, gamepad, value, timestamp);
                break;
            }
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER:
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (gamepad.RightTrigger == value)
                {
                    return;
                }

                gamepad.RightTrigger = value;
                InvokeTriggerMotion(_rightTriggerMotionHandlers, gamepad, value, timestamp);
                break;
            }
        }
    }

    // Both axes of a synthetic stick at once, so a move is one event; SDL reports each axis of a real stick as its own event.
    internal void OnGamepadStickMotion(SDL_JoystickID joystickId, bool isLeft, Vector2 value, ulong timestamp)
    {
        MoveStick(_gamepads[joystickId], isLeft, value, timestamp);
    }

    private void MoveStick(Gamepad gamepad, bool isLeft, Vector2 rawValue, ulong timestamp)
    {
        Vector2 value = ApplyDeadZone(rawValue);
        if (isLeft)
        {
            gamepad.RawLeftStick = rawValue;
            if (value == gamepad.LeftStick)
            {
                return;
            }

            gamepad.LeftStick = value;
            InvokeStickMotion(_leftStickMotionHandlers, gamepad, value, timestamp);
        }
        else
        {
            gamepad.RawRightStick = rawValue;
            if (value == gamepad.RightStick)
            {
                return;
            }

            gamepad.RightStick = value;
            InvokeStickMotion(_rightStickMotionHandlers, gamepad, value, timestamp);
        }
    }

    // Radial, so the direction is kept: the length is rescaled from the dead zone's edge to 1 and capped at 1.
    private static Vector2 ApplyDeadZone(Vector2 stick)
    {
        float length = stick.Length();
        if (length < StickDeadZone)
        {
            return Vector2.Zero;
        }

        float scaledLength = Math.Min((length - StickDeadZone) / (1 - StickDeadZone), 1);
        return stick * (scaledLength / length);
    }

    private static Vector2 WithAxis(Vector2 stick, bool isX, float value)
    {
        return isX ? stick with { X = value } : stick with { Y = value };
    }

    private void InvokeStickMotion(OrderedEventHandlers<GamepadStickEventArgs> handlers, Gamepad gamepad, Vector2 value, ulong timestamp)
    {
        _stickEventArgs.Gamepad = gamepad;
        _stickEventArgs.Value = value;
        _stickEventArgs.Timestamp = timestamp;
        handlers.Invoke(_stickEventArgs);
    }

    private void InvokeTriggerMotion(OrderedEventHandlers<GamepadTriggerEventArgs> handlers, Gamepad gamepad, float value, ulong timestamp)
    {
        _triggerEventArgs.Gamepad = gamepad;
        _triggerEventArgs.Value = value;
        _triggerEventArgs.Timestamp = timestamp;
        handlers.Invoke(_triggerEventArgs);
    }
}
