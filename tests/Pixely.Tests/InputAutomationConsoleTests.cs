using Pixely.Content;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationConsoleTests
{
    [Test]
    public void Update_RunsCommandsOnCallingThreadInOrder()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("key press A;\n\n;key\npress B;\n", out GatedReader reader);

        StartAndReadAll(console, reader);
        console.Update();

        Assert.Multiple(() =>
        {
            Assert.That(presses.Select(press => press.Scancode), Is.EqualTo(new[] { Scancode.A, Scancode.B }));
            Assert.That(presses.Select(press => press.ThreadId), Is.All.EqualTo(Environment.CurrentManagedThreadId));
        });
    }

    [Test]
    public void Update_Wait_HoldsLaterCommandsForItsFrames()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("key press A; wait 2; key press B; wait 0; key press C;", out GatedReader reader);
        StartAndReadAll(console, reader);
        List<int> pressCounts = new();

        for (int i = 0; i < 4; i++)
        {
            console.Update();
            pressCounts.Add(presses.Count);
        }

        Assert.That(pressCounts, Is.EqualTo(new[] { 1, 1, 3, 3 }));
    }

    [Test]
    public void Update_MalformedCommand_ThrowsOutOfTheFrame()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("bogus;", out GatedReader reader);
        StartAndReadAll(console, reader);

        FormatException? exception = Assert.Throws<FormatException>(console.Update);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("unknown command 'bogus'"));
            Assert.That(presses, Is.Empty);
        });
    }

    [Test]
    public void Update_UnterminatedCommandAtEnd_ThrowsAfterTheCommandsBeforeIt()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("key press A; key press B\n", out GatedReader reader);
        StartAndReadAll(console, reader);

        // The reader queues the unterminated rest after its last read, so the first updates may not find it yet.
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
            Assert.That(exception!.Message, Is.EqualTo("unterminated command 'key press B'"));
            Assert.That(presses.Select(press => press.Scancode), Is.EqualTo(new[] { Scancode.A }));
        });
    }

    [Test]
    public void Update_WhitespaceAfterLastCommand_IsNotAnUnterminatedCommand()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("key press A;\n \t\n", out GatedReader reader);
        StartAndReadAll(console, reader);

        console.Update();

        Assert.That(presses.Select(press => press.Scancode), Is.EqualTo(new[] { Scancode.A }));
    }

    [Test]
    public void Update_WithEmptyInput_DoesNothing()
    {
        (InputAutomationConsole console, List<(Scancode Scancode, int ThreadId)> presses) = CreateConsole("", out GatedReader reader);
        StartAndReadAll(console, reader);

        console.Update();

        Assert.That(presses, Is.Empty);
    }

    // The first update starts the reader, which is held until then, so the updates after this see the whole input.
    private static void StartAndReadAll(InputAutomationConsole console, GatedReader reader)
    {
        console.Update();
        reader.Open();
        Assert.That(reader.WaitUntilFinished(TimeSpan.FromSeconds(5)), Is.True, "the reader did not reach the end of the input");
    }

    private static (InputAutomationConsole Console, List<(Scancode Scancode, int ThreadId)> Presses) CreateConsole(string input, out GatedReader reader)
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        (InputAutomation automation, _, KeyboardService keyboardService, _) = InputAutomationTests.CreateAutomation(windowRegistry);

        List<(Scancode Scancode, int ThreadId)> presses = new();
        keyboardService.SubscribeKeyDown(0, eventArgs => presses.Add((eventArgs.Scancode, Environment.CurrentManagedThreadId)));

        InputAutomationCommandInterpreter interpreter = new(automation, windowRegistry, new NoImageWriter(), new HeadlessClock(), new AppControl());
        reader = new GatedReader(input);
        return (new InputAutomationConsole(interpreter, reader), presses);
    }

    private sealed class GatedReader(string input) : TextReader
    {
        private readonly StringReader _inner = new(input);
        private readonly ManualResetEventSlim _opened = new();
        private readonly ManualResetEventSlim _finished = new();

        public void Open() => _opened.Set();

        public bool WaitUntilFinished(TimeSpan timeout) => _finished.Wait(timeout);

        public override int Read()
        {
            _opened.Wait();
            int character = _inner.Read();
            if (character < 0)
            {
                _finished.Set();
            }

            return character;
        }
    }

    private sealed class NoImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new NotSupportedException();
    }
}
