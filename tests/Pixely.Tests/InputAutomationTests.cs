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
    public void InputForUnregisteredView_Throws()
    {
        (InputAutomation automation, _, _, _) = CreateAutomation();

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => automation.MouseMoveTo(Vector2.Zero, new ViewScope(99)));

        Assert.That(exception!.Message, Does.Contain("ViewScope 99"));
    }

    private static (InputAutomation Automation, MouseService MouseService, KeyboardService KeyboardService, TextInputService TextInputService) CreateAutomation()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(CreateWindow(_viewScope, 42));
        MouseService mouseService = new(windowRegistry);
        KeyboardService keyboardService = new(new AppControl());
        TextInputService textInputService = new(windowRegistry);
        InputAutomation automation = new(windowRegistry, mouseService, keyboardService, textInputService);
        return (automation, mouseService, keyboardService, textInputService);
    }

    private static Window CreateWindow(ViewScope viewScope, uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
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
