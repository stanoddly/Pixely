using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Utilities;
using SDL;

namespace Pixely;

public partial class PixelyFactory: IDisposable
{
    private static readonly Size<uint> DefaultSize = (640, 480);

    private readonly PixelyConfig _config;
    private readonly ILogger? _sdlLogger;
    private Image? _taskbarIcon;
    private bool _initialized;

    // The logger factory is optional: without one SDL's messages go to the console.
    public PixelyFactory(PixelyConfig config, ILoggerFactory? loggerFactory)
    {
        _config = config;
        _sdlLogger = loggerFactory?.CreateLogger("SDL");
    }

    private void EnsureSdlInitialized()
    {
        if (_initialized)
        {
            return;
        }

        if (_config.ApplicationIdentifier != null &&
            !SDL3.SDL_SetAppMetadataProperty(SDL3.SDL_PROP_APP_METADATA_IDENTIFIER_STRING, _config.ApplicationIdentifier))
        {
            throw new PixelyInitializationException($"SDL_SetAppMetadataProperty failed for the application identifier: {SDL3.SDL_GetError()}");
        }

        //SDL3.SDL_SetHint(SDL3.SDL_HINT_EVENT_LOGGING, "2");
        //SDL3.SDL_SetHint(SDL3.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        if (_config.EnableSdlLogging)
        {
            SDL3.SDL_SetHint(SDL3.SDL_HINT_LOGGING, "*=debug");
        }

        // SDL swallows the mouse click that activates an unfocused window by default, which loses the first
        // click whenever the user switches windows, including between two windows of the same application.
        SDL3.SDL_SetHint(SDL3.SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, _config.DeliverActivatingMouseClicks ? "1" : "0");

        // Installed before SDL_Init, which logs its own startup messages; a failed init does not reach Dispose, so it uninstalls here.
        SdlLogOutput.Install(_sdlLogger);
        SDL_InitFlags initFlags = SDL_InitFlags.SDL_INIT_EVENTS | SDL_InitFlags.SDL_INIT_VIDEO |
                                  SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_GAMEPAD;
        if (SDL3.SDL_Init(initFlags) == false)
        {
            SdlLogOutput.Uninstall();
            throw new PixelyInitializationException($"SDL_Init failed: {SDL3.SDL_GetError()}");
        }

        _initialized = true;
    }

    private static unsafe string? GetCurrentVideoDriver()
    {
        byte* videoDriver = SDL3.Unsafe_SDL_GetCurrentVideoDriver();
        return Marshal.PtrToStringUTF8((IntPtr)videoDriver);
    }

    internal PlatformInfo CreatePlatformInfo()
    {
        EnsureSdlInitialized();
        return new PlatformInfo(GetCurrentVideoDriver());
    }

    // A null device is an app that registered no rendering: the window is created without being claimed for a device.
    internal Window CreateWindow(
        ViewScope viewScope,
        GpuDevice? gpuDevice,
        PixelyFrameContext frameContext,
        WindowConfig config,
        PlatformInfo platformInfo,
        IImageLoader imageLoader)
    {
        if (_config.Headless)
        {
#if BROWSER
            // Headless mode reads commands from standard input and frames back from the GPU, neither of which the page has.
            throw new PixelyInitializationException("Headless mode is not supported in the browser.");
#else
            if (gpuDevice == null)
            {
                throw new PixelyInitializationException("Headless mode renders into GPU textures and needs a GPU device. Register rendering with UseDefaultRendering or call UseGpu().");
            }

            return CreateOffscreenWindow(viewScope, gpuDevice, frameContext, platformInfo, config);
#endif
        }

        Window window = CreateWindow(viewScope, gpuDevice, frameContext, platformInfo, config);

        if (_config.TaskbarIconPath != null)
        {
            _taskbarIcon ??= imageLoader.Load(_config.TaskbarIconPath);
            _ = window.SetIcon(_taskbarIcon);
        }

        return window;
    }

    private (Pointer<SDL_Window> SdlWindow, uint SdlWindowId) CreateSdlWindow(GpuDevice? gpuDevice, string? title, uint width, uint height, SDL_WindowFlags windowFlags)
    {
        EnsureSdlInitialized();

        // The entry assembly's name, which the browser has too, unlike a process name.
        string windowTitle = title ?? AppDomain.CurrentDomain.FriendlyName;

        Pointer<SDL_Window> sdlWindow;
        unsafe
        {
            sdlWindow = SDL3.SDL_CreateWindow(windowTitle, (int)width, (int)height, windowFlags);
        }

        if (sdlWindow.IsNull)
        {
            throw new PixelyInitializationException($"SDL_CreateWindow failed: {SDL3.SDL_GetError()}");
        }

        unsafe
        {
            if (gpuDevice != null && SDL3.SDL_ClaimWindowForGPUDevice(gpuDevice.SdlGpuDevice, sdlWindow) == false)
            {
                throw new PixelyInitializationException($"GPUClaimWindow failed: {SDL3.SDL_GetError()}");
            }
        }

        uint sdlWindowId;
        unsafe
        {
            sdlWindowId = (uint)SDL3.SDL_GetWindowID(sdlWindow);

            if (sdlWindowId == 0)
            {
                throw new PixelyInitializationException($"GPUClaimWindow failed: {SDL3.SDL_GetError()}");
            }
        }

        return (sdlWindow, sdlWindowId);
    }

