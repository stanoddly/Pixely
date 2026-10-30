using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Testing;

/// <summary>
/// The window of an app on a <see cref="StubGpuDevice"/>: an SDL window created as configured, on SDL's dummy video driver
/// unless the environment picks another, without a swapchain. Acquiring hands out a swapchain texture of the window's size at
/// once, with no wait. Events, size, visibility and input work as on any other window.
/// </summary>
public sealed class StubWindow : Window
{
    private readonly StubGpuDevice _gpuDevice;

    internal StubWindow(
        StubGpuDevice gpuDevice,
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        uint sdlId,
        PixelyFrameClock frameClock,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior)
        : base(viewScope, sdlWindow, sdlId, frameClock, platformInfo, closeBehavior)
    {
        _gpuDevice = gpuDevice;
    }

    // The format of a desktop window's sRGB swapchain on Vulkan and Direct3D 12.
    public override TextureFormat ColorTargetFormat => TextureFormat.B8G8R8A8UnormSrgb;

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        swapchainTexture = _gpuDevice.CreateSwapchainTexture(RenderSizeInPixels, ColorTargetFormat);
        return true;
    }
}
