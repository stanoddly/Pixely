#if !BROWSER
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

public partial class PixelyFactory
{
    private Window CreateWindow(ViewScope viewScope, GpuDevice? gpuDevice, PixelyFrameContext frameContext, PlatformInfo platformInfo, WindowConfig config)
    {
        (uint width, uint height) = config.Fullscreen ? (0, 0) : config.Size ?? DefaultSize;
        SDL_WindowFlags windowFlags = 0;
        if (config.Fullscreen)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;
        }

        if (config.Resizable)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        }

        if (config.Transparent)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_TRANSPARENT;
        }

        if (config.Borderless)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_BORDERLESS;
        }

        if (config.AlwaysOnTop)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_ALWAYS_ON_TOP;
        }

        if (!config.InitiallyVisible)
        {
            windowFlags |= SDL_WindowFlags.SDL_WINDOW_HIDDEN;
        }

        (Pointer<SDL_Window> sdlWindow, uint sdlWindowId) = CreateSdlWindow(gpuDevice, config.Title, width, height, windowFlags);

        return new Window(viewScope, sdlWindow, gpuDevice?.SdlGpuDevice ?? Pointer<SDL_GPUDevice>.Null, sdlWindowId, frameContext, platformInfo, config.CloseBehavior);
    }
}
#endif
