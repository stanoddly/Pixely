using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

/// <summary>
/// What renderers draw one frame of a window with. The render coordinator owns <see cref="CommandBuffer"/>: it submits the
/// pending uploads and then the command buffer after disposing the context. A context or renderer must not submit or cancel
/// it; a context that submits in <see cref="IDisposable.Dispose"/> sends the frame before its uploads, and the coordinator's
/// own submit then throws <see cref="ObjectDisposedException"/>.
/// </summary>
public interface IRenderContext : IDisposable
{
    CommandBuffer CommandBuffer { get; }
    Texture ColorTarget { get; }
}
