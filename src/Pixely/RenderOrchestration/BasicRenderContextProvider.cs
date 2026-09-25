using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

/// <summary>
/// Provides a <see cref="BasicRenderContext"/>, or a context derived from it, for every frame without allocating one: it acquires
/// the command buffer and the swapchain texture, cancels the command buffer when the acquire fails or throws, and hands the
/// same context out again once the previous frame disposed it. A derived provider says how the context is created and what
/// it needs each frame.
/// </summary>
public abstract class BasicRenderContextProvider<TRenderContext> : RenderContextProvider<TRenderContext>
    where TRenderContext : BasicRenderContext
{
    private readonly GpuDevice _gpuDevice;
    private TRenderContext? _reusableContext;

    protected BasicRenderContextProvider(GpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public sealed override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out TRenderContext? renderContext)
    {
        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        SwapchainTexture swapchainTexture;
        try
        {
            if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out swapchainTexture))
            {
                commandBuffer.Cancel();
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

        TRenderContext context = _reusableContext is { IsInUse: false } ? _reusableContext : CreateRenderContext();
        _reusableContext ??= context;
        context.Begin(swapchainTexture, commandBuffer);

        try
        {
            PrepareRenderContext(context, window);
        }
        catch
        {
            // The swapchain texture is acquired, so the command buffer can only be submitted, not cancelled.
            context.Dispose();
            throw;
        }

        renderContext = context;
        return true;
    }

    /// <summary>Creates the context. Called once, and again only when a context is asked for while the previous one is not disposed yet.</summary>
    protected abstract TRenderContext CreateRenderContext();

    /// <summary>Sets what the context needs for this frame, such as the render size or a camera. Runs after the command buffer and swapchain texture are set.</summary>
    protected virtual void PrepareRenderContext(TRenderContext renderContext, Window window)
    {
    }
}

public sealed class BasicRenderContextProvider : BasicRenderContextProvider<BasicRenderContext>
{
    internal BasicRenderContextProvider(GpuDevice gpuDevice)
        : base(gpuDevice)
    {
    }

    protected override BasicRenderContext CreateRenderContext()
    {
        return new BasicRenderContext();
    }
}