    internal GpuDevice CreateGpuDevice()
    {
        GpuBackend gpuBackend = _config.GpuBackend;

        EnsureSdlInitialized();

        SDL_PropertiesID props = SDL3.SDL_CreateProperties();
        try
        {
            SdlBoolInterop.SDL_SetBooleanProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_DEBUGMODE_BOOLEAN, _config.EnableGpuValidation);

            string? driverName = gpuBackend switch
            {
                GpuBackend.Automatic => null,
                GpuBackend.Vulkan => "vulkan",
                GpuBackend.Direct3D12 => "direct3d12",
                GpuBackend.Metal => "metal",
                GpuBackend.WebGpu => "webgpu",
                _ => throw new ArgumentOutOfRangeException(nameof(_config.GpuBackend), gpuBackend, "Unknown GPU backend")
            };
            if (driverName != null)
            {
                SDL3.SDL_SetStringProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_NAME_STRING, driverName);
            }

            // The shader formats and the backend-specific options are the host's: PixelyFactory.Desktop.cs and PixelyFactory.Browser.cs.
            return CreateGpuDevice(props, gpuBackend);
        }
        finally
        {
            SDL3.SDL_DestroyProperties(props);
        }
    }

    private static unsafe GpuDevice CreateGpuDeviceFromProperties(SDL_PropertiesID props)
    {
        Pointer<SDL_GPUDevice> device = SDL3.SDL_CreateGPUDeviceWithProperties(props);
        if (device.IsNull)
        {
            throw new PixelyInitializationException($"SDL_CreateGPUDevice failed: {SDL3.SDL_GetError()}");
        }

        return new GpuDevice(device);
    }

    internal KeyboardService CreateKeyboardService(AppControl appControl)
    {
        EnsureSdlInitialized();

        return new KeyboardService(appControl);
    }
    
    internal GamepadService CreateGamepadService()
    {
        EnsureSdlInitialized();
        
        GamepadService gamepadService = new();
        gamepadService.SetupGamepads();
        
        return gamepadService;
    }

    internal MouseService CreateMouseService(WindowRegistry windowRegistry)
    {
        EnsureSdlInitialized();

        return new MouseService(windowRegistry);
    }

    internal TextInputService CreateTextInputService(WindowRegistry windowRegistry)
    {
        EnsureSdlInitialized();

        return new TextInputService(windowRegistry);
    }

    // Automation exists in headless mode only; a null result registers nothing.
    internal InputAutomation? CreateInputAutomation(WindowRegistry windowRegistry, MouseService mouseService, KeyboardService keyboardService, TextInputService textInputService)
    {
        return _config.Headless ? new InputAutomation(windowRegistry, mouseService, keyboardService, textInputService) : null;
    }

    internal IImageWriter? CreateImageWriter()
    {
        return _config.Headless ? new SdlImageWriter() : null;
    }

    [UnsupportedOSPlatform("browser")]
    internal InputAutomationConsole? CreateInputAutomationConsole(InputAutomation? inputAutomation, WindowRegistry windowRegistry, IImageWriter? imageWriter)
    {
        if (inputAutomation is null || imageWriter is null)
        {
            return null;
        }

        // Raw standard streams, so reading never changes the terminal mode the way Console.In does on Unix.
        return new InputAutomationConsole(inputAutomation, windowRegistry, imageWriter, new StreamReader(Console.OpenStandardInput()));
    }

    internal EventService CreateEventService(
        KeyboardService keyboardService,
        GamepadService gamepadService,
        MouseService mouseService,
        TextInputService textInputService,
        WindowRegistry windowRegistry,
        AppControl appControl)
    {
        EnsureSdlInitialized();

        return new EventService(
            keyboardService,
            gamepadService,
            mouseService,
            textInputService,
            windowRegistry,
            appControl);
    }

    public PixelyFrameContext CreateFrameContext()
    {
        return new PixelyFrameContext();
    }

    public void Dispose()
    {
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;

        if (!_initialized)
        {
            return;
        }

        SDL3.SDL_Quit();
        SdlLogOutput.Uninstall();
        _initialized = false;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct VkPhysicalDeviceShaderDrawParametersFeatures
{
    public const uint StructureType = 1000063000;

    public uint sType;
    public IntPtr pNext;
    public uint shaderDrawParameters;
}
