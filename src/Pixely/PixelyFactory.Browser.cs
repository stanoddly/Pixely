#if BROWSER
using Pixely.App;
using Pixely.Gpu;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

public partial class PixelyFactory
{
    // WGSL is the only format here. Requesting a WebGPU adapter and device is asynchronous, which SDL would wait out by
    // suspending the wasm stack under this managed frame, so the page requested them before the app was built and SDL adopts them.
    private GpuDevice CreateGpuDevice(SDL_PropertiesID props, GpuBackend gpuBackend)
    {
        SDL_SetBooleanProperty(props, XDL_PROP_GPU_DEVICE_CREATE_SHADERS_WGSL_BOOLEAN, true);

        WebGpuHandles handles = BrowserHost.WebGpuHandles
            ?? throw new PixelyInitializationException("The browser has no WebGPU device for Pixely to adopt. Await BrowserHost.PrepareAsync(builder) before building the app.");
        SDL_SetPointerProperty(props, XDL_PROP_GPU_DEVICE_CREATE_WEBGPU_INSTANCE_POINTER, handles.Instance);
        SDL_SetPointerProperty(props, XDL_PROP_GPU_DEVICE_CREATE_WEBGPU_ADAPTER_POINTER, handles.Adapter);
        SDL_SetPointerProperty(props, XDL_PROP_GPU_DEVICE_CREATE_WEBGPU_DEVICE_POINTER, handles.Device);

        return CreateGpuDeviceFromProperties(props);
    }

    // The browser has one "screen", the page, so the window fills it and follows the browser window's size. The configured
    // size and the desktop window options do not apply. The shipped page sizes the canvas through CSS, which makes SDL's
    // Emscripten driver drop fill-document mode and take the window size from the canvas box; only a resizable window then
    // reads that box again on each page resize and resizes the canvas bitmap with it. Fill-document mode stays on for a
    // page whose canvas has no CSS size.
    private Window CreateWindow(ViewScope viewScope, SdlGpuDevice? gpuDevice, PixelyFrameClock frameClock, PlatformInfo platformInfo, WindowConfig config)
    {
        SDL_WindowFlags windowFlags = SDL_WindowFlags.SDL_WINDOW_FILL_DOCUMENT | SDL_WindowFlags.SDL_WINDOW_RESIZABLE | (config.InitiallyVisible ? 0 : SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        (Pointer<SDL_Window> sdlWindow, uint sdlWindowId) = CreateSdlWindow(gpuDevice, config.Title, DefaultSize.Width, DefaultSize.Height, windowFlags);
        return new SwapchainWindow(viewScope, sdlWindow, gpuDevice?.Pointer ?? Pointer<SDL_GPUDevice>.Null, sdlWindowId, frameClock, platformInfo, config.CloseBehavior);
    }
}
#endif
