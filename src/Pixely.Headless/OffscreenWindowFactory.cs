using Pixely.Gpu;
using SDL;

namespace Pixely.Headless;

// Creates every window of a headless app as an OffscreenWindow.
internal sealed class OffscreenWindowFactory : IWindowFactory
{
    private readonly PixelyFactory _pixelyFactory;

    internal OffscreenWindowFactory(PixelyFactory pixelyFactory)
    {
        _pixelyFactory = pixelyFactory;
    }

    public Window CreateWindow(ViewScope viewScope, GpuDevice? gpuDevice, PixelyFrameClock frameClock, WindowConfig config, PlatformInfo platformInfo)
    {
        if (gpuDevice == null)
        {
            throw new PixelyInitializationException("Headless mode renders into GPU textures and needs a GPU device. Register rendering with UseDefaultRendering or call UseGpu().");
        }

        // A device such as the stub of Pixely.Testing renders nothing, so a screenshot would read back frames never rendered.
        if (gpuDevice is not SdlGpuDevice)
        {
            throw new PixelyInitializationException($"Headless mode reads frames back from the GPU, which {gpuDevice.GetType().Name} does not render.");
        }

        // Only size and title matter: the SDL window is never shown, it just backs events and text input.
        // It is not claimed for the GPU device: with a claimed window, SDL's Vulkan backend frees finished work only on a
        // submit that requested a swapchain texture, which an offscreen window never does.
        // Selected before the SDL window exists, so a device without either format leaves no window to clean up.
        TextureFormat colorTargetFormat = OffscreenWindow.SelectColorTargetFormat(
            format => gpuDevice.IsTextureFormatSupported(format, TextureType.TwoD, TextureUsage.ColorTarget | TextureUsage.Sampler));
        (uint width, uint height) = config.Size ?? PixelyFactory.DefaultSize;
        return _pixelyFactory.CreateUnclaimedWindow(config.Title, width, height, SDL_WindowFlags.SDL_WINDOW_HIDDEN,
            (sdlWindow, sdlWindowId) => new OffscreenWindow(viewScope, sdlWindow, gpuDevice, colorTargetFormat, sdlWindowId, frameClock, platformInfo, WindowCloseBehavior.QuitApplication));
    }
}
