namespace Pixely;

public enum GpuBackend
{
    Automatic,
    Vulkan,
    Direct3D12,
    Metal,
    WebGpu
}

/// <summary>
/// SDL objects that were created outside Pixely and are handed to it instead of being created by
/// <see cref="PixelyFactory"/>. The browser needs this: SDL's WebGPU backend suspends the wasm stack
/// inside SDL_CreateGPUDevice, which no managed frame can survive, so the browser host has
/// JavaScript call SDL_CreateGPUDevice and hands the resulting device over.
/// </summary>
public sealed record AdoptedSdlHandles(IntPtr GpuDevice, IntPtr Window);

#if DEBUG
public sealed record PixelyConfig(
    bool EnableSdlLogging = true,
    bool EnableGpuValidation = true,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true,
    AdoptedSdlHandles? AdoptedSdlHandles = null);
#else
public sealed record PixelyConfig(
    bool EnableSdlLogging = false,
    bool EnableGpuValidation = false,
    GpuBackend GpuBackend = GpuBackend.Automatic,
    string? ApplicationIdentifier = null,
    string? TaskbarIconPath = null,
    bool DeliverActivatingMouseClicks = true,
    AdoptedSdlHandles? AdoptedSdlHandles = null);
#endif
