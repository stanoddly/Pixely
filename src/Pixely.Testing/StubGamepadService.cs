using System.Numerics;
using Pixely.Input;
using SDL;

namespace Pixely.Testing;

/// <summary>
/// Gamepads for a game's tests, which connect and move through Pixely's own gamepad service. Handlers run in their order, a
/// consumed event stops there, a stick goes through Pixely's dead zone, and each <see cref="Gamepad"/> keeps its buttons, sticks
/// and triggers, as in the game. No gamepad is connected until <see cref="Connect"/>.
/// </summary>
public sealed class StubGamepadService : IGamepadService
{
    private readonly GamepadService _gamepadService = new();
    private uint _lastDeviceId;
    private bool _raising;

    public IReadOnlyCollection<Gamepad> Gamepads => _gamepadService.Gamepads;

    public event InputEventHandler<GamepadStickEventArgs> LeftStickMotion
    {
        add => _gamepadService.LeftStickMotion += value;
        remove => _gamepadService.LeftStickMotion -= value;
    }

    public event InputEventHandler<GamepadStickEventArgs> RightStickMotion
    {
        add => _gamepadService.RightStickMotion += value;
        remove => _gamepadService.RightStickMotion -= value;
    }

    public event InputEventHandler<GamepadTriggerEventArgs> LeftTriggerMotion
    {
        add => _gamepadService.LeftTriggerMotion += value;
        remove => _gamepadService.LeftTriggerMotion -= value;
    }

    public event InputEventHandler<GamepadTriggerEventArgs> RightTriggerMotion
    {
        add => _gamepadService.RightTriggerMotion += value;
        remove => _gamepadService.RightTriggerMotion -= value;
    }

    public event InputEventHandler<GamepadButtonEventArgs> ButtonPress
    {
        add => _gamepadService.ButtonPress += value;
        remove => _gamepadService.ButtonPress -= value;
    }

    public event InputEventHandler<GamepadButtonEventArgs> ButtonRelease
    {
        add => _gamepadService.ButtonRelease += value;
        remove => _gamepadService.ButtonRelease -= value;
    }

    public event GamepadConnectionEventHandler? GamepadConnected
    {
        add => _gamepadService.GamepadConnected += value;
        remove => _gamepadService.GamepadConnected -= value;
    }

    public event GamepadConnectionEventHandler? GamepadDisconnected
    {
        add => _gamepadService.GamepadDisconnected += value;
        remove => _gamepadService.GamepadDisconnected -= value;
    }

    public void SubscribeLeftStickMotion(int order, InputEventHandler<GamepadStickEventArgs> handler)
    {
        _gamepadService.SubscribeLeftStickMotion(order, handler);
    }

    public void SubscribeRightStickMotion(int order, InputEventHandler<GamepadStickEventArgs> handler)
    {
        _gamepadService.SubscribeRightStickMotion(order, handler);
    }

    public void SubscribeLeftTriggerMotion(int order, InputEventHandler<GamepadTriggerEventArgs> handler)
    {
        _gamepadService.SubscribeLeftTriggerMotion(order, handler);
    }

    public void SubscribeRightTriggerMotion(int order, InputEventHandler<GamepadTriggerEventArgs> handler)
    {
        _gamepadService.SubscribeRightTriggerMotion(order, handler);
    }

    public void SubscribeButtonPress(int order, InputEventHandler<GamepadButtonEventArgs> handler)
    {
        _gamepadService.SubscribeButtonPress(order, handler);
    }

    public void SubscribeButtonRelease(int order, InputEventHandler<GamepadButtonEventArgs> handler)
    {
        _gamepadService.SubscribeButtonRelease(order, handler);
    }

    /// <summary>
    /// Connects a new gamepad, with its own <see cref="Gamepad.DeviceId"/>, and raises <see cref="GamepadConnected"/>.
    /// </summary>
    public Gamepad Connect()
    {
        _lastDeviceId++;
        Gamepad? gamepad = null;
        Raise(() => gamepad = _gamepadService.AddGamepad((SDL_JoystickID)_lastDeviceId));
        return gamepad!;
    }

