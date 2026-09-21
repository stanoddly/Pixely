#if BROWSER
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

public partial class PixelyFactory
{
    // SDL 3.4's fill-document flag is not an SDL_WindowFlags member in the bindings.
    private const SDL_WindowFlags FillDocumentWindowFlag = (SDL_WindowFlags)SDL3.SDL_WINDOW_FILL_DOCUMENT;

    // The browser has one "screen", the page, so the window fills it and follows the browser window's size. The configured
    // size and the desktop window options do not apply.
    private Window CreateWindow(ViewScope viewScope, GpuDevice? gpuDevice, PixelyFrameContext frameContext, PlatformInfo platformInfo, WindowConfig config)
    {
        SDL_WindowFlags windowFlags = FillDocumentWindowFlag | (config.InitiallyVisible ? 0 : SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        (Pointer<SDL_Window> sdlWindow, uint sdlWindowId) = CreateSdlWindow(gpuDevice, config.Title, DefaultSize.Width, DefaultSize.Height, windowFlags);
        return new Window(viewScope, sdlWindow, gpuDevice?.SdlGpuDevice ?? Pointer<SDL_GPUDevice>.Null, sdlWindowId, frameContext, platformInfo, config.CloseBehavior);
    }
}
#endif
