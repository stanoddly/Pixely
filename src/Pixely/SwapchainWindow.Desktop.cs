#if !BROWSER
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely;

public sealed partial class SwapchainWindow
{
    private const string AcquireSwapchainTextureCall = "SDL_WaitAndAcquireGPUSwapchainTexture";

    // Waits for a swapchain texture, which paces the frame loop to the display.
    private unsafe bool AcquireSwapchainTexture(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture** texture, uint* width, uint* height)
    {
        return SDL_WaitAndAcquireGPUSwapchainTexture(commandBuffer, SdlWindow, (Pointer<SDL_GPUTexture>*)texture, width, height);
    }
}
#endif
