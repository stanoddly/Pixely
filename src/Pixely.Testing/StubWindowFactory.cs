using Pixely.Gpu;

namespace Pixely.Testing;

// Creates every window of an app on the stub device as a StubWindow, without a platform.
// Such a window gets no taskbar icon: a test does not show it, and a wrong icon path shows on the game's first run.
internal sealed class StubWindowFactory : IWindowFactory
{
    private readonly StubGpuDevice _gpuDevice;
    private readonly StubEventSource _eventSource;

    internal StubWindowFactory(StubGpuDevice gpuDevice, StubEventSource eventSource)
    {
        _gpuDevice = gpuDevice;
        _eventSource = eventSource;
    }

    public Window CreateWindow(ViewScope viewScope, GpuDevice? gpuDevice, PixelyFrameClock frameClock, WindowConfig config, PlatformInfo platformInfo)
    {
        return new StubWindow(_gpuDevice, _eventSource, viewScope, frameClock, platformInfo, config);
    }
}
