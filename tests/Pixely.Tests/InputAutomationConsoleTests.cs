using System.Collections.Concurrent;
using System.IO.Pipes;
using Pixely.App;
using Pixely.Content;
using Pixely.Input;

namespace Pixely.Tests;

public sealed class InputAutomationConsoleTests
{
    [Test]
    public void Update_RunsCommandsInOrder()
    {
        Fixture fixture = CreateConsole("key press A;\n\n;key\npress B;\n");

        fixture.Console.Update();

        Assert.That(fixture.Presses, Is.EqualTo(new[] { Scancode.A, Scancode.B }));
    }

    [Test]
    public void Update_Wait_HoldsLaterCommandsForItsFrames()
    {
        Fixture fixture = CreateConsole("key press A; wait 2; key press B; wait 0; key press C;");
        List<(int Presses, bool QuitRequested)> frames = new();

        for (int i = 0; i < 3; i++)
        {
            fixture.Console.Update();
            frames.Add((fixture.Presses.Count, fixture.AppControl.QuitRequested));
        }

        Assert.That(frames, Is.EqualTo(new[] { (1, false), (1, false), (3, true) }));
    }

    [Test]
    public void Update_Quit_StopsTheCommandsAfterIt()
    {
        Fixture fixture = CreateConsole("key press A; quit; key press B; bogus;");

        fixture.Console.Update();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Presses, Is.EqualTo(new[] { Scancode.A }));
            Assert.That(fixture.AppControl.QuitRequested, Is.True);
        });
    }

    [Test]
    public void Update_MalformedCommand_ThrowsOutOfTheFrame()
    {
        Fixture fixture = CreateConsole("bogus;");

        FormatException? exception = Assert.Throws<FormatException>(fixture.Console.Update);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("unknown command 'bogus'"));
            Assert.That(fixture.Presses, Is.Empty);
        });
    }

    [Test]
    public void Update_UnterminatedCommandAtEnd_ThrowsAfterTheCommandsBeforeIt()
    {
        Fixture fixture = CreateConsole("key press A; key press B\n");

        FormatException? exception = Assert.Throws<FormatException>(fixture.Console.Update);

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("unterminated command 'key press B'"));
            Assert.That(fixture.Presses, Is.EqualTo(new[] { Scancode.A }));
        });
    }

    [Test]
    public void Update_WhitespaceAfterLastCommand_IsNotAnUnterminatedCommand()
    {
        Fixture fixture = CreateConsole("key press A;\n \t\n");

        fixture.Console.Update();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Presses, Is.EqualTo(new[] { Scancode.A }));
            Assert.That(fixture.AppControl.QuitRequested, Is.True);
        });
    }

    [Test]
    public void Update_EndOfInput_Quits()
    {
        Fixture fixture = CreateConsole("");

        fixture.Console.Update();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Presses, Is.Empty);
            Assert.That(fixture.AppControl.QuitRequested, Is.True);
        });
    }

    [Test]
    public void Update_PartialCommand_BlocksUntilItsSemicolonWithinOneUpdate()
    {
        using AnonymousPipeServerStream server = new(PipeDirection.Out);
        using AnonymousPipeClientStream client = new(PipeDirection.In, server.ClientSafePipeHandle);
        // Disposed first, which closes the pipe, so a blocked update ends even when an assertion fails.
        using StreamWriter writer = new(server) { AutoFlush = true };
        Fixture fixture = CreateConsole(new StreamReader(client));

        writer.Write("key press A; key pre");
        Task update = Task.Run(fixture.Console.Update);
        // A has run, so the update now waits for the rest of the second command.
        Assert.That(() => fixture.Presses.Count, Is.EqualTo(1).After(5000, 10));

        writer.Write("ss B;");
        writer.Close();

        Assert.Multiple(() =>
        {
            Assert.That(update.Wait(TimeSpan.FromSeconds(5)), Is.True, "the update did not finish");
            Assert.That(fixture.Presses, Is.EqualTo(new[] { Scancode.A, Scancode.B }));
            Assert.That(fixture.AppControl.QuitRequested, Is.True);
        });
    }

    private static Fixture CreateConsole(string input)
    {
        return CreateConsole(new StringReader(input));
    }

    private static Fixture CreateConsole(TextReader input)
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(InputAutomationTests.CreateWindow(default, 42));
        (InputAutomation automation, _, KeyboardService keyboardService, _) = InputAutomationTests.CreateAutomation(windowRegistry);

        // Concurrent, because the pipe test runs the update on another thread.
        ConcurrentQueue<Scancode> presses = new();
        keyboardService.SubscribeKeyDown(0, eventArgs => presses.Enqueue(eventArgs.Scancode));

        AppControl appControl = new();
        InputAutomationCommandInterpreter interpreter = new(automation, windowRegistry, new NoImageWriter(), appControl);
        return new Fixture(new InputAutomationConsole(interpreter, input, appControl, NullFrameTimingRecorder.Instance), presses, appControl);
    }

    private sealed record Fixture(InputAutomationConsole Console, ConcurrentQueue<Scancode> Presses, AppControl AppControl);

    private sealed class NoImageWriter : IImageWriter
    {
        public void SavePng(Image image, string path) => throw new NotSupportedException();
    }
}
