#if BROWSER
using SDL;

namespace Pixely;

public sealed partial class SwapchainWindow
{
    private const string AcquireSwapchainTextureCall = "SDL_AcquireGPUSwapchainTexture";

    // The waiting form spins on SDL_DelayNS, which in the browser suspends the wasm stack, and a managed frame cannot be in
    // the suspended region. The page paces frames from requestAnimationFrame anyway, so a frame with no texture ready is skipped.
    private unsafe bool AcquireSwapchainTexture(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture** texture, uint* width, uint* height)
    {
        return SDL3.SDL_AcquireGPUSwapchainTexture(commandBuffer, SdlWindow, texture, width, height);
    }
}
#endif
