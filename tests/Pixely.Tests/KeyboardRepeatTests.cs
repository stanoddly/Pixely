using Pixely.Input;
using SDL;

namespace Pixely.Tests;

public sealed class KeyboardRepeatTests
{
    private static readonly ViewScope _view = new(1);

    [Test]
    public void KeyDown_FirstPress_IsNotMarkedAsRepeat()
    {
        KeyboardService keyboardService = new(new AppControl());
        List<bool> repeats = new();
        keyboardService.SubscribeKeyDown(_view, 0, eventArgs => repeats.Add(eventArgs.Repeat));

        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));

        Assert.That(repeats, Is.EqualTo(new[] { false }));
    }

    [Test]
    public void KeyDown_WhileKeyIsHeld_IsDeliveredAndMarkedAsRepeat()
    {
        KeyboardService keyboardService = new(new AppControl());
        List<bool> repeats = new();
        keyboardService.SubscribeKeyDown(_view, 0, eventArgs => repeats.Add(eventArgs.Repeat));

        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));

        Assert.That(repeats, Is.EqualTo(new[] { false, true, true }));
    }

    [Test]
    public void KeyDown_AfterKeyUp_IsNotMarkedAsRepeatAgain()
    {
        KeyboardService keyboardService = new(new AppControl());
        List<bool> repeats = new();
        keyboardService.SubscribeKeyDown(_view, 0, eventArgs => repeats.Add(eventArgs.Repeat));

        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyUp(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));

        Assert.That(repeats, Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void KeyUp_AfterRepeatedKeyDown_IsNotMarkedAsRepeat()
    {
        KeyboardService keyboardService = new(new AppControl());
        bool? keyUpRepeat = null;
        keyboardService.SubscribeKeyUp(_view, 0, eventArgs => keyUpRepeat = eventArgs.Repeat);

        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyUp(Scancode.A));

        Assert.That(keyUpRepeat, Is.False);
    }

    [Test]
    public void KeyDown_RepeatAgreesWithTrackedKeyState()
    {
        KeyboardService keyboardService = new(new AppControl());
        List<bool> pressedWhenRepeated = new();
        keyboardService.SubscribeKeyDown(_view, 0, eventArgs =>
        {
            if (eventArgs.Repeat)
            {
                pressedWhenRepeated.Add(eventArgs.Keyboard.IsPressed(Scancode.A));
            }
        });

        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));
        keyboardService.OnKeyEvent(_view, KeyDown(Scancode.A));

        Assert.That(pressedWhenRepeated, Is.EqualTo(new[] { true }));
    }

    private static SDL_KeyboardEvent KeyDown(Scancode scancode)
    {
        return new SDL_KeyboardEvent { down = true, scancode = (SDL_Scancode)scancode };
    }

    private static SDL_KeyboardEvent KeyUp(Scancode scancode)
    {
        return new SDL_KeyboardEvent { down = false, scancode = (SDL_Scancode)scancode };
    }
}
