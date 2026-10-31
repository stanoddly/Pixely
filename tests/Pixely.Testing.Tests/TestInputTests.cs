using System.Numerics;
using Pixely.App;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Testing.Tests;

// Builds real apps, which start SDL.
[NonParallelizable]
public class TestInputTests
{
    [Test]
    public void KeyPress_ReachesTheGameAtTheNextFrame()
    {
        using IPixelyApp app = BuildWithWindow();
        TestInput input = app.GetRequiredService<TestInput>();
        List<Scancode> pressed = new();
        app.GetRequiredService<IKeyboardService>().KeyDown += eventArgs => pressed.Add(eventArgs.Scancode);

        input.KeyPress(Scancode.Space);
        int beforeFrame = pressed.Count;
        app.RunFrame();

        Assert.Multiple(() =>
        {
            Assert.That(beforeFrame, Is.Zero);
            Assert.That(pressed, Is.EqualTo(new[] { Scancode.Space }));
        });
    }

    [Test]
    public void KeyPress_BeforeAStageChange_ReachesTheNewStage()
    {
        using IPixelyApp app = BuildWithWindow();
        TestInput input = app.GetRequiredService<TestInput>();
        List<Scancode> pressed = new();
        app.GetRequiredService<IStageManager>().Load(stage => stage.AddSingleton<KeyRecorder>(provider =>
            new KeyRecorder(provider.GetRequiredService<IKeyboardService>(), pressed)));

        input.KeyPress(Scancode.Return);
        app.RunFrame();

        Assert.That(pressed, Is.EqualTo(new[] { Scancode.Return }));
    }

    [Test]
    public void MouseClick_PressesAndReleasesAtThePosition()
    {
        using IPixelyApp app = BuildWithWindow();
        TestInput input = app.GetRequiredService<TestInput>();
        IMouseService mouse = app.GetRequiredService<IMouseService>();
        List<(MouseButton, Vector2)> presses = new();
        int releases = 0;
        mouse.ButtonPress += eventArgs => presses.Add((eventArgs.Button, eventArgs.Position));
        mouse.ButtonRelease += _ => releases++;

        input.MouseClick(MouseButton.Left, new Vector2(40, 20));
        app.RunFrame();

        Assert.Multiple(() =>
        {
            Assert.That(presses, Is.EqualTo(new[] { (MouseButton.Left, new Vector2(40, 20)) }));
            Assert.That(releases, Is.EqualTo(1));
        });
    }

    [Test]
    public void GamepadSticksAndTriggers_ReachTheGameAsTheGamepadApiReportsThem()
    {
        using IPixelyApp app = BuildWithWindow();
        TestInput input = app.GetRequiredService<TestInput>();
        IGamepadService gamepads = app.GetRequiredService<IGamepadService>();
        Vector2 leftStick = default;
        float rightTrigger = 0;
        gamepads.LeftStickMotion += eventArgs => leftStick = eventArgs.Value;
        gamepads.RightTriggerMotion += eventArgs => rightTrigger = eventArgs.Value;

        input.GamepadConnect();
        input.GamepadLeftStick(new Vector2(1, 0));
        input.GamepadRightTrigger(1);
        app.RunFrame();

        Assert.Multiple(() =>
        {
            Assert.That(gamepads.Gamepads, Has.Count.EqualTo(1));
            Assert.That(leftStick, Is.EqualTo(new Vector2(1, 0)));
            Assert.That(rightTrigger, Is.EqualTo(1));
        });
    }

    [Test]
    public void GamepadButtonPress_WithoutAConnectedGamepad_ThrowsAtTheFrame()
    {
        using IPixelyApp app = BuildWithWindow();
        TestInput input = app.GetRequiredService<TestInput>();

        input.GamepadButtonPress(GamepadButton.South);

        Assert.Throws<InvalidOperationException>(() => app.RunFrame());
    }

    [Test]
    public void Calls_WithValuesOutOfRange_ThrowAtTheCall()
    {
        using IPixelyApp app = BuildWithWindow();
        TestInput input = app.GetRequiredService<TestInput>();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => input.KeyPress(Scancode.Count));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.KeyDown((Scancode)1));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.MouseClick((MouseButton)0, Vector2.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.GamepadButtonPress(GamepadButton.Count));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.GamepadLeftStick(new Vector2(float.NaN, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => input.GamepadLeftTrigger(-0.5f));
        });
    }

    private static IPixelyApp BuildWithWindow()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering();
        return builder.Build();
    }

    private sealed class KeyRecorder
    {
        public KeyRecorder(IKeyboardService keyboard, List<Scancode> pressed)
        {
            keyboard.KeyDown += eventArgs => pressed.Add(eventArgs.Scancode);
        }
    }
}
