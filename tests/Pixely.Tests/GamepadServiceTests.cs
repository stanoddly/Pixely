using System.Numerics;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

public sealed class GamepadServiceTests
{
    private const SDL_JoystickID JoystickId = (SDL_JoystickID)7;

    [TestCase(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, (short)32767, "left stick <1, 0>")]
    [TestCase(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, (short)-32768, "left stick <0, -1>")]
    [TestCase(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX, (short)-16384, "right stick <-0.375, 0>")]
    [TestCase(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, (short)3000)]
    [TestCase(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, (short)32767, "left trigger 1")]
    [TestCase(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, (short)3000, "right trigger 0.09155553")]
    public void OnGamepadStickMotion_NormalizesTheSdlValue(SDL_GamepadAxis axis, short value, params string[] expectedEvents)
    {
        (GamepadService gamepadService, List<string> events) = CreateService();

        gamepadService.OnGamepadStickMotion(new SDL_GamepadAxisEvent { which = JoystickId, axis = (byte)axis, value = value, timestamp = 5 });

        Assert.That(events, Is.EqualTo(expectedEvents));
    }

    [Test]
    public void OnGamepadButtonPressedAndReleased_DispatchOnlyStateChanges()
    {
        (GamepadService gamepadService, List<string> events) = CreateService();
        SDL_GamepadButtonEvent buttonEvent = new() { which = JoystickId, button = (byte)SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, timestamp = 5 };

        gamepadService.OnGamepadButtonPressed(buttonEvent);
        gamepadService.OnGamepadButtonPressed(buttonEvent);
        gamepadService.OnGamepadButtonReleased(buttonEvent);
        gamepadService.OnGamepadButtonReleased(buttonEvent);

        Assert.That(events, Is.EqualTo(new[] { "press East", "release East" }));
    }

    [Test]
    public void OnGamepadAxisMotion_KeepsTheDirectionOfAStickTiltedSlightlyOffAnAxis()
    {
        (GamepadService gamepadService, List<Vector2> sticks) = CreateServiceWithLeftStick();

        gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, 0.1f, 5);
        gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, 0.5f, 5);

        Vector2 expected = Vector2.Normalize(new Vector2(0.1f, 0.5f)) * ((new Vector2(0.1f, 0.5f).Length() - 0.2f) / 0.8f);
        Assert.That(sticks, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(sticks[0].X, Is.EqualTo(expected.X).Within(1e-6f));
            Assert.That(sticks[0].Y, Is.EqualTo(expected.Y).Within(1e-6f));
        });
    }

    [Test]
    public void OnGamepadAxisMotion_EndsAReleasedDiagonalOnTheDiagonal()
    {
        (GamepadService gamepadService, List<Vector2> sticks) = CreateServiceWithLeftStick();

        foreach (float value in new[] { 0.5f, 0.3f, 0.15f, 0.1f, 0f })
        {
            gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, value, 5);
            gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, value, 5);
        }

        Vector2 lastBeforeCentre = sticks[^2];
        Assert.Multiple(() =>
        {
            Assert.That(sticks[^1], Is.EqualTo(Vector2.Zero));
            Assert.That(lastBeforeCentre.X, Is.Positive);
            Assert.That(lastBeforeCentre.X, Is.EqualTo(lastBeforeCentre.Y));
        });
    }

    [Test]
    public void OnGamepadAxisMotion_RescalesTheStickLengthFromTheDeadZoneEdgeAndCapsItAtOne()
    {
        (GamepadService gamepadService, List<Vector2> sticks) = CreateServiceWithLeftStick();

        gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, 0.19f, 5);
        gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, 0.6f, 5);
        gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, 1f, 5);
        gamepadService.OnGamepadAxisMotion(JoystickId, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY, 1f, 5);

        Assert.That(sticks.Select(stick => stick.Length()), Is.EqualTo(new[] { 0.5f, 1f, 1f }).Within(1e-6f));
    }

    private static (GamepadService GamepadService, List<Vector2> Sticks) CreateServiceWithLeftStick()
    {
        GamepadService gamepadService = new();
        gamepadService.AddGamepad(JoystickId);
        List<Vector2> sticks = new();
        gamepadService.SubscribeLeftStickMotion(0, eventArgs => sticks.Add(eventArgs.Value));
        return (gamepadService, sticks);
    }

    private static (GamepadService GamepadService, List<string> Events) CreateService()
    {
        GamepadService gamepadService = new();
        gamepadService.AddGamepad(JoystickId);
        List<string> events = new();
        gamepadService.SubscribeLeftStickMotion(0, eventArgs => events.Add($"left stick {eventArgs.Value}"));
        gamepadService.SubscribeRightStickMotion(0, eventArgs => events.Add($"right stick {eventArgs.Value}"));
        gamepadService.SubscribeLeftTriggerMotion(0, eventArgs => events.Add($"left trigger {eventArgs.Value}"));
        gamepadService.SubscribeRightTriggerMotion(0, eventArgs => events.Add($"right trigger {eventArgs.Value}"));
        gamepadService.SubscribeButtonPress(0, eventArgs => events.Add($"press {eventArgs.Button}"));
        gamepadService.SubscribeButtonRelease(0, eventArgs => events.Add($"release {eventArgs.Button}"));
        return (gamepadService, events);
    }
}
