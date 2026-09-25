using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

public class BasicRenderContextProvider : RenderContextProvider<BasicRenderContext>
{
    private readonly GpuDevice _gpuDevice;

    // Handed out again while the device reuses frame objects, the way the command buffer inside it is.
    private BasicRenderContext? _reusableContext;

    internal BasicRenderContextProvider(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out BasicRenderContext? renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out SwapchainTexture swapchainTexture))
        {
            commandBuffer.Dispose();
            renderContext = null;
            return false;
        }

        if (!_gpuDevice.ReusesFrameObjects)
        {
            renderContext = new BasicRenderContext(swapchainTexture, commandBuffer);
            return true;
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
