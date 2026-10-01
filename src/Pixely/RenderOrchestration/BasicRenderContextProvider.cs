using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public class BasicRenderContextProvider : RenderContextProvider<BasicRenderContext>
{
    private readonly GpuDevice _gpuDevice;

    internal BasicRenderContextProvider(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out BasicRenderContext? renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out SwapchainTexture swapchainTexture))
        {
            // Submitted, not cancelled: SDL's Vulkan backend frees finished GPU work on a submit that requested a swapchain texture.
            commandBuffer.Submit();
            renderContext = null;
            return false;
        }

        renderContext = new BasicRenderContext(swapchainTexture, commandBuffer);
        return true;
    }
}
