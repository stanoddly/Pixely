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
    [TestCase("@0 key press Return", "down Return", "up Return")]
    [TestCase("@7 key press Return", "7: down Return", "7: up Return")]
    [TestCase("@7 mouse move 1 2", "7: motion <1, 2>")]
    [TestCase("@7 text hi", "7: text 'hi'")]
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
    [TestCase("@x key press A", "error: invalid view scope 'x'")]
    [TestCase("@7", "error: unknown command ''")]
    [TestCase("@9 key press A", "error: no window for view scope 9")]
    [TestCase("@9 screenshot frame.png", "error: no window for view scope 9")]
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
    public void Execute_PropagatesHandlerFailure()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        (InputAutomation automation, _, KeyboardService keyboardService, _) = InputAutomationTests.CreateAutomation(windowRegistry);
        keyboardService.SubscribeKeyDown(0, _ => throw new InvalidOperationException("game broke"));
        InputAutomationCommandInterpreter interpreter = new(automation, windowRegistry, new NoImageWriter());

        Assert.Throws<InvalidOperationException>(() => interpreter.Execute("key press A"));
    }

    [Test]
    public void Execute_WithoutDefaultWindow_Fails()
    {
        WindowRegistry windowRegistry = new();
        InputAutomationCommandInterpreter interpreter = new(InputAutomationTests.CreateAutomation(windowRegistry).Automation, windowRegistry, new NoImageWriter());

        Assert.Multiple(() =>
        {
            Assert.That(interpreter.Execute("key press A"), Is.EqualTo("error: no window for view scope 0"));
            Assert.That(interpreter.Execute("screenshot frame.png"), Is.EqualTo("error: no window for view scope 0"));
        });
    }

    private static (InputAutomationCommandInterpreter Interpreter, List<string> Events) CreateInterpreter()
    {
        ViewScope secondary = new(7);
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        windowRegistry.Register(InputAutomationTests.CreateWindow(secondary, 43));
        (InputAutomation automation, MouseService mouseService, KeyboardService keyboardService, TextInputService textInputService) = InputAutomationTests.CreateAutomation(windowRegistry);

        List<string> events = new();
        mouseService.SubscribeMotion(0, eventArgs => events.Add($"motion {eventArgs.Position}"));
        mouseService.SubscribeButtonPress(0, eventArgs => events.Add($"press {eventArgs.Button} {eventArgs.Position}"));
        mouseService.SubscribeButtonRelease(0, eventArgs => events.Add($"release {eventArgs.Button} {eventArgs.Position}"));
        mouseService.SubscribeWheel(0, eventArgs => events.Add($"wheel {eventArgs.Delta} {eventArgs.Position}"));
        keyboardService.SubscribeKeyDown(0, eventArgs => events.Add($"down {eventArgs.Scancode}"));
        keyboardService.SubscribeKeyUp(0, eventArgs => events.Add($"up {eventArgs.Scancode}"));
        textInputService.SubscribeTextInput(0, eventArgs => events.Add($"text '{eventArgs.Text}'"));
        mouseService.SubscribeMotion(secondary, 0, eventArgs => events.Add($"7: motion {eventArgs.Position}"));
        keyboardService.SubscribeKeyDown(secondary, 0, eventArgs => events.Add($"7: down {eventArgs.Scancode}"));
        keyboardService.SubscribeKeyUp(secondary, 0, eventArgs => events.Add($"7: up {eventArgs.Scancode}"));
        textInputService.SubscribeTextInput(secondary, 0, eventArgs => events.Add($"7: text '{eventArgs.Text}'"));

        return (new InputAutomationCommandInterpreter(automation, windowRegistry, new NoImageWriter()), events);
    }

    private sealed class NoImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new NotSupportedException();
    }
}
