using Pixely.Content;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationCommandInterpreterTests
{
    [TestCase("mouse move 320 180", "motion <320, 180>")]
    [TestCase("mouse moveby 10 -5.5", "motion <10, -5.5>")]
    [TestCase("mouse down left 1 2", "press Left <1, 2>")]
    // A release is only dispatched for a pressed button, so the line under test comes after its press.
    [TestCase("mouse down Right 1 2\nmouse up Right 1 2", "press Right <1, 2>", "release Right <1, 2>")]
    [TestCase("  mouse  click  Left  330 175", "motion <330, 175>", "press Left <330, 175>", "release Left <330, 175>")]
    [TestCase("mouse wheel 0 -1 330 175", "wheel <0, -1> <330, 175>")]
    [TestCase("key down LeftCtrl", "down LeftCtrl")]
    [TestCase("key up e", "up E")]
    [TestCase("key press Return", "down Return", "up Return")]
    [TestCase("text hello  world ", "text 'hello  world '")]
    [TestCase("text", "text ''")]
    public void Execute_DispatchesCommand(string lines, params string[] expectedEvents)
    {
        (InputAutomationCommandInterpreter interpreter, List<string> events) = CreateInterpreter();

        List<string?> replies = lines.Split('\n').Select(interpreter.Execute).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(replies, Is.All.EqualTo("ok"));
            Assert.That(events, Is.EqualTo(expectedEvents));
        });
    }

    [TestCase("", null)]
    [TestCase("   ", null)]
    [TestCase("# a comment", null)]
    [TestCase("jump", "error: unknown command 'jump'")]
    [TestCase("mouse move 320", "error: unknown command 'mouse move 320'")]
    [TestCase("mouse move x 180", "error: invalid number 'x'")]
    [TestCase("mouse click Center 1 2", "error: unknown MouseButton 'Center'")]
    [TestCase("mouse click 9 1 2", "error: unknown MouseButton '9'")]
    [TestCase("key press Ctrl", "error: unknown Scancode 'Ctrl'")]
    [TestCase("screenshot", "error: unknown command 'screenshot'")]
    public void Execute_RejectsMalformedLineWithoutDispatching(string line, string? expectedReply)
    {
        (InputAutomationCommandInterpreter interpreter, List<string> events) = CreateInterpreter();

        string? reply = interpreter.Execute(line);

        Assert.Multiple(() =>
        {
            Assert.That(reply, Is.EqualTo(expectedReply));
            Assert.That(events, Is.Empty);
        });
    }

    [Test]
    public void Execute_PropagatesAutomationFailure()
    {
        // No window is registered for the default scope, so every input command fails inside the automation.
        InputAutomationCommandInterpreter interpreter = new(InputAutomationTests.CreateAutomation(new WindowRegistry()).Automation, new WindowRegistry(), new NoImageWriter());

        Assert.Throws<InvalidOperationException>(() => interpreter.Execute("key press A"));
    }

    [Test]
    public void Execute_Screenshot_WithoutDefaultWindow_Fails()
    {
        WindowRegistry windowRegistry = new();
        InputAutomationCommandInterpreter interpreter = new(InputAutomationTests.CreateAutomation(windowRegistry).Automation, windowRegistry, new NoImageWriter());

        Assert.That(interpreter.Execute("screenshot frame.png"), Is.EqualTo("error: no window for the default view scope"));
    }

    private static (InputAutomationCommandInterpreter Interpreter, List<string> Events) CreateInterpreter()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        (InputAutomation automation, MouseService mouseService, KeyboardService keyboardService, TextInputService textInputService) = InputAutomationTests.CreateAutomation(windowRegistry);

        List<string> events = new();
        mouseService.SubscribeMotion(0, eventArgs => events.Add($"motion {eventArgs.Position}"));
        mouseService.SubscribeButtonPress(0, eventArgs => events.Add($"press {eventArgs.Button} {eventArgs.Position}"));
        mouseService.SubscribeButtonRelease(0, eventArgs => events.Add($"release {eventArgs.Button} {eventArgs.Position}"));
        mouseService.SubscribeWheel(0, eventArgs => events.Add($"wheel {eventArgs.Delta} {eventArgs.Position}"));
        keyboardService.SubscribeKeyDown(0, eventArgs => events.Add($"down {eventArgs.Scancode}"));
        keyboardService.SubscribeKeyUp(0, eventArgs => events.Add($"up {eventArgs.Scancode}"));
        textInputService.SubscribeTextInput(0, eventArgs => events.Add($"text '{eventArgs.Text}'"));

        return (new InputAutomationCommandInterpreter(automation, windowRegistry, new NoImageWriter()), events);
    }

    private sealed class NoImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new NotSupportedException();
    }
}
