using Pixely.Gpu;

namespace Pixely;

// Creates the app's window in place of the SDL device's swapchain window: the offscreen window of headless mode
// (Pixely.Headless) or the window of the stub GPU device (Pixely.Testing). The app builder uses the registered one, and an app has
// at most one, because its window comes from one place.
internal interface IWindowFactory
{
    Window CreateWindow(GpuDevice gpuDevice, PixelyFrameClock frameClock, WindowConfig config, PlatformInfo platformInfo);
}
