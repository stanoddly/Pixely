using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

/// <summary>
/// A window whose frames are presented through the swapchain of the GPU device it is claimed for. Without a GPU device, when
/// the app registers no rendering, it has no swapchain, and <see cref="ColorTargetFormat"/> and
/// <see cref="TryWaitAndAcquireSwapchainTexture"/> throw.
/// </summary>
public sealed partial class SwapchainWindow : Window
{
    internal Pointer<SDL_GPUDevice> SdlGpuDevice { get; }

    internal SwapchainWindow(
        ViewScope viewScope,
        Pointer<SDL_Window> sdlWindow,
        Pointer<SDL_GPUDevice> sdlGpuDevice,
        uint sdlId,
        PixelyFrameContext frameContext,
        PlatformInfo platformInfo,
        WindowCloseBehavior closeBehavior)
        : base(viewScope, sdlWindow, sdlId, frameContext, platformInfo, closeBehavior)
    {
        SdlGpuDevice = sdlGpuDevice;
    }

    public override TextureFormat ColorTargetFormat
    {
        get
        {
            ThrowIfNoGpuDevice();
            unsafe
            {
                return (TextureFormat)SDL3.SDL_GetGPUSwapchainTextureFormat(SdlGpuDevice, SdlWindow);
            }
        }
    }

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        ThrowIfNoGpuDevice();
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
            if (!SdlGpuDevice.IsNull)
            {
                SDL3.SDL_ReleaseWindowFromGPUDevice(SdlGpuDevice, SdlWindow);
            }
        }

        base.Dispose();
    }

    private void ThrowIfNoGpuDevice()
    {
        if (SdlGpuDevice.IsNull)
        {
            throw new InvalidOperationException("The window has no GPU device. Register rendering with UseDefaultRendering or call UseGpu().");
        }
    }
}
