using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Tests;

public sealed class WindowConfigTests
{
    [Test]
    public void Defaults_AreInitiallyVisibleAndQuitApplicationOnClose()
    {
        WindowConfig config = new();

        Assert.Multiple(() =>
        {
            Assert.That(config.InitiallyVisible, Is.True);
            Assert.That(config.CloseBehavior, Is.EqualTo(WindowCloseBehavior.QuitApplication));
        });
    }

    [Test]
    public void Configuration_CanStartHiddenAndHideOnClose()
    {
        WindowConfig config = new(
            InitiallyVisible: false,
            CloseBehavior: WindowCloseBehavior.HideWindow);

        Assert.Multiple(() =>
        {
            Assert.That(config.InitiallyVisible, Is.False);
            Assert.That(config.CloseBehavior, Is.EqualTo(WindowCloseBehavior.HideWindow));
        });
    }

    [Test]
    public void Utility_SetsTheSdlFlagOnlyWhenRequested()
    {
        (uint _, uint _, SDL_WindowFlags defaultFlags) = PixelyFactory.GetWindowCreationParameters(new WindowConfig());
        (uint _, uint _, SDL_WindowFlags utilityFlags) = PixelyFactory.GetWindowCreationParameters(new WindowConfig(Utility: true));

        Assert.Multiple(() =>
        {
            Assert.That(defaultFlags & SDL_WindowFlags.SDL_WINDOW_UTILITY, Is.EqualTo((SDL_WindowFlags)0));
            Assert.That(utilityFlags & SDL_WindowFlags.SDL_WINDOW_UTILITY, Is.EqualTo(SDL_WindowFlags.SDL_WINDOW_UTILITY));
        });
    }
}
