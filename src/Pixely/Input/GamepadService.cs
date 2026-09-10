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
    public int ButtonFlags { get; internal set; }
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
        SDL3.SDL_SetGamepadEventsEnabled(true);
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

        Gamepad pad = new Gamepad((uint)joystickId);
        _gamepads.Add(joystickId, pad);
        GamepadConnected?.Invoke(pad);
    }

    internal void OnGamepadRemoved(SDL_JoystickID joystickId)
    {
        if (!_gamepads.Remove(joystickId, out Gamepad? pad))
        {
            return;
        }

        GamepadDisconnected?.Invoke(pad);
    }

    internal void OnGamepadButtonPressed(SDL_GamepadButtonEvent gamepadButtonEvent)
    {
        SDL_JoystickID joystickId = gamepadButtonEvent.which;

        Gamepad gamepad = _gamepads[joystickId];

        if (gamepadButtonEvent.Button == SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID)
        {
            return;
        }

        int buttonState = (1 << (int)gamepadButtonEvent.Button);
        bool isPressedAlready = (buttonState & gamepad.ButtonFlags) != 0;

        if (isPressedAlready)
        {
            return;
        }

        gamepad.ButtonFlags |= buttonState;

        _buttonEventArgs.Gamepad = gamepad;
        _buttonEventArgs.Button = (GamepadButton)gamepadButtonEvent.Button;
        _buttonEventArgs.Timestamp = gamepadButtonEvent.timestamp;
        _buttonPressHandlers.Invoke(_buttonEventArgs);
    }

    internal void OnGamepadButtonReleased(SDL_GamepadButtonEvent gamepadButtonEvent)
    {
        SDL_JoystickID joystickId = gamepadButtonEvent.which;

        Gamepad gamepad = _gamepads[joystickId];

        if (gamepadButtonEvent.Button == SDL_GamepadButton.SDL_GAMEPAD_BUTTON_INVALID)
        {
            return;
        }

        int buttonState = (1 << (int)gamepadButtonEvent.Button);
        bool isPressed = (buttonState & gamepad.ButtonFlags) != 0;

        if (!isPressed)
        {
            return;
        }

        gamepad.ButtonFlags &= ~buttonState;

        _buttonEventArgs.Gamepad = gamepad;
        _buttonEventArgs.Button = (GamepadButton)gamepadButtonEvent.Button;
        _buttonEventArgs.Timestamp = gamepadButtonEvent.timestamp;
        _buttonReleaseHandlers.Invoke(_buttonEventArgs);
    }

    internal void OnGamepadStickMotion(in SDL_GamepadAxisEvent gamepadAxisEvent)
    {
        SDL_JoystickID joystickId = gamepadAxisEvent.which;

        Gamepad gamepad = _gamepads[joystickId];

        short value = gamepadAxisEvent.value;

        float normalizedValue = value switch
        {
            < 0 => value / JoystickMinDivisor,
            > 0 => value / JoystickMaxDivisor,
            _ => 0
        };

        // dead zone
        if (normalizedValue is < 0.2f and > -0.2f)
        {
            normalizedValue = 0f;
        }

        SDL_GamepadAxis gamepadAxis = (SDL_GamepadAxis)gamepadAxisEvent.axis;

        if (gamepadAxis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX)
        {
            Vector2 originalLeftStickState = gamepad.LeftStick;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (originalLeftStickState.X == normalizedValue)
            {
                return;
            }

            gamepad.LeftStick = originalLeftStickState with { X = normalizedValue };

            _stickEventArgs.Gamepad = gamepad;
            _stickEventArgs.Value = gamepad.LeftStick;
            _stickEventArgs.Timestamp = gamepadAxisEvent.timestamp;
            _leftStickMotionHandlers.Invoke(_stickEventArgs);
        }
        else if (gamepadAxis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY)
        {
            Vector2 originalLeftStickState = gamepad.LeftStick;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (originalLeftStickState.Y == normalizedValue)
            {
                return;
            }

            gamepad.LeftStick = originalLeftStickState with { Y = normalizedValue };

            _stickEventArgs.Gamepad = gamepad;
            _stickEventArgs.Value = gamepad.LeftStick;
            _stickEventArgs.Timestamp = gamepadAxisEvent.timestamp;
            _leftStickMotionHandlers.Invoke(_stickEventArgs);
        }
        else if (gamepadAxis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX)
        {
            Vector2 originalRightStickState = gamepad.RightStick;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (originalRightStickState.X == normalizedValue)
            {
                return;
            }

            gamepad.RightStick = originalRightStickState with { X = normalizedValue };

            _stickEventArgs.Gamepad = gamepad;
            _stickEventArgs.Value = gamepad.RightStick;
            _stickEventArgs.Timestamp = gamepadAxisEvent.timestamp;
            _rightStickMotionHandlers.Invoke(_stickEventArgs);
        }
        else if (gamepadAxis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY)
        {
            Vector2 originalRightStickState = gamepad.RightStick;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (originalRightStickState.Y == normalizedValue)
            {
                return;
            }

            gamepad.RightStick = originalRightStickState with { Y = normalizedValue };

            _stickEventArgs.Gamepad = gamepad;
            _stickEventArgs.Value = gamepad.RightStick;
            _stickEventArgs.Timestamp = gamepadAxisEvent.timestamp;
            _rightStickMotionHandlers.Invoke(_stickEventArgs);
        }
        else if (gamepadAxis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER)
        {
            float triggerValue = value / JoystickMaxDivisor;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (gamepad.LeftTrigger == triggerValue)
            {
                return;
            }

            gamepad.LeftTrigger = triggerValue;

            _triggerEventArgs.Gamepad = gamepad;
            _triggerEventArgs.Value = triggerValue;
            _triggerEventArgs.Timestamp = gamepadAxisEvent.timestamp;
            _leftTriggerMotionHandlers.Invoke(_triggerEventArgs);
        }
        else if (gamepadAxis == SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER)
        {
            float triggerValue = value / JoystickMaxDivisor;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (gamepad.RightTrigger == triggerValue)
            {
                return;
            }

            gamepad.RightTrigger = triggerValue;

            _triggerEventArgs.Gamepad = gamepad;
            _triggerEventArgs.Value = triggerValue;
            _triggerEventArgs.Timestamp = gamepadAxisEvent.timestamp;
            _rightTriggerMotionHandlers.Invoke(_triggerEventArgs);
        }
    }
}
