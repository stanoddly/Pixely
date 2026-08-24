using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

internal sealed class DefaultRenderContextProvider : IRenderContextProvider<DefaultRenderContext>
{
    private readonly GpuDevice _gpuDevice;

    internal DefaultRenderContextProvider(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out DefaultRenderContext? renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out SwapchainTexture swapchainTexture))
        {
            commandBuffer.Dispose();
            renderContext = null;
            return false;
        }

        renderContext = new DefaultRenderContext(swapchainTexture, commandBuffer);
        return true;
    }
}
