using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public class BasicRenderContextProvider : RenderContextProvider<BasicRenderContext>
{
    private readonly GpuDevice _gpuDevice;

    // Handed out again once the previous frame disposed it, the way the command buffer inside it is.
    private BasicRenderContext? _reusableContext;

    internal BasicRenderContextProvider(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out BasicRenderContext? renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        SwapchainTexture swapchainTexture;
        try
        {
            if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out swapchainTexture))
            {
                commandBuffer.Dispose();
                renderContext = null;
                return false;
            }
        }
        catch
        {
            // Nothing was acquired, so cancelling is valid, and it returns the command buffer to the pool.
            commandBuffer.Cancel();
            throw;
        }

        if (_reusableContext is { IsInUse: false })
        {
            _reusableContext.Reuse(swapchainTexture, commandBuffer);
            renderContext = _reusableContext;
            return true;
        }

        renderContext = new BasicRenderContext(swapchainTexture, commandBuffer);
        _reusableContext ??= renderContext;
        return true;
    }
}
