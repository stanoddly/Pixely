using Pixely.App;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

[NonParallelizable]
public class AutomationCommandRegistrationTests
{
    [SetUp]
    public void SetUp()
    {
        // Building initializes SDL video; the dummy driver needs no display, and an environment override still wins.
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
    }

    [Test]
    public void Build_TwoCommandsWithSameName_Throws()
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig(Headless: true));
        builder.AddSingleton<AutomationCommand>(new SpawnCommand());
        builder.AddSingleton<AutomationCommand>(new SpawnCommand());

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("'spawn'"));
    }

    [Test]
    public void StageCommand_JoinsOnLoadAndLeavesOnUnload()
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig(Headless: true));
        using IPixelyApp app = builder.Build();
        AutomationCommandRegistry registry = app.GetRequiredService<AutomationCommandRegistry>();
        StageManager stageManager = app.GetRequiredService<StageManager>();
        SpawnCommand spawn = new();

        stageManager.Load(stage => stage.AddSingleton<AutomationCommand>(spawn));
        stageManager.ApplyPendingTransition();
        bool joined = registry.TryGet("spawn", out AutomationCommand? command) && ReferenceEquals(command, spawn);

        stageManager.Load(static _ => { });
        stageManager.ApplyPendingTransition();

        Assert.Multiple(() =>
        {
            Assert.That(joined, Is.True);
            Assert.That(registry.TryGet("spawn", out _), Is.False);
        });
    }

    [Test]
    public void StageCommand_WithNameOfAppCommand_FailsStageLoad()
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig(Headless: true));
        builder.AddSingleton<AutomationCommand>(new SpawnCommand());
        using IPixelyApp app = builder.Build();
        StageManager stageManager = app.GetRequiredService<StageManager>();

        stageManager.Load(static stage => stage.AddSingleton<AutomationCommand>(new SpawnCommand()));

        Assert.That(stageManager.ApplyPendingTransition, Throws.InvalidOperationException.With.Message.Contains("'spawn'"));
    }

    private sealed class SpawnCommand() : AutomationCommand("spawn")
    {
        public override void Execute(string arguments, ViewScope? viewScope)
        {
        }
    }
}
