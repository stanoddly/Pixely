using Pixely.Input;

namespace Pixely.Testing.Tests;

public class StubKeyboardServiceTests
{
    [Test]
    public void Down_RaisesKeyDownWithTheKeyHeld()
    {
        StubKeyboardService keyboard = new();
        List<(Scancode Scancode, VirtualKey Key, bool Repeat, bool Held)> downs = [];
        keyboard.KeyDown += eventArgs => downs.Add((eventArgs.Scancode, eventArgs.Key, eventArgs.Repeat, eventArgs.Keyboard.IsPressed(eventArgs.Scancode)));

        keyboard.Down(Scancode.G);

        Assert.That(downs, Is.EqualTo(new[] { (Scancode.G, VirtualKey.G, false, true) }));
    }

    [Test]
    public void Down_OnAHeldKey_IsARepeat()
    {
        StubKeyboardService keyboard = new();
        List<bool> repeats = [];
        keyboard.KeyDown += eventArgs => repeats.Add(eventArgs.Repeat);

        keyboard.Down(Scancode.G);
        keyboard.Down(Scancode.G);

        Assert.That(repeats, Is.EqualTo(new[] { false, true }));
    }

    [Test]
    public void Press_RaisesKeyDownThenKeyUpAndLeavesTheKeyReleased()
    {
        StubKeyboardService keyboard = new();
        List<string> events = [];
        keyboard.KeyDown += eventArgs => events.Add($"down {eventArgs.Scancode}");
        keyboard.KeyUp += eventArgs => events.Add($"up {eventArgs.Scancode} held {eventArgs.Keyboard.IsPressed(eventArgs.Scancode)}");

        keyboard.Press(Scancode.Number1);

        Assert.That(events, Is.EqualTo(new[] { "down Number1", "up Number1 held False" }));
    }

    [Test]
    public void Press_WithCtrlDown_SeesCtrlHeld()
    {
        StubKeyboardService keyboard = new();
        List<bool> ctrl = [];
        keyboard.KeyDown += eventArgs =>
        {
            if (eventArgs.Scancode == Scancode.G)
            {
                ctrl.Add(eventArgs.Keyboard.Ctrl);
            }
        };

        keyboard.Down(Scancode.LeftCtrl);
        keyboard.Press(Scancode.G);
        keyboard.Up(Scancode.LeftCtrl);
        keyboard.Press(Scancode.G);

        Assert.That(ctrl, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public void Down_RunsHandlersInOrderAndStopsAtTheOneThatConsumes()
    {
        StubKeyboardService keyboard = new();
        List<int> called = [];
        keyboard.SubscribeKeyDown(2, _ => called.Add(2));
        keyboard.SubscribeKeyDown(1, eventArgs =>
        {
            called.Add(1);
            eventArgs.Consume();
        });
        keyboard.SubscribeKeyDown(0, _ => called.Add(0));

        keyboard.Down(Scancode.A);

        Assert.That(called, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Down_InAView_ReachesOnlyThatViewsHandlers()
    {
        StubKeyboardService keyboard = new();
        List<string> called = [];
        keyboard.KeyDown += _ => called.Add("main");
        keyboard.SubscribeKeyDown(new ViewScope(1), 0, _ => called.Add("second"));

        keyboard.Down(Scancode.A, new ViewScope(1));

        Assert.That(called, Is.EqualTo(new[] { "second" }));
    }

    [TestCase(Scancode.Count)]
    [TestCase((Scancode)600)]
    [TestCase((Scancode)1)]
    public void Down_WithAValueOutsideTheEnum_Throws(Scancode scancode)
    {
        StubKeyboardService keyboard = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => keyboard.Down(scancode));
    }

    [Test]
    public void Down_FromAKeyDownHandler_ThrowsAndLeavesTheStubUsable()
    {
        StubKeyboardService keyboard = new();
        List<Scancode> downs = [];
        keyboard.KeyDown += eventArgs =>
        {
            downs.Add(eventArgs.Scancode);
            if (eventArgs.Scancode == Scancode.A)
            {
                keyboard.Down(Scancode.B);
            }
        };

        Assert.Throws<InvalidOperationException>(() => keyboard.Down(Scancode.A));
        keyboard.Down(Scancode.C);

        Assert.That(downs, Is.EqualTo(new[] { Scancode.A, Scancode.C }));
    }
}
