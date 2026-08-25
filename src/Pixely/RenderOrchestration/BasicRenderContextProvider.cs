using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

internal sealed class BasicRenderContextProvider : IRenderContextProvider<BasicRenderContext>
{
    private readonly GpuDevice _gpuDevice;

    internal BasicRenderContextProvider(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out BasicRenderContext? renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out SwapchainTexture swapchainTexture))
        {
            commandBuffer.Dispose();
            renderContext = null;
            return false;
        }

        renderContext = new BasicRenderContext(swapchainTexture, commandBuffer);
        return true;
    }
}
