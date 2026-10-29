using System.Collections.Immutable;
using Pixely.Gpu;

namespace Pixely.Testing;

/// <summary>
/// One draw of a <see cref="StubRenderPass"/> with the state current when it was recorded. Bindings are by slot; a slot
/// bound earlier in the pass and not rebound keeps its binding, as on the GPU.
/// </summary>
public sealed class StubDraw
{
    internal StubDraw()
    {
    }

    public GraphicsPipeline Pipeline { get; internal init; } = null!;

    public IReadOnlyDictionary<uint, GpuVertexBuffer> VertexBuffers { get; internal init; } = ImmutableDictionary<uint, GpuVertexBuffer>.Empty;

    /// <summary>The bound index buffer of an indexed draw, null for a draw that is not indexed.</summary>
    public GpuIndexBuffer? IndexBuffer { get; internal init; }

    public bool IsIndexed { get; internal init; }

    /// <summary>The vertices a draw that is not indexed draws, the size of the vertex buffer in slot 0 when it was bound; 0 for an indexed draw.</summary>
    public uint VertexCount { get; internal init; }

    /// <summary>The indices an indexed draw draws; 0 for a draw that is not indexed.</summary>
    public uint IndexCount { get; internal init; }

    public uint FirstIndex { get; internal init; }

    public uint InstanceCount { get; internal init; }

    public IReadOnlyDictionary<uint, StubSamplerBinding> VertexSamplers { get; internal init; } = ImmutableDictionary<uint, StubSamplerBinding>.Empty;

    public IReadOnlyDictionary<uint, StubSamplerBinding> FragmentSamplers { get; internal init; } = ImmutableDictionary<uint, StubSamplerBinding>.Empty;

    public IReadOnlyDictionary<uint, GpuStorageBuffer> VertexStorageBuffers { get; internal init; } = ImmutableDictionary<uint, GpuStorageBuffer>.Empty;

    public IReadOnlyDictionary<uint, GpuStorageBuffer> FragmentStorageBuffers { get; internal init; } = ImmutableDictionary<uint, GpuStorageBuffer>.Empty;

    /// <summary>The scissor clipped to the pass's target, null when the pass never set one.</summary>
    public Rectangle? Scissor { get; internal init; }

    public byte StencilReference { get; internal init; }

    internal StubUniforms VertexUniforms { get; init; } = StubUniforms.Empty;

    internal StubUniforms FragmentUniforms { get; init; } = StubUniforms.Empty;

    public bool HasVertexUniform(uint slot) => VertexUniforms.Contains(slot);

    public bool HasFragmentUniform(uint slot) => FragmentUniforms.Contains(slot);

    /// <summary>The value last pushed to the vertex uniform slot before the draw, which must be a <typeparamref name="T"/> by size.</summary>
    public T ReadVertexUniform<T>(uint slot) where T : unmanaged => VertexUniforms.Read<T>(slot, "Vertex");

    /// <summary>The value last pushed to the fragment uniform slot before the draw, which must be a <typeparamref name="T"/> by size.</summary>
    public T ReadFragmentUniform<T>(uint slot) where T : unmanaged => FragmentUniforms.Read<T>(slot, "Fragment");
}
