using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Pixely.App;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

// What SDL creates a window from. A null title is the entry assembly's name, and a null position leaves placement to the platform.
internal readonly record struct SdlWindowParameters(string? Title, uint Width, uint Height, SDL_WindowFlags Flags, WindowPosition? Position = null);

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

        using NullTerminatedUtf8 applicationIdentifier = new NullTerminatedUtf8(_config.ApplicationIdentifier);
        if (_config.ApplicationIdentifier != null && !SDL_SetAppMetadataProperty(SDL_PROP_APP_METADATA_IDENTIFIER_STRING, applicationIdentifier.Span))
        {
            throw new PixelyInitializationException($"SDL_SetAppMetadataProperty failed for the application identifier: {SdlError.GetError()}");
        }

        //SDL_SetHint(SDL_HINT_EVENT_LOGGING, "2");
        //SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        if (_config.EnableSdlLogging)
        {
            SDL_SetHint(SDL_HINT_LOGGING, "*=debug"u8);
        }

        // SDL swallows the mouse click that activates an unfocused window by default, which loses the first
        // click whenever the user switches windows, including between two windows of the same application.
        SDL_SetHint(SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, _config.DeliverActivatingMouseClicks ? "1"u8 : "0"u8);

        // At default priority, so the environment overrides it. So would a driver hint the process sets at normal priority, but nothing
        // in Pixely sets one, and a test that does chose its driver. SDL_Quit clears it with every other hint, so it lasts as long as the app.
        // A failed SDL_Init skips SDL_Quit and leaves it set, like the hints above. Only a later app in the same process would inherit
        // it, after a start that has already failed and been reported, so it is not cleared there.
        if (_config.SdlVideoDriver != null)
        {
            using NullTerminatedUtf8 videoDriver = new NullTerminatedUtf8(_config.SdlVideoDriver);
            SDL_SetHintWithPriority(SDL_HINT_VIDEO_DRIVER, videoDriver.Span, SDL_HintPriority.SDL_HINT_DEFAULT);
        }

        // The same for audio, which Pixely.Audio initializes later, while the hint still holds.
        if (_config.SdlAudioDriver != null)
        {
            using NullTerminatedUtf8 audioDriver = new NullTerminatedUtf8(_config.SdlAudioDriver);
            SDL_SetHintWithPriority(SDL_HINT_AUDIO_DRIVER, audioDriver.Span, SDL_HintPriority.SDL_HINT_DEFAULT);
        }

        // Installed before SDL_Init, which logs its own startup messages; a failed init does not reach Dispose, so it uninstalls here.
        SdlLogOutput.Install(_sdlLogger);
        SDL_InitFlags initFlags = SDL_InitFlags.SDL_INIT_EVENTS | SDL_InitFlags.SDL_INIT_VIDEO;
        // An isolated app sees only synthetic gamepads, so it behaves the same whether or not a pad is plugged in.
        if (!_config.IsIsolated)
        {
            initFlags |= SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_GAMEPAD;
        }

        if (SDL_Init(initFlags) == false)
        {
            SdlLogOutput.Uninstall();
            throw new PixelyInitializationException($"SDL_Init failed: {SdlError.GetError()}");
        }

        _initialized = true;
    }

    private static string? GetCurrentVideoDriver() => Marshal.PtrToStringUTF8((nint)SDL_GetCurrentVideoDriver());

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

    private (Pointer<SDL_Window> SdlWindow, uint SdlWindowId) CreateSdlWindow(SdlGpuDevice? gpuDevice, SdlWindowParameters parameters)
    {
        EnsureSdlInitialized();

        // The entry assembly's name, which the browser has too, unlike a process name.
        using NullTerminatedUtf8 windowTitle = new NullTerminatedUtf8(parameters.Title ?? AppDomain.CurrentDomain.FriendlyName);
        Pointer<SDL_Window> sdlWindow;
        SDL_PropertiesID props = SDL_CreateProperties();
        if (props == 0)
        {
            throw new PixelyInitializationException($"SDL_CreateProperties failed: {SdlError.GetError()}");
        }

        try
        {
            SDL_SetStringProperty(props, SDL_PROP_WINDOW_CREATE_TITLE_STRING, windowTitle.Span);
            SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_WIDTH_NUMBER, parameters.Width);
            SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_HEIGHT_NUMBER, parameters.Height);
            SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_FLAGS_NUMBER, (long)parameters.Flags);
            // Without a position SDL leaves the placement to the platform, as SDL_CreateWindow does.
            if (parameters.Position is { } position)
            {
                SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_X_NUMBER, position.SdlX);
                SDL_SetNumberProperty(props, SDL_PROP_WINDOW_CREATE_Y_NUMBER, position.SdlY);
            }

            sdlWindow = SDL_CreateWindowWithProperties(props);
        }
        finally
        {
            SDL_DestroyProperties(props);
        }

        if (sdlWindow.IsNull)
        {
            throw new PixelyInitializationException($"SDL_CreateWindowWithProperties failed: {SdlError.GetError()}");
        }

        // Nothing owns the SDL window until this returns, so every failure below releases the claim and destroys the window.
        bool claimed = false;
        try
        {
            // Without window rendering no render coordinator submits GpuMemorySystem, so such an app must submit it itself. Nothing
            // requests the claimed window's swapchain texture either, so SDL's Vulkan backend never defragments GPU memory or frees
            // empty allocations, which with a claimed window wait for a submit that requested one.
            if (gpuDevice != null)
            {
                unsafe
                {
                    if (SDL_ClaimWindowForGPUDevice(gpuDevice.Pointer, sdlWindow) == false)
                    {
                        throw new PixelyInitializationException($"GPUClaimWindow failed: {SdlError.GetError()}");
                    }
                }

                claimed = true;
                UseSrgbSwapchain(gpuDevice, sdlWindow);
            }

            uint sdlWindowId;
            unsafe
            {
                sdlWindowId = (uint)SDL_GetWindowID(sdlWindow);

                if (sdlWindowId == 0)
                {
                    throw new PixelyInitializationException($"SDL_GetWindowID failed: {SdlError.GetError()}");
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
                    SDL_ReleaseWindowFromGPUDevice(gpuDevice!.Pointer, sdlWindow);
                }

                SDL_DestroyWindow(sdlWindow);
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
            if (SDL_WindowSupportsGPUSwapchainComposition(gpuDevice.Pointer, sdlWindow, SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR_LINEAR) == false)
            {
                throw new PixelyInitializationException(
                    $"The GPU driver ({gpuDevice.Driver}, {gpuDevice.NativeDriverName ?? "native driver unknown"}) has no sRGB swapchain (SDR_LINEAR) for the window, which Pixely needs.");
            }

            if (SDL_SetGPUSwapchainParameters(gpuDevice.Pointer, sdlWindow, SDL_GPUSwapchainComposition.SDL_GPU_SWAPCHAINCOMPOSITION_SDR_LINEAR, SDL_GPUPresentMode.SDL_GPU_PRESENTMODE_VSYNC) == false)
            {
                throw new PixelyInitializationException($"SDL_SetGPUSwapchainParameters failed: {SdlError.GetError()}");
            }
        }
    }

    internal GpuDevice CreateGpuDevice()
    {
        GpuBackend gpuBackend = _config.GpuBackend;

        EnsureSdlInitialized();

        SDL_PropertiesID props = SDL_CreateProperties();
        try
        {
            SDL_SetBooleanProperty(props, SDL_PROP_GPU_DEVICE_CREATE_DEBUGMODE_BOOLEAN, _config.EnableGpuValidation);
            SDL_SetBooleanProperty(props, SDL_PROP_GPU_DEVICE_CREATE_PREFERLOWPOWER_BOOLEAN, _config.PreferLowPowerGpu);

            ReadOnlySpan<byte> driverName = gpuBackend switch
            {
                GpuBackend.Automatic => default,
                GpuBackend.Vulkan => "vulkan"u8,
                GpuBackend.Direct3D12 => "direct3d12"u8,
                GpuBackend.Metal => "metal"u8,
                GpuBackend.WebGpu => "webgpu"u8,
                _ => throw new ArgumentOutOfRangeException(nameof(_config.GpuBackend), gpuBackend, "Unknown GPU backend")
            };
            if (!driverName.IsEmpty)
            {
                SDL_SetStringProperty(props, SDL_PROP_GPU_DEVICE_CREATE_NAME_STRING, driverName);
            }

            // The shader formats and the backend-specific options are the host's: PixelyFactory.Desktop.cs and PixelyFactory.Browser.cs.
            return CreateGpuDevice(props, gpuBackend);
        }
        finally
        {
            SDL_DestroyProperties(props);
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
        Pointer<SDL_GPUDevice> device = SDL_CreateGPUDeviceWithProperties(props);
        if (device.IsNull)
        {
            throw new PixelyInitializationException($"SDL_CreateGPUDevice failed: {SdlError.GetError()}");
        }

        return SdlGpuDevice.Create(device);
    }

    // Starts SDL's audio after SDL itself, so the audio driver hint and the app metadata are set first. Pixely.Audio stops it.
    internal bool InitializeAudio()
    {
        EnsureSdlInitialized();

        return SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
    }

    internal KeyboardService CreateKeyboardService(AppControl appControl)
    {
        return new KeyboardService(appControl);
    }
    
    internal GamepadService CreateGamepadService()
    {
        GamepadService gamepadService = new();
        // Without the gamepad subsystem, which an isolated app does not start, SDL cannot list gamepads.
        if (!_config.IsIsolated)
        {
            EnsureSdlInitialized();
            gamepadService.SetupGamepads();
        }

        return gamepadService;
    }

    internal MouseService CreateMouseService(WindowRegistry windowRegistry)
    {
        return new MouseService(windowRegistry);
    }

    internal CursorService CreateCursorService()
    {
        EnsureSdlInitialized();

        return new CursorService();
    }

    internal DisplayService CreateDisplayService()
    {
        EnsureSdlInitialized();

        return new DisplayService();
    }

    internal PlatformSettings CreatePlatformSettings()
    {
        EnsureSdlInitialized();

        return new PlatformSettings(_config.IsIsolated);
    }

    internal TextInputService CreateTextInputService(WindowRegistry windowRegistry)
    {
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
        PlatformSettings? platformSettings,
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
        SDL_Quit();
#endif
        SdlLogOutput.Uninstall();
        _initialized = false;
    }
}
