using Pixely.Gpu;

namespace Pixely;

// Creates an app's windows in place of the SDL device's swapchain windows: the offscreen windows of headless mode
// (Pixely.Headless) or the windows of the stub GPU device (Pixely.Testing). AddWindow uses the registered one, and an app has
// at most one, because its windows come from one place.
internal interface IWindowFactory
{
    Window CreateWindow(ViewScope viewScope, GpuDevice? gpuDevice, PixelyFrameClock frameClock, WindowConfig config, PlatformInfo platformInfo);
}
