#if BROWSER
using Pixely.App;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

public partial class PixelyFactory
{
    // SDL's WebGPU backend, which ppy.SDL3-CS does not know: its shader format property, and the properties through which
    // it adopts the WebGPU instance, adapter and device the page created (SDL_gpu.h in stanoddly/SDL_wgpu).
    private static ReadOnlySpan<byte> WgslShadersProperty => "SDL.gpu.device.create.shaders.wgsl\0"u8;
    private const string WebGpuInstanceProperty = "SDL.gpu.device.create.webgpu.instance";
    private const string WebGpuAdapterProperty = "SDL.gpu.device.create.webgpu.adapter";
    private const string WebGpuDeviceProperty = "SDL.gpu.device.create.webgpu.device";

    // WGSL is the only format here. Requesting a WebGPU adapter and device is asynchronous, which SDL would wait out by
    // suspending the wasm stack under this managed frame, so the page requested them before the app was built and SDL adopts them.
    private GpuDevice CreateGpuDevice(SDL_PropertiesID props, GpuBackend gpuBackend)
    {
        SdlBoolInterop.SDL_SetBooleanProperty(props, WgslShadersProperty, true);

        WebGpuHandles handles = BrowserHost.WebGpuHandles
            ?? throw new PixelyInitializationException("The browser has no WebGPU device for Pixely to adopt. Await BrowserHost.PrepareAsync(builder) before building the app.");
        SDL3.SDL_SetPointerProperty(props, WebGpuInstanceProperty, handles.Instance);
        SDL3.SDL_SetPointerProperty(props, WebGpuAdapterProperty, handles.Adapter);
        SDL3.SDL_SetPointerProperty(props, WebGpuDeviceProperty, handles.Device);

        return CreateGpuDeviceFromProperties(props);
    }

    // SDL 3.4's fill-document flag is not an SDL_WindowFlags member in the bindings.
    private const SDL_WindowFlags FillDocumentWindowFlag = (SDL_WindowFlags)SDL3.SDL_WINDOW_FILL_DOCUMENT;

    // The browser has one "screen", the page, so the window fills it and follows the browser window's size. The configured
    // size and the desktop window options do not apply. The shipped page sizes the canvas through CSS, which makes SDL's
    // Emscripten driver drop fill-document mode and take the window size from the canvas box; only a resizable window then
    // reads that box again on each page resize and resizes the canvas bitmap with it. Fill-document mode stays on for a
    // page whose canvas has no CSS size.
    private Window CreateWindow(ViewScope viewScope, GpuDevice? gpuDevice, PixelyFrameContext frameContext, PlatformInfo platformInfo, WindowConfig config)
    {
        SDL_WindowFlags windowFlags = FillDocumentWindowFlag | SDL_WindowFlags.SDL_WINDOW_RESIZABLE | (config.InitiallyVisible ? 0 : SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        (Pointer<SDL_Window> sdlWindow, uint sdlWindowId) = CreateSdlWindow(gpuDevice, config.Title, DefaultSize.Width, DefaultSize.Height, windowFlags);
        return new SwapchainWindow(viewScope, sdlWindow, gpuDevice?.SdlGpuDevice ?? Pointer<SDL_GPUDevice>.Null, sdlWindowId, frameContext, platformInfo, config.CloseBehavior);
    }
}
#endif
