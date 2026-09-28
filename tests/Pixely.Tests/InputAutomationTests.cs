using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationTests
{
    private static readonly ViewScope _viewScope = new(7);

    [Test]
    public void MouseMoveTo_UsesWindowPositionAndDerivesRelativeMotion()
    {
        (InputAutomation automation, MouseService mouseService, _, _) = CreateAutomation();
        List<(Vector2 Position, Vector2 RelativeMotion)> motions = new();
        mouseService.SubscribeMotion(_viewScope, 0, eventArgs => motions.Add((eventArgs.Position, eventArgs.RelativeMotion)));

        automation.MouseMoveTo(new Vector2(20, 30), _viewScope);
        automation.MouseMoveTo(new Vector2(25, 28), _viewScope);

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
        mouseService.SubscribeMotion(_viewScope, 0, eventArgs => motions.Add((eventArgs.Position, eventArgs.RelativeMotion)));
        automation.MouseMoveTo(new Vector2(20, 30), _viewScope);

        automation.MouseMoveBy(new Vector2(5, -2), _viewScope);

        Assert.That(motions[^1], Is.EqualTo((new Vector2(25, 28), new Vector2(5, -2))));
    }

    [Test]
    public void MouseClick_DispatchesMotionPressAndReleaseInOrder()
    {
        (InputAutomation automation, MouseService mouseService, _, _) = CreateAutomation();
        List<string> events = new();
        List<bool> pressedStates = new();
        mouseService.SubscribeMotion(_viewScope, 0, _ => events.Add("motion"));
        mouseService.SubscribeButtonPress(_viewScope, 0, eventArgs =>
        {
            events.Add("press");
            pressedStates.Add(eventArgs.Mouse.IsPressed(MouseButton.Left));
        });
        mouseService.SubscribeButtonRelease(_viewScope, 0, eventArgs =>
        {
            events.Add("release");
            pressedStates.Add(eventArgs.Mouse.IsPressed(MouseButton.Left));
        });

        automation.MouseClick(MouseButton.Left, new Vector2(20, 30), _viewScope);

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
        mouseService.SubscribeWindowEnter(_viewScope, 0, eventArgs => events.Add(("enter", mouseService.IsInWindow(_viewScope))));
        mouseService.SubscribeMotion(_viewScope, 0, _ => events.Add(("motion", mouseService.IsInWindow(_viewScope))));
        bool isInWindowBefore = mouseService.IsInWindow(_viewScope);

        automation.MouseMoveTo(new Vector2(20, 30), _viewScope);
        automation.MouseMoveBy(new Vector2(1, 1), _viewScope);

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
        mouseService.SubscribeWindowEnter(_viewScope, 0, _ => events.Add(("enter", mouseService.IsInWindow(_viewScope))));
        mouseService.SubscribeWindowLeave(_viewScope, 0, eventArgs => events.Add(("leave", mouseService.IsInWindow(_viewScope))));
        mouseService.SubscribeButtonPress(_viewScope, 0, _ => events.Add(("press", mouseService.IsInWindow(_viewScope))));

        automation.MouseLeave(_viewScope);
        automation.MouseDown(MouseButton.Left, new Vector2(20, 30), _viewScope);
        automation.MouseLeave(_viewScope);
        automation.MouseLeave(_viewScope);
        automation.MouseMoveBy(Vector2.One, _viewScope);

        Assert.That(events, Is.EqualTo(new[] { ("enter", true), ("press", true), ("leave", false), ("enter", true) }));
    }

    [Test]
    public void MouseLeave_ForUnregisteredView_Throws()
    {
        (InputAutomation automation, _, _, _) = CreateAutomation();

        Assert.Throws<InvalidOperationException>(() => automation.MouseLeave(new ViewScope(99)));
    }

    [Test]
    public void KeyPress_DispatchesDownAndUpWithCorrespondingState()
    {
        (InputAutomation automation, _, KeyboardService keyboardService, _) = CreateAutomation();
        List<(string Event, bool IsPressed)> events = new();
        keyboardService.SubscribeKeyDown(_viewScope, 0, eventArgs => events.Add(("down", eventArgs.Keyboard.IsPressed(Scancode.A))));
        keyboardService.SubscribeKeyUp(_viewScope, 0, eventArgs => events.Add(("up", eventArgs.Keyboard.IsPressed(Scancode.A))));

        automation.KeyPress(Scancode.A, _viewScope);

        Assert.That(events, Is.EqualTo(new[] { ("down", true), ("up", false) }));
    }

    [Test]
    public void TextInput_DispatchesTextUnchanged()
    {
        (InputAutomation automation, _, _, TextInputService textInputService) = CreateAutomation();
        string? receivedText = null;
        textInputService.SubscribeTextInput(_viewScope, 0, eventArgs => receivedText = eventArgs.Text);

        automation.TextInput("Hello 👋", _viewScope);

        Assert.That(receivedText, Is.EqualTo("Hello 👋"));
    }

    [Test]
    public void EachView_HasItsOwnMouseAndKeyboard()
    {
        ViewScope otherViewScope = new(8);
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(CreateWindow(_viewScope, 42));
        windowRegistry.Register(CreateWindow(otherViewScope, 43));
        (InputAutomation automation, MouseService mouseService, KeyboardService keyboardService, _) = CreateAutomation(windowRegistry);
        List<(Vector2 Position, Vector2 RelativeMotion)> otherMotions = new();
        mouseService.SubscribeMotion(otherViewScope, 0, eventArgs => otherMotions.Add((eventArgs.Position, eventArgs.RelativeMotion)));
        bool? ctrlPressedInOtherView = null;
        keyboardService.SubscribeKeyDown(otherViewScope, 0, eventArgs => ctrlPressedInOtherView = eventArgs.Keyboard.IsPressed(Scancode.LeftCtrl));

        automation.MouseMoveTo(new Vector2(100, 100), _viewScope);
        automation.KeyDown(Scancode.LeftCtrl, _viewScope);
        automation.MouseMoveBy(new Vector2(1, 1), otherViewScope);
        automation.KeyDown(Scancode.A, otherViewScope);

        Assert.Multiple(() =>
        {
            Assert.That(otherMotions, Is.EqualTo(new[] { (new Vector2(1, 1), new Vector2(1, 1)) }));
            Assert.That(ctrlPressedInOtherView, Is.False);
        });
    }

    [Test]
    public void InputForUnregisteredView_Throws()
    {
        (InputAutomation automation, _, _, _) = CreateAutomation();

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => automation.MouseMoveTo(Vector2.Zero, new ViewScope(99)));

        Assert.That(exception!.Message, Does.Contain("ViewScope 99"));
    }

    [Test]
    public void Input_IsTimestampedWithGameTime()
    {
        TestFrameContext frameContext = new();
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(CreateWindow(_viewScope, 42));
        (InputAutomation automation, _, KeyboardService keyboardService, _) = CreateAutomation(windowRegistry, frameContext: frameContext);
        ulong? timestamp = null;
        keyboardService.SubscribeKeyDown(_viewScope, 0, eventArgs => timestamp = eventArgs.Timestamp);
        frameContext.StartTestFrame(1500);

        automation.KeyDown(Scancode.A, _viewScope);

        Assert.That(timestamp, Is.EqualTo(1_500_000_000UL));
    }

    [Test]
    public void GamepadConnect_AddsGamepadAndRaisesConnected()
    {
        GamepadService gamepadService = new();
        (InputAutomation automation, _, _, _) = CreateAutomation(new WindowRegistry(), gamepadService);
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
        (InputAutomation automation, _, _, _) = CreateAutomation(new WindowRegistry());

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
        (InputAutomation automation, _, _, _) = CreateAutomation(new WindowRegistry(), gamepadService);
        List<(string Event, GamepadButton Button, int ButtonFlags)> events = new();
        gamepadService.SubscribeButtonPress(0, eventArgs => events.Add(("press", eventArgs.Button, eventArgs.Gamepad.ButtonFlags)));
        gamepadService.SubscribeButtonRelease(0, eventArgs => events.Add(("release", eventArgs.Button, eventArgs.Gamepad.ButtonFlags)));
        automation.GamepadConnect();

        automation.GamepadButtonPress(GamepadButton.South);

        Assert.That(events, Is.EqualTo(new[] { ("press", GamepadButton.South, 1 << (int)GamepadButton.South), ("release", GamepadButton.South, 0) }));
    }

    [Test]
    public void GamepadAxisMotion_AppliesStickDeadZoneButNotToTriggers()
    {
        GamepadService gamepadService = new();
        (InputAutomation automation, _, _, _) = CreateAutomation(new WindowRegistry(), gamepadService);
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
            Assert.That(sticks, Is.EqualTo(new[] { new Vector2(0.5f, 0) }));
            Assert.That(triggers, Is.EqualTo(new[] { 0.1f }));
        });
    }

    [Test]
    public void GamepadDisconnect_RecentersAxesAndReleasesButtonsBeforeDisconnecting()
    {
        GamepadService gamepadService = new();
        (InputAutomation automation, _, _, _) = CreateAutomation(new WindowRegistry(), gamepadService);
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
            Assert.That(events, Is.EqualTo(new[] { $"right stick {Vector2.Zero}", "right trigger 0", "release North", "disconnected" }));
            Assert.That(gamepadService.Gamepads, Is.Empty);
            Assert.That(automation.IsGamepadConnected, Is.False);
        });
    }

    private static (InputAutomation Automation, MouseService MouseService, KeyboardService KeyboardService, TextInputService TextInputService) CreateAutomation()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(CreateWindow(_viewScope, 42));
        return CreateAutomation(windowRegistry);
    }

    internal static (InputAutomation Automation, MouseService MouseService, KeyboardService KeyboardService, TextInputService TextInputService) CreateAutomation(
        WindowRegistry windowRegistry, GamepadService? gamepadService = null, FrameContext? frameContext = null)
    {
        MouseService mouseService = new(windowRegistry);
        KeyboardService keyboardService = new(new AppControl());
        TextInputService textInputService = new(windowRegistry);
        InputAutomation automation = new(windowRegistry, mouseService, keyboardService, textInputService, gamepadService ?? new GamepadService(), frameContext ?? new TestFrameContext());
        return (automation, mouseService, keyboardService, textInputService);
    }

    internal static Window CreateWindow(ViewScope viewScope, uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(SwapchainWindow));
        SetBackingField(window, nameof(Window.ViewScope), viewScope);
        SetBackingField(window, nameof(Window.SdlId), sdlId);
        return window;
    }

    private static void SetBackingField<T>(Window window, string propertyName, T value)
    {
        FieldInfo field = typeof(Window).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, value);
    }
}
