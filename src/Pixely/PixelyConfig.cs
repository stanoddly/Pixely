namespace Pixely;

public enum GpuBackend
{
    Automatic,
    Vulkan,
    Direct3D12,
    Metal
}

#if DEBUG
public sealed record PixelyConfig(
    bool EnableSdlLogging = true,
    bool EnableGpuValidation = true,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true);
#else
public sealed record PixelyConfig(
    bool EnableSdlLogging = false,
    bool EnableGpuValidation = false,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true);
#endif
