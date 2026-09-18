using Pixely.Content;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationConsoleTests
{
    [Test]
    public void Update_RunsQueuedLinesOnCallingThreadInOrder()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("key press A\n\n# comment\nkey press B\n");

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
        });
    }

    [Test]
    public void Update_MalformedLine_ThrowsOutOfTheFrame()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("bogus\n");

        // The reader thread queues the line at its own pace, so the first updates may find nothing.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        FormatException? exception = null;
        while (exception == null && DateTime.UtcNow < deadline)
        {
            try
            {
                console.Update();
                Thread.Yield();
            }
            catch (FormatException caught)
            {
                exception = caught;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(exception?.Message, Is.EqualTo("unknown command 'bogus'"));
            Assert.That(presses, Is.Empty);
        });
    }

    [Test]
    public void Update_WithEmptyInput_DoesNothing()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("");

        console.Update();
        console.Update();

        Assert.That(presses, Is.Empty);
    }

    private static (InputAutomationConsole Console, List<(Scancode Scancode, int ThreadId)> Presses) CreateConsole(string input)
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        (InputAutomation automation, _, KeyboardService keyboardService, _) = InputAutomationTests.CreateAutomation(windowRegistry);

        List<(Scancode Scancode, int ThreadId)> presses = new();
        keyboardService.SubscribeKeyDown(0, eventArgs => presses.Add((eventArgs.Scancode, Environment.CurrentManagedThreadId)));

        InputAutomationConsole console = new(automation, windowRegistry, new NoImageWriter(), new StringReader(input));
        return (console, presses);
    }

    private sealed class NoImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new NotSupportedException();
    }
}
