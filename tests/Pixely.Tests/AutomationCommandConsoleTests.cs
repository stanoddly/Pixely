using System.Collections.Concurrent;
using System.IO.Pipes;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

public sealed class AutomationCommandConsoleTests
{
    [Test]
    public void Update_RunsGameCommandsEndedByNewlineOrSemicolon()
    {
        Fixture fixture = CreateConsole("give gold 100\nspawn enemy; @7 spawn boss\r\n\n;");

        Assert.That(() => fixture.UpdateAndGetCalls(), Is.EqualTo(new[] { "give gold 100", "spawn enemy", "@7 spawn boss" }).After(5000, 10));
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Error.ToString(), Is.Empty);
            Assert.That(fixture.AppControl.QuitRequested, Is.False);
        });
    }

    [Test]
    public void Update_UnterminatedCommandAtEnd_Runs()
    {
        Fixture fixture = CreateConsole("give gold 1");

        Assert.That(() => fixture.UpdateAndGetCalls(), Is.EqualTo(new[] { "give gold 1" }).After(5000, 10));
    }

    [TestCase("key press A", "Command 'key press A' failed: unknown command 'key press A'")]
    [TestCase("Give gold", "Command 'Give gold' failed: unknown command 'Give gold'")]
    [TestCase("@x give gold", "Command '@x give gold' failed: invalid view scope 'x'")]
    [TestCase("@7 quit", "Command '@7 quit' failed: 'quit' takes no view scope")]
    [TestCase("fail", "Command 'fail' failed: bad")]
    public void Update_CommandThatCannotRun_IsReportedAndTheNextOneRuns(string command, string expectedError)
    {
        Fixture fixture = CreateConsole($"{command}\ngive gold 2\n");

        Assert.That(() => fixture.UpdateAndGetCalls(), Is.EqualTo(new[] { "give gold 2" }).After(5000, 10));
        Assert.That(fixture.Error.ToString(), Is.EqualTo(expectedError + Environment.NewLine));
    }

    [Test]
    public void Update_Quit_StopsTheCommandsAfterIt()
    {
        Fixture fixture = CreateConsole("give gold 3; quit; give gold 4;");

        Assert.That(() => { fixture.Console.Update(); return fixture.AppControl.QuitRequested; }, Is.True.After(5000, 10));
        fixture.Console.Update();

        Assert.That(fixture.Calls, Is.EqualTo(new[] { "give gold 3" }));
    }

    [Test]
    public void Update_WithoutInput_DoesNotWait()
    {
        using AnonymousPipeServerStream server = new(PipeDirection.Out);
        using AnonymousPipeClientStream client = new(PipeDirection.In, server.ClientSafePipeHandle);
        // Disposed first, which closes the pipe and ends the read.
        using StreamWriter writer = new(server) { AutoFlush = true };
        Fixture fixture = CreateConsole(new StreamReader(client));

        Task update = Task.Run(fixture.Console.Update);
        Assert.That(update.Wait(TimeSpan.FromSeconds(5)), Is.True, "the update waited for input");

        writer.Write("give gold 5\n");

        Assert.That(() => fixture.UpdateAndGetCalls(), Is.EqualTo(new[] { "give gold 5" }).After(5000, 10));
    }

    [Test]
    public void Update_GameCommandFailure_Propagates()
    {
        Fixture fixture = CreateConsole("crash\n");

        Assert.That(() =>
        {
            try
            {
                fixture.Console.Update();
                return null;
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }, Is.EqualTo("crash").After(5000, 10));
    }

    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    [NonParallelizable]
    public void Build_RegistersTheConsoleOnlyWhenEnabledOutsideHeadlessMode(bool enableCommandConsole, bool headless, bool expected)
    {
        // Building initializes SDL video; the dummy driver needs no display, and an environment override still wins.
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig(Headless: headless, EnableCommandConsole: enableCommandConsole));

        using IPixelyApp app = builder.Build();

        // The frame loop runs the registered updatables, so the console runs only when it is one of them.
        bool isUpdated = app.ServiceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>().OfType<AutomationCommandConsole>().Any();

        Assert.That(isUpdated, Is.EqualTo(expected));
    }

    private static Fixture CreateConsole(string input)
    {
        return CreateConsole(new StringReader(input));
    }

    private static Fixture CreateConsole(TextReader input)
    {
        // Concurrent, because the commands run on the test thread while the console reads on its own.
        ConcurrentQueue<string> calls = new();
        AutomationCommandRegistry commands = new();
        commands.Add(new RecordingCommand("give", calls));
        commands.Add(new RecordingCommand("spawn", calls));
        commands.Add(new FailingCommand("fail", new FormatException("bad")));
        commands.Add(new FailingCommand("crash", new InvalidOperationException("crash")));
        AppControl appControl = new();
        StringWriter error = new();
        return new Fixture(new AutomationCommandConsole(commands, input, error, appControl), calls, error, appControl);
    }

    private sealed record Fixture(AutomationCommandConsole Console, ConcurrentQueue<string> Calls, StringWriter Error, AppControl AppControl)
    {
        public string[] UpdateAndGetCalls()
        {
            Console.Update();
            return Calls.ToArray();
        }
    }

    private sealed class RecordingCommand(string name, ConcurrentQueue<string> calls) : AutomationCommand(name)
    {
        public override void Execute(string arguments, ViewScope? viewScope)
        {
            calls.Enqueue($"{(viewScope is ViewScope scope ? $"@{scope.Value} " : "")}{Name} {arguments}");
        }
    }

    private sealed class FailingCommand(string name, Exception exception) : AutomationCommand(name)
    {
        public override void Execute(string arguments, ViewScope? viewScope) => throw exception;
    }
}
