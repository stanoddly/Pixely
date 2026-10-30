using System.Numerics;
using Pixely.Input;

namespace Pixely.Testing.Tests;

public class StubGamepadServiceTests
{
    [Test]
    public void Connect_AddsAGamepadWithItsOwnDeviceId()
    {
        StubGamepadService gamepads = new();
        List<Gamepad> connected = [];
        gamepads.GamepadConnected += connected.Add;

        Gamepad first = gamepads.Connect();
        Gamepad second = gamepads.Connect();

        Assert.Multiple(() =>
        {
            Assert.That(connected, Is.EqualTo(new[] { first, second }));
            Assert.That(gamepads.Gamepads, Is.EquivalentTo(new[] { first, second }));
            Assert.That(first.DeviceId, Is.Not.EqualTo(second.DeviceId));
        });
    }

    [Test]
    public void Press_RaisesButtonPressThenButtonReleaseForThatGamepad()
    {
        StubGamepadService gamepads = new();
        Gamepad first = gamepads.Connect();
        Gamepad second = gamepads.Connect();
        List<string> events = [];
        gamepads.ButtonPress += eventArgs => events.Add($"press {eventArgs.Button} {eventArgs.Gamepad.DeviceId} held {eventArgs.Gamepad.IsPressed(eventArgs.Button)}");
        gamepads.ButtonRelease += eventArgs => events.Add($"release {eventArgs.Button} {eventArgs.Gamepad.DeviceId} held {eventArgs.Gamepad.IsPressed(eventArgs.Button)}");

        gamepads.Press(second, GamepadButton.Start);

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.EqualTo(new[] { $"press Start {second.DeviceId} held True", $"release Start {second.DeviceId} held False" }));
            Assert.That(first.IsPressed(GamepadButton.Start), Is.False);
        });
    }

    [Test]
    public void Down_OnAHeldButton_RaisesNothing()
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();
        int presses = 0;
        gamepads.ButtonPress += _ => presses++;

        gamepads.Down(gamepad, GamepadButton.South);
        gamepads.Down(gamepad, GamepadButton.South);

        Assert.That((presses, gamepad.IsPressed(GamepadButton.South)), Is.EqualTo((1, true)));
    }

    [Test]
    public void MoveLeftStick_RaisesOneEventWithTheDeadZoneApplied()
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();
        List<Vector2> motions = [];
        gamepads.LeftStickMotion += eventArgs => motions.Add(eventArgs.Value);

        gamepads.MoveLeftStick(gamepad, new Vector2(0.6f, 0.8f));
        gamepads.MoveLeftStick(gamepad, new Vector2(0.1f, 0f));

        Assert.Multiple(() =>
        {
            Assert.That(motions, Has.Count.EqualTo(2));
            Assert.That(motions[0].X, Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(motions[0].Y, Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(motions[1], Is.EqualTo(Vector2.Zero));
            Assert.That(gamepad.LeftStick, Is.EqualTo(Vector2.Zero));
        });
    }

    [Test]
    public void MoveRightTrigger_RaisesTheValue()
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();
        List<float> motions = [];
        gamepads.RightTriggerMotion += eventArgs => motions.Add(eventArgs.Value);

        gamepads.MoveRightTrigger(gamepad, 0.5f);

        Assert.That((motions.Single(), gamepad.RightTrigger), Is.EqualTo((0.5f, 0.5f)));
    }

    [Test]
    public void Disconnect_ReleasesButtonsAndRestsSticksBeforeTheGamepadGoes()
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();
        gamepads.Down(gamepad, GamepadButton.West);
        gamepads.MoveLeftStick(gamepad, new Vector2(1f, 0f));
        gamepads.MoveLeftTrigger(gamepad, 1f);
        List<string> events = [];
        gamepads.ButtonRelease += eventArgs => events.Add($"release {eventArgs.Button}");
        gamepads.LeftStickMotion += eventArgs => events.Add($"stick {eventArgs.Value}");
        gamepads.LeftTriggerMotion += eventArgs => events.Add($"trigger {eventArgs.Value}");
        gamepads.GamepadDisconnected += _ => events.Add($"disconnected, {gamepads.Gamepads.Count} left");

        gamepads.Disconnect(gamepad);

        Assert.That(events, Is.EqualTo(new[] { "release West", $"stick {Vector2.Zero}", "trigger 0", "disconnected, 0 left" }));
    }

    [Test]
    public void Press_OnADisconnectedGamepad_Throws()
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();
        gamepads.Disconnect(gamepad);

        Assert.Throws<InvalidOperationException>(() => gamepads.Press(gamepad, GamepadButton.South));
    }

    [TestCase(1.5f, 0f)]
    [TestCase(0f, -1.5f)]
    [TestCase(float.NaN, 0f)]
    [TestCase(0f, float.NaN)]
    public void MoveLeftStick_OutOfRange_Throws(float x, float y)
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();

        Assert.Throws<ArgumentOutOfRangeException>(() => gamepads.MoveLeftStick(gamepad, new Vector2(x, y)));
    }

    [TestCase(-0.1f)]
    [TestCase(1.1f)]
    [TestCase(float.NaN)]
    public void MoveLeftTrigger_OutOfRange_Throws(float value)
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();

        Assert.Throws<ArgumentOutOfRangeException>(() => gamepads.MoveLeftTrigger(gamepad, value));
    }

    [TestCase(GamepadButton.Invalid)]
    [TestCase(GamepadButton.Count)]
    [TestCase((GamepadButton)36)]
    public void Down_WithAValueThatIsNotAButton_Throws(GamepadButton button)
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();

        Assert.Throws<ArgumentOutOfRangeException>(() => gamepads.Down(gamepad, button));
    }

    [Test]
    public void Disconnect_FromAButtonReleaseHandler_ThrowsAndLeavesTheStubUsable()
    {
        StubGamepadService gamepads = new();
        Gamepad gamepad = gamepads.Connect();
        gamepads.Down(gamepad, GamepadButton.South);
        gamepads.ButtonRelease += eventArgs =>
        {
            if (eventArgs.Button == GamepadButton.South)
            {
                gamepads.Disconnect(gamepad);
            }
        };

        Assert.Throws<InvalidOperationException>(() => gamepads.Up(gamepad, GamepadButton.South));
        gamepads.Press(gamepad, GamepadButton.East);

        Assert.That(gamepads.Gamepads, Is.EqualTo(new[] { gamepad }));
    }
}
