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
        SDL_WindowFlags defaultFlags = PixelyFactory.GetWindowCreationParameters(new WindowConfig()).Flags;
        SDL_WindowFlags utilityFlags = PixelyFactory.GetWindowCreationParameters(new WindowConfig(Utility: true)).Flags;

        Assert.Multiple(() =>
        {
            Assert.That(defaultFlags & SDL_WindowFlags.SDL_WINDOW_UTILITY, Is.EqualTo((SDL_WindowFlags)0));
            Assert.That(utilityFlags & SDL_WindowFlags.SDL_WINDOW_UTILITY, Is.EqualTo(SDL_WindowFlags.SDL_WINDOW_UTILITY));
        });
    }

    [Test]
    public void HighPixelDensity_IsOnByDefaultAndSetsTheSdlFlagOnlyWhileOn()
    {
        SDL_WindowFlags defaultFlags = PixelyFactory.GetWindowCreationParameters(new WindowConfig()).Flags;
        SDL_WindowFlags offFlags = PixelyFactory.GetWindowCreationParameters(new WindowConfig(HighPixelDensity: false)).Flags;

        Assert.Multiple(() =>
        {
            Assert.That(defaultFlags & SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY, Is.EqualTo(SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY));
            Assert.That(offFlags & SDL_WindowFlags.SDL_WINDOW_HIGH_PIXEL_DENSITY, Is.EqualTo((SDL_WindowFlags)0));
        });
    }

    [Test]
    public void Position_IsUnsetByDefaultAndPassedToSdl()
    {
        WindowPosition position = WindowPosition.At(new Vector2Int(10, 20));

        Assert.Multiple(() =>
        {
            Assert.That(PixelyFactory.GetWindowCreationParameters(new WindowConfig()).Position, Is.Null);
            Assert.That(PixelyFactory.GetWindowCreationParameters(new WindowConfig(Position: position)).Position, Is.EqualTo(position));
        });
    }

    [Test]
    public void Centered_EncodesSdlCentring()
    {
        WindowPosition position = WindowPosition.Centered();

        Assert.Multiple(() =>
        {
            Assert.That(position.SdlX, Is.EqualTo((int)SDL_WINDOWPOS_CENTERED));
            Assert.That(position.SdlY, Is.EqualTo((int)SDL_WINDOWPOS_CENTERED));
        });
    }
}
