using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.Headless;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationTests
{
    [Test]
    public void MouseMoveTo_UsesWindowPositionAndDerivesRelativeMotion()
    {
        (InputAutomation automation, MouseService mouseService, _, _) = CreateAutomation();
        List<(Vector2 Position, Vector2 RelativeMotion)> motions = new();
        mouseService.SubscribeMotion(0, eventArgs => motions.Add((eventArgs.Position, eventArgs.RelativeMotion)));

        automation.MouseMoveTo(new Vector2(20, 30));
        automation.MouseMoveTo(new Vector2(25, 28));

        Assert.That(motions, Is.EqualTo(new[]
        {
            (new Vector2(20, 30), new Vector2(20, 30)),
            (new Vector2(25, 28), new Vector2(5, -2))
        }));
    }

    [Test]
    public void MouseMoveBy_UsesCurrentAutomatedMousePosition()
    {
        (InputAutomation automation, MouseService mouseService, _, _) = CreateAutomation();
        List<(Vector2 Position, Vector2 RelativeMotion)> motions = new();
        mouseService.SubscribeMotion(0, eventArgs => motions.Add((eventArgs.Position, eventArgs.RelativeMotion)));
        automation.MouseMoveTo(new Vector2(20, 30));

        automation.MouseMoveBy(new Vector2(5, -2));

        Assert.That(motions[^1], Is.EqualTo((new Vector2(25, 28), new Vector2(5, -2))));
    }

    [Test]
    public void MouseClick_DispatchesMotionPressAndReleaseInOrder()
    {
        (InputAutomation automation, MouseService mouseService, _, _) = CreateAutomation();
        List<string> events = new();
        List<bool> pressedStates = new();
        mouseService.SubscribeMotion(0, _ => events.Add("motion"));
        mouseService.SubscribeButtonPress(0, eventArgs =>
        {
            events.Add("press");
            pressedStates.Add(eventArgs.Mouse.IsPressed(MouseButton.Left));
        });
        mouseService.SubscribeButtonRelease(0, eventArgs =>
        {
            events.Add("release");
            pressedStates.Add(eventArgs.Mouse.IsPressed(MouseButton.Left));
        });

        automation.MouseClick(MouseButton.Left, new Vector2(20, 30));

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(new[] { "motion", "press", "release" }));
            Assert.That(pressedStates, Is.EqualTo(new[] { true, false }));
        });
    }

    [Test]
    public void FirstMouseInput_RaisesWindowEnterOnceBeforeItsOwnEvent()
    {
        (InputAutomation automation, MouseService mouseService, _, _) = CreateAutomation();
        List<(string Event, bool IsInWindow)> events = new();
        mouseService.SubscribeWindowEnter(0, eventArgs => events.Add(("enter", mouseService.IsInWindow())));
        mouseService.SubscribeMotion(0, _ => events.Add(("motion", mouseService.IsInWindow())));
        bool isInWindowBefore = mouseService.IsInWindow();

        automation.MouseMoveTo(new Vector2(20, 30));
        automation.MouseMoveBy(new Vector2(1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(isInWindowBefore, Is.False);
            Assert.That(events, Is.EqualTo(new[] { ("enter", true), ("motion", true), ("motion", true) }));
        });
    }

    [Test]
    public void MouseLeave_RaisesWindowLeaveOnceAndTheNextInputEntersAgain()
    {
        (InputAutomation automation, MouseService mouseService, _, _) = CreateAutomation();
        List<(string Event, bool IsInWindow)> events = new();
        mouseService.SubscribeWindowEnter(0, _ => events.Add(("enter", mouseService.IsInWindow())));
        mouseService.SubscribeWindowLeave(0, eventArgs => events.Add(("leave", mouseService.IsInWindow())));
        mouseService.SubscribeButtonPress(0, _ => events.Add(("press", mouseService.IsInWindow())));

        automation.MouseLeave();
        automation.MouseDown(MouseButton.Left, new Vector2(20, 30));
        automation.MouseLeave();
        automation.MouseLeave();
        automation.MouseMoveBy(Vector2.One);

        Assert.That(events, Is.EqualTo(new[] { ("enter", true), ("press", true), ("leave", false), ("enter", true) }));
    }

    [Test]
    public void KeyPress_DispatchesDownAndUpWithCorrespondingState()
    {
        (InputAutomation automation, _, KeyboardService keyboardService, _) = CreateAutomation();
        List<(string Event, bool IsPressed)> events = new();
        keyboardService.SubscribeKeyDown(0, eventArgs => events.Add(("down", eventArgs.Keyboard.IsPressed(Scancode.A))));
        keyboardService.SubscribeKeyUp(0, eventArgs => events.Add(("up", eventArgs.Keyboard.IsPressed(Scancode.A))));

        automation.KeyPress(Scancode.A);

        Assert.That(events, Is.EqualTo(new[] { ("down", true), ("up", false) }));
    }

    [Test]
    public void TextInput_DispatchesTextUnchanged()
    {
        (InputAutomation automation, _, _, TextInputService textInputService) = CreateAutomation();
        string? receivedText = null;
        textInputService.SubscribeTextInput(0, eventArgs => receivedText = eventArgs.Text);

        automation.TextInput("Hello 👋");

        Assert.That(receivedText, Is.EqualTo("Hello 👋"));
    }

    [Test]
    public void Input_IsTimestampedWithGameTime()
    {
        TestFrameClock frameClock = new();
        (InputAutomation automation, _, KeyboardService keyboardService, _) = CreateAutomation(frameClock: frameClock);
        ulong? timestamp = null;
        keyboardService.SubscribeKeyDown(0, eventArgs => timestamp = eventArgs.Timestamp);
        frameClock.StartTestFrame(1500);

        automation.KeyDown(Scancode.A);

        Assert.That(timestamp, Is.EqualTo(1_500_000_000UL));
    }

    [Test]
    public void GamepadConnect_AddsGamepadAndRaisesConnected()
    {
        GamepadService gamepadService = new();
        (InputAutomation automation, _, _, _) = CreateAutomation(gamepadService);
        Gamepad? connected = null;
        gamepadService.GamepadConnected += gamepad => connected = gamepad;

        automation.GamepadConnect();

        Assert.Multiple(() =>
        {
            Assert.That(automation.IsGamepadConnected, Is.True);
            Assert.That(gamepadService.Gamepads, Is.EqualTo(new[] { connected }));
            Assert.That(() => automation.GamepadConnect(), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void GamepadInput_WithoutConnect_Throws()
    {
        (InputAutomation automation, _, _, _) = CreateAutomation();

        Assert.Multiple(() =>
        {
            Assert.That(() => automation.GamepadButtonPress(GamepadButton.South), Throws.InvalidOperationException);
            Assert.That(() => automation.GamepadAxisMotion(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, 1), Throws.InvalidOperationException);
            Assert.That(() => automation.GamepadDisconnect(), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void GamepadButtonPress_DispatchesPressAndReleaseWithCorrespondingState()
    {
        GamepadService gamepadService = new();
        (InputAutomation automation, _, _, _) = CreateAutomation(gamepadService);
        List<(string Event, GamepadButton Button, bool IsPressed)> events = new();
        gamepadService.SubscribeButtonPress(0, eventArgs => events.Add(("press", eventArgs.Button, eventArgs.Gamepad.IsPressed(eventArgs.Button))));
        gamepadService.SubscribeButtonRelease(0, eventArgs => events.Add(("release", eventArgs.Button, eventArgs.Gamepad.IsPressed(eventArgs.Button))));
        automation.GamepadConnect();

        automation.GamepadButtonPress(GamepadButton.South);

        Assert.That(events, Is.EqualTo(new[] { ("press", GamepadButton.South, true), ("release", GamepadButton.South, false) }));
    }

    [Test]
    public void GamepadAxisMotion_AppliesStickDeadZoneButNotToTriggers()
    {
        GamepadService gamepadService = new();
        (InputAutomation automation, _, _, _) = CreateAutomation(gamepadService);
        List<Vector2> sticks = new();
        List<float> triggers = new();
        gamepadService.SubscribeLeftStickMotion(0, eventArgs => sticks.Add(eventArgs.Value));
        gamepadService.SubscribeLeftTriggerMotion(0, eventArgs => triggers.Add(eventArgs.Value));
        automation.GamepadConnect();

        automation.GamepadAxisMotion(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, 0.1f);
        automation.GamepadAxisMotion(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, 0.5f);
        automation.GamepadAxisMotion(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER, 0.1f);

        Assert.Multiple(() =>
        {
            Assert.That(sticks, Is.EqualTo(new[] { new Vector2(0.375f, 0) }));
            Assert.That(triggers, Is.EqualTo(new[] { 0.1f }));
        });
    }

    [Test]
    public void GamepadDisconnect_ReleasesButtonsThenRecentersAxesBeforeDisconnecting()
    {
        GamepadService gamepadService = new();
        (InputAutomation automation, _, _, _) = CreateAutomation(gamepadService);
        automation.GamepadConnect();
        automation.GamepadAxisMotion(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY, -1);
        automation.GamepadAxisMotion(SDL.SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER, 0.5f);
        automation.GamepadButtonDown(GamepadButton.North);
        List<string> events = new();
        gamepadService.SubscribeLeftStickMotion(0, eventArgs => events.Add($"left stick {eventArgs.Value}"));
        gamepadService.SubscribeRightStickMotion(0, eventArgs => events.Add($"right stick {eventArgs.Value}"));
        gamepadService.SubscribeRightTriggerMotion(0, eventArgs => events.Add($"right trigger {eventArgs.Value}"));
        gamepadService.SubscribeButtonRelease(0, eventArgs => events.Add($"release {eventArgs.Button}"));
        gamepadService.GamepadDisconnected += _ => events.Add("disconnected");

        automation.GamepadDisconnect();

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(new[] { "release North", $"right stick {Vector2.Zero}", "right trigger 0", "disconnected" }));
            Assert.That(gamepadService.Gamepads, Is.Empty);
            Assert.That(automation.IsGamepadConnected, Is.False);
        });
    }

    internal static (InputAutomation Automation, MouseService MouseService, KeyboardService KeyboardService, TextInputService TextInputService) CreateAutomation(
        GamepadService? gamepadService = null, FrameClock? frameClock = null)
    {
        MouseService mouseService = new();
        KeyboardService keyboardService = new(new AppControl());
        TextInputService textInputService = new(CreateWindow(42));
        InputAutomation automation = new(mouseService, keyboardService, textInputService, gamepadService ?? new GamepadService(), frameClock ?? new TestFrameClock());
        return (automation, mouseService, keyboardService, textInputService);
    }

    // A window without an SDL window behind it, for code that only reads its SDL window ID.
    internal static Window CreateWindow(uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(SwapchainWindow));
        FieldInfo field = typeof(Window).GetField($"<{nameof(Window.SdlId)}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, sdlId);
        return window;
    }
}
