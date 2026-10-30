using Pixely.Gpu;
using SDL;

namespace Pixely.Testing;

// Creates every window of an app on the stub device as a StubWindow, around an SDL window created as its WindowConfig says.
// Such a window gets no taskbar icon: a test does not show it, and a wrong icon path shows on the game's first run.
internal sealed class StubWindowFactory : IWindowFactory
{
    private readonly PixelyFactory _pixelyFactory;
    private readonly StubGpuDevice _gpuDevice;

    internal StubWindowFactory(PixelyFactory pixelyFactory, StubGpuDevice gpuDevice)
    {
        _pixelyFactory = pixelyFactory;
        _gpuDevice = gpuDevice;
    }

    public Window CreateWindow(ViewScope viewScope, GpuDevice? gpuDevice, PixelyFrameClock frameClock, WindowConfig config, PlatformInfo platformInfo)
    {
        (uint width, uint height, SDL_WindowFlags windowFlags) = PixelyFactory.GetWindowShape(config);
        return _pixelyFactory.CreateUnclaimedWindow(config.Title, width, height, windowFlags,
            (sdlWindow, sdlWindowId) => new StubWindow(_gpuDevice, viewScope, sdlWindow, sdlWindowId, frameClock, platformInfo, config.CloseBehavior));
    }
}
