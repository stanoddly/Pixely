using System.Diagnostics.CodeAnalysis;
using Pixely.Gpu;

namespace Pixely.RenderOrchestration;

/// <summary>
/// What a frame renders with. Implement it as a <c>ref struct</c> that holds its <see cref="Gpu.CommandBuffer"/>, and return
/// that command buffer by <c>ref</c>, so that renderers record on the one instance the context submits.
/// </summary>
public interface IRenderContext : IDisposable
{
    [UnscopedRef]
    ref CommandBuffer CommandBuffer { get; }

    Texture ColorTarget { get; }
}