    /// <summary>
    /// Unplugs the gamepad as SDL reports a real one: its held buttons are released and its sticks and triggers return to rest
    /// before <see cref="GamepadDisconnected"/>.
    /// </summary>
    public void Disconnect(Gamepad gamepad)
    {
        SDL_JoystickID deviceId = GetDeviceId(gamepad);
        Raise(() => _gamepadService.RemoveGamepad(deviceId, 0));
    }

    /// <summary>
    /// Presses the button and holds it. A button already held raises nothing.
    /// </summary>
    public void Down(Gamepad gamepad, GamepadButton button)
    {
        SDL_JoystickID deviceId = GetDeviceId(gamepad);
        ValidateButton(button);
        Raise(() => _gamepadService.OnGamepadButtonEvent(deviceId, button, true, 0));
    }

    public void Up(Gamepad gamepad, GamepadButton button)
    {
        SDL_JoystickID deviceId = GetDeviceId(gamepad);
        ValidateButton(button);
        Raise(() => _gamepadService.OnGamepadButtonEvent(deviceId, button, false, 0));
    }

    /// <summary>
    /// Presses the button and releases it.
    /// </summary>
    public void Press(Gamepad gamepad, GamepadButton button)
    {
        Down(gamepad, button);
        Up(gamepad, button);
    }

    /// <summary>
    /// Moves the stick to the position, each axis from -1 to 1 with Y growing downwards. The game sees the position through Pixely's
    /// dead zone, so a small one reads as the stick at rest, and the move raises one event, or none when what the game sees stays
    /// the same.
    /// </summary>
    public void MoveLeftStick(Gamepad gamepad, Vector2 position)
    {
        SDL_JoystickID deviceId = GetDeviceId(gamepad);
        ValidateStick(position);
        Raise(() => _gamepadService.OnGamepadStickMotion(deviceId, true, position, 0));
    }

    /// <inheritdoc cref="MoveLeftStick"/>
    public void MoveRightStick(Gamepad gamepad, Vector2 position)
    {
        SDL_JoystickID deviceId = GetDeviceId(gamepad);
        ValidateStick(position);
        Raise(() => _gamepadService.OnGamepadStickMotion(deviceId, false, position, 0));
    }

    /// <summary>
    /// Pulls the trigger from 0, at rest, to 1, fully pulled. A value the trigger already has raises nothing.
    /// </summary>
    public void MoveLeftTrigger(Gamepad gamepad, float value)
    {
        SDL_JoystickID deviceId = GetDeviceId(gamepad);
        ValidateTrigger(value);
        Raise(() => _gamepadService.OnGamepadAxisMotion(deviceId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, value, 0));
    }

    /// <inheritdoc cref="MoveLeftTrigger"/>
    public void MoveRightTrigger(Gamepad gamepad, float value)
    {
        SDL_JoystickID deviceId = GetDeviceId(gamepad);
        ValidateTrigger(value);
        Raise(() => _gamepadService.OnGamepadAxisMotion(deviceId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, value, 0));
    }

    // The service reuses one event args object per kind and SDL never calls it from a handler, so a call from a handler would
    // change the event the handlers after it get.
    private void Raise(Action raise)
    {
        if (_raising)
        {
            throw new InvalidOperationException($"{nameof(StubGamepadService)} cannot be called from one of its own event handlers.");
        }

        _raising = true;
        try
        {
            raise();
        }
        finally
        {
            _raising = false;
        }
    }

    private SDL_JoystickID GetDeviceId(Gamepad gamepad)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        if (!_gamepadService.Gamepads.Contains(gamepad))
        {
            throw new InvalidOperationException($"Gamepad {gamepad.DeviceId} is not connected to this {nameof(StubGamepadService)}.");
        }

        return (SDL_JoystickID)gamepad.DeviceId;
    }

    internal static void ValidateButton(GamepadButton button)
    {
        if (button is < 0 or >= GamepadButton.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "Not a gamepad button.");
        }
    }

    // Written so that NaN fails, as every comparison with it is false.
    internal static void ValidateStick(Vector2 position)
    {
        if (!(position.X is >= -1 and <= 1 && position.Y is >= -1 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Each axis of a stick ranges from -1 to 1.");
        }
    }

    // Written so that NaN fails, as every comparison with it is false.
    internal static void ValidateTrigger(float value)
    {
        if (value is not (>= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A trigger ranges from 0 to 1.");
        }
    }
}
