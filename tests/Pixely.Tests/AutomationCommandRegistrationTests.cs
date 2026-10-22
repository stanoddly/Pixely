using Pixely.App;
using Pixely.Input;
using SDL;

namespace Pixely.Tests;

[NonParallelizable]
public class AutomationCommandRegistrationTests
{
    [Test]
    public void Build_Headless_PassesRegisteredCommandsToInterpreter()
    {
        // Building initializes SDL video; the dummy driver needs no display, and an environment override still wins.
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig(Headless: true));
        builder.AddSingleton<IAutomationCommand>(new SpawnCommand());
        builder.AddSingleton<IAutomationCommand>(new OtherSpawnCommand());

        // The interpreter rejects the duplicate name only if both registrations reach it.
        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("'spawn'"));
    }

    private sealed class SpawnCommand : IAutomationCommand
    {
        public string Name => "spawn";

        public void Execute(string arguments, ViewScope? viewScope)
        {
        }
    }

    private sealed class OtherSpawnCommand : IAutomationCommand
    {
        public string Name => "spawn";

        public void Execute(string arguments, ViewScope? viewScope)
        {
        }
    }
}
