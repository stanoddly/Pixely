#if !BROWSER
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

public partial class PixelyFactory
{
    // Advertises the formats the desktop backends take and creates the device. Automatic on Windows advertises SPIR-V and DXIL
    // so SDL picks Vulkan or Direct3D 12; the Vulkan options apply whenever Vulkan can be selected.
    private GpuDevice CreateGpuDevice(SDL_PropertiesID props, GpuBackend gpuBackend)
    {
        bool advertiseSpirV = gpuBackend == GpuBackend.Vulkan ||
                              (gpuBackend == GpuBackend.Automatic && !OperatingSystem.IsMacOS());
        bool advertiseDxil = gpuBackend == GpuBackend.Direct3D12 ||
                             (gpuBackend == GpuBackend.Automatic && OperatingSystem.IsWindows());
        bool advertiseMsl = gpuBackend == GpuBackend.Metal ||
                            (gpuBackend == GpuBackend.Automatic && OperatingSystem.IsMacOS());

        if (advertiseSpirV)
        {
            SdlBoolInterop.SDL_SetBooleanProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_SHADERS_SPIRV_BOOLEAN, true);
        }

        if (advertiseDxil)
        {
            SdlBoolInterop.SDL_SetBooleanProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_SHADERS_DXIL_BOOLEAN, true);
        }

        if (advertiseMsl)
        {
            SdlBoolInterop.SDL_SetBooleanProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_SHADERS_MSL_BOOLEAN, true);
        }

        if (!advertiseSpirV)
        {
            return CreateGpuDeviceFromProperties(props);
        }

        unsafe
        {
            SDL_GPUVulkanOptions vulkanOptions = default;
            // Request Vulkan 1.3.0 ((1 << 22) | (3 << 12) | 0). SDL defaults to Vulkan 1.0, which takes only SPIR-V 1.0,
            // and Slang's stable SPIR-V target support starts at SPIR-V 1.3:
            // https://shader-slang.org/slang/user-guide/spirv-target-specific
            vulkanOptions.vulkan_api_version = (1 << 22) | (3 << 12) | 0;

            SDL_GPUVulkanOptions* vulkanOptionsPointer = &vulkanOptions;
            SDL3.SDL_SetPointerProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_VULKAN_OPTIONS_POINTER, (IntPtr)vulkanOptionsPointer);

            // The options live on this stack until the device exists.
            return CreateGpuDeviceFromProperties(props);
        }
    }

    private Window CreateWindow(ViewScope viewScope, SdlGpuDevice? gpuDevice, PixelyFrameClock frameClock, PlatformInfo platformInfo, WindowConfig config)
    {
        (uint width, uint height, SDL_WindowFlags windowFlags) = GetWindowShape(config);
        (Pointer<SDL_Window> sdlWindow, uint sdlWindowId) = CreateSdlWindow(gpuDevice, config.Title, width, height, windowFlags);

        return new SwapchainWindow(viewScope, sdlWindow, gpuDevice?.Pointer ?? Pointer<SDL_GPUDevice>.Null, sdlWindowId, frameClock, platformInfo, config.CloseBehavior);
    }

    // For an IWindowFactory: an SDL window that is not claimed for the GPU device, wrapped in a window of the factory's own type.
    // Nothing owns the SDL window until the wrapper exists, so a failure to create the wrapper destroys it.
    internal Window CreateUnclaimedWindow(string? title, uint width, uint height, SDL_WindowFlags windowFlags, Func<Pointer<SDL_Window>, uint, Window> createWindow)
    {
        (Pointer<SDL_Window> sdlWindow, uint sdlWindowId) = CreateSdlWindow(null, title, width, height, windowFlags);

        try
        {
            return createWindow(sdlWindow, sdlWindowId);
        }
        catch
        {
            unsafe
            {
                SDL3.SDL_DestroyWindow(sdlWindow);
            }

            throw;
        }
    }

    // The size and flags of a window created as its config says.
    internal static (uint Width, uint Height, SDL_WindowFlags Flags) GetWindowShape(WindowConfig config)
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

        return (width, height, windowFlags);
    }
}
#endif
