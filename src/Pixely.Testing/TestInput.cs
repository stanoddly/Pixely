using System.Numerics;
using Pixely.Headless;
using Pixely.Input;
using SDL;

namespace Pixely.Testing;

/// <summary>
/// Synthetic mouse, keyboard, text and gamepad input for an app from <see cref="TestApp.CreateBuilder"/>, the input of headless mode
/// (<c>docs/headless.md</c>). A call is queued and reaches the game at <see cref="UpdateOrders.Input"/> of the next
/// <c>RunFrame</c>, after that frame's stage change, so it reaches the stage the frame runs. Each view has its own mouse and keyboard,
/// and there is one gamepad, which <see cref="GamepadConnect"/> connects.
/// </summary>
public sealed class TestInput : IUpdatable
{
    private readonly InputAutomation _automation;
    private List<Action<InputAutomation>> _queued = new();

    internal TestInput(InputAutomation automation)
    {
        _automation = automation;
    }

    int IUpdatable.UpdateOrder => UpdateOrders.Input;

    public void MouseMoveTo(Vector2 windowPosition, ViewScope viewScope = default)
    {
        _queued.Add(automation => automation.MouseMoveTo(windowPosition, viewScope));
    }

    public void MouseMoveBy(Vector2 delta, ViewScope viewScope = default)
    {
        _queued.Add(automation => automation.MouseMoveBy(delta, viewScope));
    }

    public void MouseDown(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateMouseButton(button);
        _queued.Add(automation => automation.MouseDown(button, windowPosition, viewScope));
    }

    public void MouseUp(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateMouseButton(button);
        _queued.Add(automation => automation.MouseUp(button, windowPosition, viewScope));
    }

    public void MouseClick(MouseButton button, Vector2 windowPosition, ViewScope viewScope = default)
    {
        ValidateMouseButton(button);
        _queued.Add(automation => automation.MouseClick(button, windowPosition, viewScope));
    }

    public void MouseWheel(Vector2 delta, Vector2 windowPosition, ViewScope viewScope = default)
    {
        _queued.Add(automation => automation.MouseWheel(delta, windowPosition, viewScope));
    }

    public void MouseLeave(ViewScope viewScope = default)
    {
        _queued.Add(automation => automation.MouseLeave(viewScope));
    }

    public void KeyDown(Scancode scancode, ViewScope viewScope = default)
    {
        StubKeyboardService.ValidateScancode(scancode);
        _queued.Add(automation => automation.KeyDown(scancode, viewScope));
    }

    public void KeyUp(Scancode scancode, ViewScope viewScope = default)
    {
        StubKeyboardService.ValidateScancode(scancode);
        _queued.Add(automation => automation.KeyUp(scancode, viewScope));
    }

    public void KeyPress(Scancode scancode, ViewScope viewScope = default)
    {
        StubKeyboardService.ValidateScancode(scancode);
        _queued.Add(automation => automation.KeyPress(scancode, viewScope));
    }

    public void TextInput(string text, ViewScope viewScope = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        _queued.Add(automation => automation.TextInput(text, viewScope));
    }

    public void GamepadConnect()
    {
        _queued.Add(static automation => automation.GamepadConnect());
    }

    public void GamepadDisconnect()
    {
        _queued.Add(static automation => automation.GamepadDisconnect());
    }

    public void GamepadButtonDown(GamepadButton button)
    {
        StubGamepadService.ValidateButton(button);
        _queued.Add(automation => automation.GamepadButtonDown(button));
    }

    public void GamepadButtonUp(GamepadButton button)
    {
        StubGamepadService.ValidateButton(button);
        _queued.Add(automation => automation.GamepadButtonUp(button));
    }

    public void GamepadButtonPress(GamepadButton button)
    {
        StubGamepadService.ValidateButton(button);
        _queued.Add(automation => automation.GamepadButtonPress(button));
    }

    /// <summary>Moves the left stick, each axis from -1 to 1 with Y growing downwards, as the gamepad API reports it.</summary>
    public void GamepadLeftStick(Vector2 position)
    {
        StubGamepadService.ValidateStick(position);
        _queued.Add(automation => MoveStick(automation, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, position));
    }

    /// <summary>Moves the right stick, each axis from -1 to 1 with Y growing downwards, as the gamepad API reports it.</summary>
    public void GamepadRightStick(Vector2 position)
    {
        StubGamepadService.ValidateStick(position);
        _queued.Add(automation => MoveStick(automation, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, position));
    }

    /// <summary>Pulls the left trigger from 0, at rest, to 1, fully pulled.</summary>
    public void GamepadLeftTrigger(float value)
    {
        StubGamepadService.ValidateTrigger(value);
        _queued.Add(automation => automation.GamepadAxisMotion(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, value));
    }

    /// <summary>Pulls the right trigger from 0, at rest, to 1, fully pulled.</summary>
    public void GamepadRightTrigger(float value)
    {
        StubGamepadService.ValidateTrigger(value);
        _queued.Add(automation => automation.GamepadAxisMotion(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, value));
    }

    void IUpdatable.Update()
    {
        // Input sent from a handler during the replay goes to the next frame, as input sent between frames does.
        List<Action<InputAutomation>> queued = _queued;
        _queued = new List<Action<InputAutomation>>();
        foreach (Action<InputAutomation> call in queued)
        {
            call(_automation);
        }
    }

    // Each axis is its own event, as from SDL.
    private static void MoveStick(InputAutomation automation, SDL_GamepadAxis xAxis, SDL_GamepadAxis yAxis, Vector2 position)
    {
        automation.GamepadAxisMotion(xAxis, position.X);
        automation.GamepadAxisMotion(yAxis, position.Y);
    }

    private static void ValidateMouseButton(MouseButton button)
    {
        if (button is < MouseButton.Left or > MouseButton.X2)
        {
            throw new ArgumentOutOfRangeException(nameof(button), button, "Not a mouse button.");
        }
    }
}
