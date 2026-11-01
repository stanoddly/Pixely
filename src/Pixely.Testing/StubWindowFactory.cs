using Pixely.Gpu;
using SDL;

namespace Pixely.Testing;

// Creates the window of an app on the stub device as a StubWindow, around an SDL window created as its WindowConfig says.
// Such a window gets no taskbar icon: a test does not show it, and a wrong icon path shows on the game's first run.
internal sealed class StubWindowFactory : IWindowFactory
{
    private readonly PixelyFactory _pixelyFactory;

    internal StubWindowFactory(PixelyFactory pixelyFactory)
    {
        _pixelyFactory = pixelyFactory;
    }

    public Window CreateWindow(GpuDevice gpuDevice, PixelyFrameClock frameClock, WindowConfig config, PlatformInfo platformInfo)
    {
        if (gpuDevice is not StubGpuDevice stubGpuDevice)
        {
            throw new PixelyInitializationException($"A stub window needs the stub GPU device, but the app's device is {gpuDevice.GetType().Name}.");
        }

        (uint width, uint height, SDL_WindowFlags windowFlags) = PixelyFactory.GetWindowShape(config);
        return _pixelyFactory.CreateUnclaimedWindow(config.Title, width, height, windowFlags,
            (sdlWindow, sdlWindowId) => new StubWindow(stubGpuDevice, sdlWindow, sdlWindowId, frameClock, platformInfo));
    }
}
