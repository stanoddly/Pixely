namespace Pixely;

public enum GpuBackend
{
    Automatic,
    Vulkan,
    Direct3D12,
    Metal,
    WebGpu
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
    bool Headless = false)
{
    public bool EnableSdlLogging { get; internal set; } = EnableSdlLogging;
    public bool EnableGpuValidation { get; internal set; } = EnableGpuValidation;
    public GpuBackend GpuBackend { get; internal set; } = GpuBackend;
    public bool Headless { get; internal set; } = Headless;
}
#else
public sealed record PixelyConfig(
    bool EnableSdlLogging = false,
    bool EnableGpuValidation = false,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true,
    bool Headless = false)
{
    public bool EnableSdlLogging { get; internal set; } = EnableSdlLogging;
    public bool EnableGpuValidation { get; internal set; } = EnableGpuValidation;
    public GpuBackend GpuBackend { get; internal set; } = GpuBackend;
    public bool Headless { get; internal set; } = Headless;
}
#endif
