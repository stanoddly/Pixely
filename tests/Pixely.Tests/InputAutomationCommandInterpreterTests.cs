using Pixely.Content;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationCommandInterpreterTests
{
    [TestCase("mouse move 320 180", "motion <320, 180>")]
    [TestCase("mouse moveby 10 -5.5", "motion <10, -5.5>")]
    [TestCase("mouse down left 1 2", "press Left <1, 2>")]
    // A release is only dispatched for a pressed button, so the command under test comes after its press.
    [TestCase("mouse down Right 1 2|mouse up Right 1 2", "press Right <1, 2>", "release Right <1, 2>")]
    [TestCase("  mouse  click  Left  330 175", "motion <330, 175>", "press Left <330, 175>", "release Left <330, 175>")]
    [TestCase("mouse wheel 0 -1 330 175", "wheel <0, -1> <330, 175>")]
    // A leave is only dispatched for a mouse in the window, so the command under test comes after a mouse command.
    [TestCase("mouse move 1 2|mouse leave", "motion <1, 2>", "leave")]
    [TestCase("@7 mouse move 1 2|mouse leave", "7: motion <1, 2>")]
    [TestCase("key down LeftCtrl", "down LeftCtrl")]
    [TestCase("key up e", "up E")]
    [TestCase("key press Return", "down Return", "up Return")]
    [TestCase("text hello  world ", "text 'hello  world'")]
    [TestCase("text\n hi\tthere\n", "text 'hi\tthere'")]
    [TestCase("key\r\ndown\tA", "down A")]
    [TestCase("@7\nkey press A", "7: down A", "7: up A")]
    [TestCase("text", "text ''")]
    [TestCase("@0 key press Return", "down Return", "up Return")]
    [TestCase("@7 key press Return", "7: down Return", "7: up Return")]
    [TestCase("@7 mouse move 1 2", "7: motion <1, 2>")]
    [TestCase("@7 text hi", "7: text 'hi'")]
    [TestCase("gamepad connect|gamepad press east", "gamepad connected", "gamepad press East", "gamepad release East")]
    [TestCase("gamepad connect|gamepad down South|gamepad up South", "gamepad connected", "gamepad press South", "gamepad release South")]
    [TestCase("gamepad connect|gamepad stick left 1 -0.5", "gamepad connected", "gamepad left stick <1, 0>", "gamepad left stick <1, -0.5>")]
    [TestCase("gamepad connect|gamepad stick right 0 1", "gamepad connected", "gamepad right stick <0, 1>")]
    [TestCase("gamepad connect|gamepad trigger left 0.25", "gamepad connected", "gamepad left trigger 0.25")]
    [TestCase("gamepad connect|gamepad trigger right 1", "gamepad connected", "gamepad right trigger 1")]
    [TestCase("gamepad connect|gamepad down West|gamepad disconnect", "gamepad connected", "gamepad press West", "gamepad release West", "gamepad disconnected")]
    public void Execute_DispatchesCommand(string commands, params string[] expectedEvents)
    {
        Fixture fixture = CreateInterpreter();

        foreach (string command in commands.Split('|'))
        {
            fixture.Interpreter.Execute(command);
        }

        Assert.That(fixture.Events, Is.EqualTo(expectedEvents));
    }

    [TestCase("")]
    [TestCase(" \n\t ")]
    public void Execute_IgnoresBlankCommand(string command)
    {
        Fixture fixture = CreateInterpreter();

        int heldFrames = fixture.Interpreter.Execute(command);

        Assert.Multiple(() =>
        {
            Assert.That(heldFrames, Is.Zero);
            Assert.That(fixture.Events, Is.Empty);
        });
    }

    [TestCase("wait 0", 0)]
    [TestCase("wait 1", 1)]
    [TestCase(" wait\n60 ", 60)]
    public void Execute_Wait_ReturnsFramesToHold(string command, int expectedFrames)
    {
        Fixture fixture = CreateInterpreter();

        Assert.That(fixture.Interpreter.Execute(command), Is.EqualTo(expectedFrames));
    }

    [TestCase("speed 4", 4.0)]
    [TestCase("speed 0", 0.0)]
    [TestCase("speed 0.01", 0.01)]
    public void Execute_Speed_SetsClockSpeed(string command, double expectedSpeed)
    {
        Fixture fixture = CreateInterpreter();

        fixture.Interpreter.Execute(command);

        Assert.That(fixture.HeadlessClock.Speed, Is.EqualTo(expectedSpeed).Within(1e-6));
    }

    [Test]
    public void Execute_Quit_RequestsQuit()
    {
        Fixture fixture = CreateInterpreter();

        fixture.Interpreter.Execute("quit");

        Assert.That(fixture.AppControl.QuitRequested, Is.True);
    }

    [TestCase("jump", "unknown command 'jump'")]
    [TestCase("mouse move 320", "unknown command 'mouse move 320'")]
    [TestCase("mouse leave 1 2", "unknown command 'mouse leave 1 2'")]
    [TestCase("mouse move x 180", "invalid number 'x'")]
    [TestCase("mouse click Center 1 2", "unknown MouseButton 'Center'")]
    [TestCase("mouse click 9 1 2", "unknown MouseButton '9'")]
    [TestCase("key press Ctrl", "unknown Scancode 'Ctrl'")]
    [TestCase("screenshot", "unknown command 'screenshot'")]
    [TestCase("@x key press A", "invalid view scope 'x'")]
    [TestCase("@7", "unknown command ''")]
    [TestCase("@9 key press A", "no window for view scope 9")]
    [TestCase("@9 screenshot frame.png", "no window for view scope 9")]
    [TestCase("# a comment", "unknown command '# a comment'")]
    [TestCase("mouse move NaN 1", "invalid number 'NaN'")]
    [TestCase("mouse move 1 Infinity", "invalid number 'Infinity'")]
    [TestCase("wait", "unknown command 'wait'")]
    [TestCase("wait -1", "invalid frame count '-1'")]
    [TestCase("wait 0.5", "invalid frame count '0.5'")]
    [TestCase("wait 1 frames", "unknown command 'wait 1 frames'")]
    [TestCase("@7 wait 1", "'wait 1' takes no view scope")]
    [TestCase("speed -1", "invalid speed '-1', expected 0 or at least 0.01")]
    [TestCase("speed 0.001", "invalid speed '0.001', expected 0 or at least 0.01")]
    [TestCase("speed NaN", "invalid number 'NaN'")]
    [TestCase("@7 speed 2", "'speed 2' takes no view scope")]
    [TestCase("quit now", "unknown command 'quit now'")]
    [TestCase("@0 quit", "'quit' takes no view scope")]
    [TestCase("gamepad press South", "gamepad is not connected")]
    [TestCase("gamepad disconnect", "gamepad is not connected")]
    [TestCase("gamepad connect|gamepad connect", "gamepad is already connected")]
    [TestCase("gamepad connect|gamepad press Invalid", "unknown GamepadButton 'Invalid'")]
    [TestCase("gamepad connect|gamepad press Count", "unknown GamepadButton 'Count'")]
    [TestCase("gamepad connect|gamepad press Jump", "unknown GamepadButton 'Jump'")]
    [TestCase("gamepad connect|gamepad stick middle 0 0", "unknown command 'gamepad stick middle 0 0'")]
    [TestCase("gamepad connect|gamepad stick left 1.5 0", "invalid axis value '1.5', expected -1 to 1")]
    [TestCase("gamepad connect|gamepad trigger left -0.1", "invalid axis value '-0.1', expected 0 to 1")]
    [TestCase("gamepad bogus", "unknown command 'gamepad bogus'")]
    [TestCase("@7 gamepad connect", "'gamepad connect' takes no view scope")]
    public void Execute_RejectsMalformedCommandWithoutDispatching(string commands, string expectedMessage)
    {
        Fixture fixture = CreateInterpreter();
        string[] allCommands = commands.Split('|');
        foreach (string command in allCommands[..^1])
        {
            fixture.Interpreter.Execute(command);
        }

        int eventCount = fixture.Events.Count;

        FormatException? exception = Assert.Throws<FormatException>(() => fixture.Interpreter.Execute(allCommands[^1]));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(expectedMessage));
            Assert.That(fixture.Events, Has.Count.EqualTo(eventCount));
        });
    }

    [Test]
    public void Execute_PropagatesHandlerFailure()
    {
        Fixture fixture = CreateInterpreter();
        fixture.KeyboardService.SubscribeKeyDown(0, _ => throw new InvalidOperationException("game broke"));

        Assert.Throws<InvalidOperationException>(() => fixture.Interpreter.Execute("key press A"));
    }

    [Test]
    public void Execute_WithoutDefaultWindow_FailsOnlyForWindowCommands()
    {
        WindowRegistry windowRegistry = new();
        InputAutomation automation = InputAutomationTests.CreateAutomation(windowRegistry).Automation;
        InputAutomationCommandInterpreter interpreter = new(automation, windowRegistry, new NoImageWriter(), new HeadlessClock(), new AppControl());

        Assert.Multiple(() =>
        {
            Assert.That(() => interpreter.Execute("key press A"), Throws.TypeOf<FormatException>().With.Message.EqualTo("no window for view scope 0"));
            Assert.That(() => interpreter.Execute("screenshot frame.png"), Throws.TypeOf<FormatException>().With.Message.EqualTo("no window for view scope 0"));
            Assert.That(() => interpreter.Execute("wait 1"), Throws.Nothing);
            Assert.That(() => interpreter.Execute("speed 2"), Throws.Nothing);
            Assert.That(() => interpreter.Execute("gamepad connect"), Throws.Nothing);
            Assert.That(() => interpreter.Execute("quit"), Throws.Nothing);
        });
    }

    private static Fixture CreateInterpreter()
    {
        ViewScope secondary = new(7);
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        windowRegistry.Register(InputAutomationTests.CreateWindow(secondary, 43));
        GamepadService gamepadService = new();
        (InputAutomation automation, MouseService mouseService, KeyboardService keyboardService, TextInputService textInputService) = InputAutomationTests.CreateAutomation(windowRegistry, gamepadService);

        List<string> events = new();
        mouseService.SubscribeMotion(0, eventArgs => events.Add($"motion {eventArgs.Position}"));
        mouseService.SubscribeButtonPress(0, eventArgs => events.Add($"press {eventArgs.Button} {eventArgs.Position}"));
        mouseService.SubscribeButtonRelease(0, eventArgs => events.Add($"release {eventArgs.Button} {eventArgs.Position}"));
        mouseService.SubscribeWheel(0, eventArgs => events.Add($"wheel {eventArgs.Delta} {eventArgs.Position}"));
        mouseService.SubscribeWindowLeave(0, _ => events.Add("leave"));
        keyboardService.SubscribeKeyDown(0, eventArgs => events.Add($"down {eventArgs.Scancode}"));
        keyboardService.SubscribeKeyUp(0, eventArgs => events.Add($"up {eventArgs.Scancode}"));
        textInputService.SubscribeTextInput(0, eventArgs => events.Add($"text '{eventArgs.Text}'"));
        mouseService.SubscribeMotion(secondary, 0, eventArgs => events.Add($"7: motion {eventArgs.Position}"));
        keyboardService.SubscribeKeyDown(secondary, 0, eventArgs => events.Add($"7: down {eventArgs.Scancode}"));
        keyboardService.SubscribeKeyUp(secondary, 0, eventArgs => events.Add($"7: up {eventArgs.Scancode}"));
        textInputService.SubscribeTextInput(secondary, 0, eventArgs => events.Add($"7: text '{eventArgs.Text}'"));
        gamepadService.GamepadConnected += _ => events.Add("gamepad connected");
        gamepadService.GamepadDisconnected += _ => events.Add("gamepad disconnected");
        gamepadService.SubscribeButtonPress(0, eventArgs => events.Add($"gamepad press {eventArgs.Button}"));
        gamepadService.SubscribeButtonRelease(0, eventArgs => events.Add($"gamepad release {eventArgs.Button}"));
        gamepadService.SubscribeLeftStickMotion(0, eventArgs => events.Add($"gamepad left stick {eventArgs.Value}"));
        gamepadService.SubscribeRightStickMotion(0, eventArgs => events.Add($"gamepad right stick {eventArgs.Value}"));
        gamepadService.SubscribeLeftTriggerMotion(0, eventArgs => events.Add($"gamepad left trigger {eventArgs.Value}"));
        gamepadService.SubscribeRightTriggerMotion(0, eventArgs => events.Add($"gamepad right trigger {eventArgs.Value}"));

        HeadlessClock headlessClock = new();
        AppControl appControl = new();
        InputAutomationCommandInterpreter interpreter = new(automation, windowRegistry, new NoImageWriter(), headlessClock, appControl);
        return new Fixture(interpreter, events, keyboardService, headlessClock, appControl);
    }

    private sealed record Fixture(
        InputAutomationCommandInterpreter Interpreter,
        List<string> Events,
        KeyboardService KeyboardService,
        HeadlessClock HeadlessClock,
        AppControl AppControl);

    private sealed class NoImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new NotSupportedException();
    }
}
