using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely;

/// <summary>
/// A window whose frames are presented through the swapchain of the GPU device it is claimed for.
/// </summary>
public sealed partial class SwapchainWindow : Window
{
    internal Pointer<SDL_GPUDevice> SdlGpuDevice { get; }

    internal SwapchainWindow(Pointer<SDL_Window> sdlWindow, Pointer<SDL_GPUDevice> sdlGpuDevice, uint sdlId, PixelyFrameClock frameClock, PlatformInfo platformInfo)
        : base(sdlWindow, sdlId, frameClock, platformInfo)
    {
        SdlGpuDevice = sdlGpuDevice;
    }

    public override TextureFormat ColorTargetFormat
    {
        get
        {
            unsafe
            {
                return (TextureFormat)SDL3.SDL_GetGPUSwapchainTextureFormat(SdlGpuDevice, SdlWindow);
            }
        }
    }

    public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
    {
        swapchainTexture = default!;
        uint width, height;

        unsafe
        {
            SDL_GPUTexture* swapchainTexturePointer;
            // A swapchain window is claimed only for the SDL device, so its command buffers are SDL ones.
            if (!AcquireSwapchainTexture(((SdlCommandBuffer)commandBuffer).Pointer, &swapchainTexturePointer, &width, &height))
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
            SDL3.SDL_ReleaseWindowFromGPUDevice(SdlGpuDevice, SdlWindow);
        }

        base.Dispose();
    }
}
