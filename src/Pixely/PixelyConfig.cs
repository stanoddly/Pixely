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
    bool Headless = false,
    bool PreferLowPowerGpu = false,
    bool EnableDiagnostics = false,
    UploadWait UploadWait = UploadWait.Automatic)
{
    public bool EnableSdlLogging { get; internal set; } = EnableSdlLogging;
    public bool EnableGpuValidation { get; internal set; } = EnableGpuValidation;
    public GpuBackend GpuBackend { get; internal set; } = GpuBackend;
    public bool Headless { get; internal set; } = Headless;
    public bool PreferLowPowerGpu { get; internal set; } = PreferLowPowerGpu;
    public bool EnableDiagnostics { get; internal set; } = EnableDiagnostics;
    public UploadWait UploadWait { get; internal set; } = UploadWait;
}
#else
public sealed record PixelyConfig(
    bool EnableSdlLogging = false,
    bool EnableGpuValidation = false,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true,
    bool Headless = false,
    bool PreferLowPowerGpu = false,
    bool EnableDiagnostics = false,
    UploadWait UploadWait = UploadWait.Automatic)
{
    public bool EnableSdlLogging { get; internal set; } = EnableSdlLogging;
    public bool EnableGpuValidation { get; internal set; } = EnableGpuValidation;
    public GpuBackend GpuBackend { get; internal set; } = GpuBackend;
    public bool Headless { get; internal set; } = Headless;
    public bool PreferLowPowerGpu { get; internal set; } = PreferLowPowerGpu;
    public bool EnableDiagnostics { get; internal set; } = EnableDiagnostics;
    public UploadWait UploadWait { get; internal set; } = UploadWait;
}
#endif
