using Pixely.Content;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationConsoleTests
{
    [Test]
    public void Update_RunsQueuedLinesOnCallingThreadAndRepliesInOrder()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses, StringWriter output) = CreateConsole("key press A\n\nbogus\nkey press B\n");

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (presses.Count < 2 && DateTime.UtcNow < deadline)
        {
            console.Update();
            Thread.Yield();
        }

        Assert.Multiple(() =>
        {
            Assert.That(presses.Select(press => press.Scancode), Is.EqualTo(new[] { Scancode.A, Scancode.B }));
            Assert.That(presses.Select(press => press.ThreadId), Is.All.EqualTo(Environment.CurrentManagedThreadId));
            Assert.That(output.ToString(), Is.EqualTo($"ok{Environment.NewLine}error: unknown command 'bogus'{Environment.NewLine}ok{Environment.NewLine}"));
        });
    }

    [Test]
    public void Update_WithEmptyInput_DoesNothing()
    {
        (InputAutomationConsole console, _, StringWriter output) = CreateConsole("");

        console.Update();
        console.Update();

        Assert.That(output.ToString(), Is.Empty);
    }

    private static (InputAutomationConsole Console, List<(Scancode Scancode, int ThreadId)> Presses, StringWriter Output) CreateConsole(string input)
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        (InputAutomation automation, _, KeyboardService keyboardService, _) = InputAutomationTests.CreateAutomation(windowRegistry);

        List<(Scancode Scancode, int ThreadId)> presses = new();
        keyboardService.SubscribeKeyDown(0, eventArgs => presses.Add((eventArgs.Scancode, Environment.CurrentManagedThreadId)));

        StringWriter output = new();
        InputAutomationConsole console = new(automation, windowRegistry, new NoImageWriter(), new StringReader(input), output);
        return (console, presses, output);
    }

    private sealed class NoImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new NotSupportedException();
    }
}
