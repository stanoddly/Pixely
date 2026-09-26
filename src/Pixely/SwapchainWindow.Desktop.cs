#if !BROWSER
using SDL;

namespace Pixely;

public sealed partial class SwapchainWindow
{
    private const string AcquireSwapchainTextureCall = "SDL_WaitAndAcquireGPUSwapchainTexture";

    // Waits for a swapchain texture, which paces the frame loop to the display.
    private unsafe bool AcquireSwapchainTexture(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture** texture, uint* width, uint* height)
    {
        return SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(commandBuffer, SdlWindow, texture, width, height);
    }
}
#endif
