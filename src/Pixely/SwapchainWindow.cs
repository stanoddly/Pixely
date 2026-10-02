using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

/// <summary>
/// A window whose frames are presented through the swapchain of the GPU device it is claimed for. It is claimed the first time
/// <see cref="ColorTargetFormat"/> or <see cref="TryWaitAndAcquireSwapchainTexture"/> is used, so a window nothing renders to
/// stays unclaimed. Without a GPU device, when the app registers no rendering, it has no swapchain, and both throw.
/// </summary>
public sealed partial class SwapchainWindow : Window
{
    internal Pointer<SDL_GPUDevice> SdlGpuDevice { get; }

    private bool _claimed;

    internal SwapchainWindow(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        Pointer<SDL_GPUDevice> sdlGpuDevice,
        uint sdlId,
        PixelyFrameClock frameClock,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior)
        : base(viewScope, sdlWindow, sdlId, frameClock, platformInfo, closeBehavior)
    {
        SdlGpuDevice = sdlGpuDevice;
    }

    public override TextureFormat ColorTargetFormat
    {
        get
        {
            EnsureClaimed();
            unsafe
            {
                return (TextureFormat)SDL3.SDL_GetGPUSwapchainTextureFormat(SdlGpuDevice, SdlWindow);
            }
        }
    }

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        EnsureClaimed();
        swapchainTexture = default!;
        uint width, height;

        unsafe
        {
            SDL_GPUTexture* swapchainTexturePointer;
            if (!AcquireSwapchainTexture(commandBuffer.SdlGpuCommandBuffer, &swapchainTexturePointer, &width, &height))
            {
                throw new PixelyInitializationException($"{AcquireSwapchainTextureCall} failed: {SDL3.SDL_GetError()}");
            }

            if (swapchainTexturePointer == null)
            {
                return false;
            }

            TextureFormat textureFormat = (TextureFormat)SDL3.SDL_GetGPUSwapchainTextureFormat(SdlGpuDevice, SdlWindow);

            swapchainTexture = new SwapchainTexture(swapchainTexturePointer, new ShortSize((ushort)width, (ushort)height), textureFormat);
        }

        return true;
    }

    public override void Dispose()
    {
        unsafe
        {
            if (_claimed)
            {
                SDL3.SDL_ReleaseWindowFromGPUDevice(SdlGpuDevice, SdlWindow);
            }
        }

        base.Dispose();
    }

    // The claim waits for the swapchain's first use because a claimed window changes when SDL's Vulkan backend frees finished
    // GPU work: only on a submit whose command buffer requested a swapchain texture, or on a fence wait. With no window claimed
    // it frees finished work on every submit. A window nothing renders to, as in an app with UseGpu() but no window rendering,
    // would never request a texture, so it would keep every finished submission and every buffer copy it holds alive.
    //
    // On a driver that reports a zero extent for a minimized window, such as NVIDIA on Win32, SDL's Vulkan claim returns true
    // without claiming the window, and the acquire then fails as for an unclaimed window. A window is normally first used on
    // the app's first frame, before it can be minimized; a window first rendered later, by a stage that starts while it is
    // minimized, would hit this.
    private void EnsureClaimed()
    {
        if (SdlGpuDevice.IsNull)
        {
            throw new InvalidOperationException("The window has no GPU device. Register rendering with UseDefaultRendering or call UseGpu().");
        }

        if (_claimed)
        {
            return;
        }

        unsafe
        {
            if (!SDL3.SDL_ClaimWindowForGPUDevice(SdlGpuDevice, SdlWindow))
            {
                throw new PixelyInitializationException($"SDL_ClaimWindowForGPUDevice failed: {SDL3.SDL_GetError()}");
            }
        }

        _claimed = true;
    }
}
