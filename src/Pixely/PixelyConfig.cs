namespace Pixely;

public enum GpuBackend
{
    Automatic,
    Vulkan,
    Direct3D12,
    Metal,
    WebGpu
}

// Whether GpuMemorySystem.Submit() waits for the uploads to finish on the GPU before the frame is submitted. Automatic waits only
// on the Raspberry Pi's V3DV driver, whose binning can read a buffer before its upload has finished (docs/window-rendering.md).
public enum UploadWait
{
    Automatic,
    On,
    Off
}

// The settable properties are overridden from PIXELY_* environment variables by PixelyAppBuilder, see PixelyConfigEnvironment.
#if DEBUG
public sealed record PixelyConfig(
    bool EnableSdlLogging = true,
    bool EnableGpuValidation = true,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true,
    bool PreferLowPowerGpu = false,
    bool EnableDiagnostics = false,
    UploadWait UploadWait = UploadWait.Automatic,
    bool EnableCommandConsole = false)
{
    public bool EnableSdlLogging { get; internal set; } = EnableSdlLogging;
    public bool EnableGpuValidation { get; internal set; } = EnableGpuValidation;
    public GpuBackend GpuBackend { get; internal set; } = GpuBackend;
    public bool PreferLowPowerGpu { get; internal set; } = PreferLowPowerGpu;
    public bool EnableDiagnostics { get; internal set; } = EnableDiagnostics;
    public UploadWait UploadWait { get; internal set; } = UploadWait;
    public bool EnableCommandConsole { get; internal set; } = EnableCommandConsole;
    // The SDL video driver of the app, which SDL_VIDEO_DRIVER overrides; set by the test app builder of Pixely.Testing.
    internal string? SdlVideoDriver { get; set; }
    // The SDL audio driver of the app, which SDL_AUDIO_DRIVER overrides; set by the test app builder of Pixely.Testing.
    internal string? SdlAudioDriver { get; set; }
    // The app ignores the machine's gamepads and the OS's theme, so it behaves the same on any machine; set by headless mode
    // (Pixely.Headless) and the test app builder of Pixely.Testing.
    internal bool IsIsolated { get; set; }
}
#else
public sealed record PixelyConfig(
    bool EnableSdlLogging = false,
    bool EnableGpuValidation = false,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true,
    bool PreferLowPowerGpu = false,
    bool EnableDiagnostics = false,
    UploadWait UploadWait = UploadWait.Automatic,
    bool EnableCommandConsole = false)
{
    public bool EnableSdlLogging { get; internal set; } = EnableSdlLogging;
    public bool EnableGpuValidation { get; internal set; } = EnableGpuValidation;
    public GpuBackend GpuBackend { get; internal set; } = GpuBackend;
    public bool PreferLowPowerGpu { get; internal set; } = PreferLowPowerGpu;
    public bool EnableDiagnostics { get; internal set; } = EnableDiagnostics;
    public UploadWait UploadWait { get; internal set; } = UploadWait;
    public bool EnableCommandConsole { get; internal set; } = EnableCommandConsole;
    // The SDL video driver of the app, which SDL_VIDEO_DRIVER overrides; set by the test app builder of Pixely.Testing.
    internal string? SdlVideoDriver { get; set; }
    // The SDL audio driver of the app, which SDL_AUDIO_DRIVER overrides; set by the test app builder of Pixely.Testing.
    internal string? SdlAudioDriver { get; set; }
    // The app ignores the machine's gamepads and the OS's theme, so it behaves the same on any machine; set by headless mode
    // (Pixely.Headless) and the test app builder of Pixely.Testing.
    internal bool IsIsolated { get; set; }
}
#endif
