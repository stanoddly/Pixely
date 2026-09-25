using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

/// <summary>
/// Provides a <see cref="BasicRenderContext"/>, or a context derived from it, for every frame without allocating one: it acquires
/// the command buffer and the swapchain texture, gives the command buffer up when the acquire fails or throws, and hands the
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
        // The context comes first: once the swapchain texture is acquired, a failure can no longer cancel the command buffer.
        // The coordinator disposes each context before asking for the next, so the reusable one is free. Were it not, the spare
        // created here is dropped when the acquire fails, which loses nothing: a context's Dispose only submits its command buffer.
        TRenderContext context = _reusableContext is { IsInUse: false } ? _reusableContext : CreateRenderContext();
        _reusableContext ??= context;

        CommandBuffer commandBuffer = _gpuDevice.AcquireCommandBuffer();
        SwapchainTexture swapchainTexture;
        try
        {
            if (!window.TryWaitAndAcquireSwapchainTexture(commandBuffer, out swapchainTexture))
            {
                commandBuffer.CancelOrSubmit();
                renderContext = null;
                return false;
            }
        }
        catch
        {
            // A window override can throw after its base implementation acquired the texture, and then only a submit is valid.
            commandBuffer.CancelOrSubmit();
            throw;
        }

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
