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

    public override bool TryCreateRenderContext(Window window, [MaybeNullWhen(false)] out BasicRenderContext renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        if (!window.TryWaitAndAcquireSwapchainTexture(ref commandBuffer, out SwapchainTexture swapchainTexture))
        {
            commandBuffer.Dispose();
            renderContext = default;
            return false;
        }

        renderContext = new BasicRenderContext(swapchainTexture, commandBuffer);
        return true;
    }
}
