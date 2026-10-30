using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Utilities;
using SDL;

namespace Pixely;

public partial class PixelyFactory: IDisposable
{
    internal static readonly Size<uint> DefaultSize = (640, 480);

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

        // At default priority, so the environment overrides it. So would a driver hint the process sets at normal priority, but nothing
        // in Pixely sets one, and a test that does chose its driver. SDL_Quit clears it with every other hint, so it lasts as long as the app.
        // A failed SDL_Init skips SDL_Quit and leaves it set, like the hints above. Only a later app in the same process would inherit
        // it, after a start that has already failed and been reported, so it is not cleared there.
        if (_config.SdlVideoDriver != null)
        {
            SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, _config.SdlVideoDriver, SDL_HintPriority.SDL_HINT_DEFAULT);
        }

        // The same for audio, which Pixely.Audio initializes later, while the hint still holds.
        if (_config.SdlAudioDriver != null)
        {
            SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_AUDIO_DRIVER, _config.SdlAudioDriver, SDL_HintPriority.SDL_HINT_DEFAULT);
        }

        // Installed before SDL_Init, which logs its own startup messages; a failed init does not reach Dispose, so it uninstalls here.
        SdlLogOutput.Install(_sdlLogger);
        SDL_InitFlags initFlags = SDL_InitFlags.SDL_INIT_EVENTS | SDL_InitFlags.SDL_INIT_VIDEO;
        // An isolated app sees only synthetic gamepads, so it behaves the same whether or not a pad is plugged in.
        if (!_config.IsIsolated)
        {
            initFlags |= SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_GAMEPAD;
        }

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
        PixelyFrameClock frameClock,
        WindowConfig config,
        PlatformInfo platformInfo,
        IImageLoader imageLoader)
    {
        // Only the SDL device has swapchains. Another device, such as the stub of Pixely.Testing, registers an IWindowFactory, which
        // AddWindow uses instead of this method.
        if (gpuDevice is { } and not SdlGpuDevice)
        {
            throw new PixelyInitializationException($"{gpuDevice.GetType().Name} has no swapchains to create windows with. Build the app from TestApp.CreateBuilder() of Pixely.Testing, which also creates the stub's windows.");
        }

        Window window = CreateWindow(viewScope, (SdlGpuDevice?)gpuDevice, frameClock, platformInfo, config);

        if (_config.TaskbarIconPath != null)
        {
            _taskbarIcon ??= imageLoader.Load(_config.TaskbarIconPath);
            _ = window.SetIcon(_taskbarIcon);
        }

        return window;
    }

    private (Pointer<SDL_Window> SdlWindow, uint SdlWindowId) CreateSdlWindow(SdlGpuDevice? gpuDevice, string? title, uint width, uint height, SDL_WindowFlags windowFlags)
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

        // Nothing owns the SDL window until this returns, so every failure below releases the claim and destroys the window.
        bool claimed = false;
        try
        {
            // A claimed window makes SDL's Vulkan backend free finished GPU work only on a submit that requested a swapchain texture,
            // or on a fence wait. Without window rendering nothing requests one, so such an app must wait on a fence regularly, and
            // must also submit GpuMemorySystem itself.
            if (gpuDevice != null)
            {
                unsafe
                {
                    if (SDL3.SDL_ClaimWindowForGPUDevice(gpuDevice.Pointer, sdlWindow) == false)
                    {
                        throw new PixelyInitializationException($"GPUClaimWindow failed: {SDL3.SDL_GetError()}");
                    }
                }

                claimed = true;
                UseSrgbSwapchain(gpuDevice, sdlWindow);
            }

            uint sdlWindowId;
            unsafe
            {
                sdlWindowId = (uint)SDL3.SDL_GetWindowID(sdlWindow);

                if (sdlWindowId == 0)
                {
                    throw new PixelyInitializationException($"SDL_GetWindowID failed: {SDL3.SDL_GetError()}");
                }
            }

            return (sdlWindow, sdlWindowId);
        }
        catch
        {
            unsafe
            {
                if (claimed)
                {
                    SDL3.SDL_ReleaseWindowFromGPUDevice(gpuDevice!.Pointer, sdlWindow);
                }

                SDL3.SDL_DestroyWindow(sdlWindow);
            }

            throw;
        }
    }

    // Shaders work in linear light and an SDR_LINEAR swapchain encodes to sRGB on write. There is no fallback to the SDR swapchain,
    // because it would need every shader to encode as well. SDL does not always set an error for an unsupported composition, so
    // that case has its own message.
    private static void UseSrgbSwapchain(SdlGpuDevice gpuDevice, Pointer<SDL_Window> sdlWindow)
    {
        unsafe
        {
            if (SDL3.SDL_WindowSupportsGPUSwapchainComposition(gpuDevice.Pointer, sdlWindow, SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR_LINEAR) == false)
            {
                throw new PixelyInitializationException(
                    $"The GPU driver ({gpuDevice.Driver}, {gpuDevice.NativeDriverName ?? "native driver unknown"}) has no sRGB swapchain (SDR_LINEAR) for the window, which Pixely needs.");
            }

            if (SDL3.SDL_SetGPUSwapchainParameters(gpuDevice.Pointer, sdlWindow, SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR_LINEAR, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC) == false)
            {
                throw new PixelyInitializationException($"SDL_SetGPUSwapchainParameters failed: {SDL3.SDL_GetError()}");
            }
        }
    }

    internal GpuDevice CreateGpuDevice()
    {
        GpuBackend gpuBackend = _config.GpuBackend;

        EnsureSdlInitialized();

        SDL_PropertiesID props = SDL3.SDL_CreateProperties();
        try
        {
            SdlBoolInterop.SDL_SetBooleanProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_DEBUGMODE_BOOLEAN, _config.EnableGpuValidation);
            SdlBoolInterop.SDL_SetBooleanProperty(props, SDL3.SDL_PROP_GPU_DEVICE_CREATE_PREFERLOWPOWER_BOOLEAN, _config.PreferLowPowerGpu);

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

    internal GpuMemorySystem CreateGpuMemorySystem(GpuDevice gpuDevice)
    {
#if BROWSER
        // Waiting needs a fence wait, which the browser build cannot do (GpuDevice.WaitForFences).
        if (_config.UploadWait == UploadWait.On)
        {
            throw new PixelyInitializationException("UploadWait.On is not supported in the browser.");
        }

        return new GpuMemorySystem(gpuDevice, false);
#else
        return new GpuMemorySystem(gpuDevice, GpuMemorySystem.ShouldWaitForUploads(_config.UploadWait, gpuDevice.NativeDriverName));
#endif
    }

    private static unsafe GpuDevice CreateGpuDeviceFromProperties(SDL_PropertiesID props)
    {
        Pointer<SDL_GPUDevice> device = SDL3.SDL_CreateGPUDeviceWithProperties(props);
        if (device.IsNull)
        {
            throw new PixelyInitializationException($"SDL_CreateGPUDevice failed: {SDL3.SDL_GetError()}");
        }

        return SdlGpuDevice.Create(device);
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
        // Without the gamepad subsystem, which an isolated app does not start, SDL cannot list gamepads.
        if (!_config.IsIsolated)
        {
            gamepadService.SetupGamepads();
        }

        return gamepadService;
    }

    internal MouseService CreateMouseService(WindowRegistry windowRegistry)
    {
        EnsureSdlInitialized();

        return new MouseService(windowRegistry);
    }

    internal CursorService CreateCursorService()
    {
        EnsureSdlInitialized();

        return new CursorService();
    }

    internal PlatformSettings CreatePlatformSettings()
    {
        EnsureSdlInitialized();

        return new PlatformSettings(_config.IsIsolated);
    }

    internal TextInputService CreateTextInputService(WindowRegistry windowRegistry)
    {
        EnsureSdlInitialized();

        return new TextInputService(windowRegistry);
    }

    // A null result registers nothing. Headless mode (Pixely.Headless) reads standard input with a console of its own and turns
    // EnableCommandConsole off.
    [UnsupportedOSPlatform("browser")]
    internal AutomationCommandConsole? CreateAutomationCommandConsole(AutomationCommandRegistry commands, AppControl appControl)
    {
        if (!_config.EnableCommandConsole)
        {
            return null;
        }

        return new AutomationCommandConsole(commands, new StreamReader(Console.OpenStandardInput()), Console.Error, appControl);
    }

    internal EventService CreateEventService(
        KeyboardService keyboardService,
        GamepadService gamepadService,
        MouseService mouseService,
        TextInputService textInputService,
        PlatformSettings platformSettings,
        WindowRegistry windowRegistry,
        AppControl appControl)
    {
        EnsureSdlInitialized();

        return new EventService(
            keyboardService,
            gamepadService,
            mouseService,
            textInputService,
            platformSettings,
            windowRegistry,
            appControl);
    }

    public void Dispose()
    {
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;

        if (!_initialized)
        {
            return;
        }

#if !BROWSER
        // In the browser SDL lives as long as the page, which frees it, as it does the GPU device.
        SDL3.SDL_Quit();
#endif
        SdlLogOutput.Uninstall();
        _initialized = false;
    }
}
