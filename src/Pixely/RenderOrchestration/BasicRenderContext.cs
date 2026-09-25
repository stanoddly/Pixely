using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

/// <summary>
/// The window's swapchain texture and the command buffer that draws into it. A context that needs more, such as a depth
/// target, holds a <see cref="BasicRenderContext"/> rather than deriving from it, since a <c>ref struct</c> has no derivation.
/// </summary>
public ref struct BasicRenderContext : IRenderContext
{
    private CommandBuffer _commandBuffer;

    public BasicRenderContext(SwapchainTexture swapchainTexture, CommandBuffer commandBuffer)
    {
        SwapchainTexture = swapchainTexture;
        _commandBuffer = commandBuffer;
    }

    public SwapchainTexture SwapchainTexture { get; }

    [UnscopedRef]
    public ref CommandBuffer CommandBuffer => ref _commandBuffer;

    public readonly Texture ColorTarget => SwapchainTexture;

    public void Dispose()
    {
        _commandBuffer.Submit();
    }
}
