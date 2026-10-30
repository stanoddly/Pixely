using System.Collections.Immutable;
using Pixely.Gpu;

namespace Pixely.Testing;

/// <summary>
/// A render pass of a <see cref="StubCommandBuffer"/>: its attachments and the draws recorded into it, each with the state it
/// was drawn with. Only calls that passed Pixely's validation are recorded.
/// </summary>
public sealed class StubRenderPass : RenderPass
{
    private readonly StubCommandBuffer _commandBuffer;
    private readonly List<StubDraw> _draws = new();

    private GraphicsPipeline? _pipeline;
    private ImmutableDictionary<uint, GpuVertexBuffer> _vertexBuffers = ImmutableDictionary<uint, GpuVertexBuffer>.Empty;
    private GpuIndexBuffer? _indexBuffer;
    private ImmutableDictionary<uint, StubSamplerBinding> _vertexSamplers = ImmutableDictionary<uint, StubSamplerBinding>.Empty;
    private ImmutableDictionary<uint, StubSamplerBinding> _fragmentSamplers = ImmutableDictionary<uint, StubSamplerBinding>.Empty;
    private ImmutableDictionary<uint, GpuStorageBuffer> _vertexStorageBuffers = ImmutableDictionary<uint, GpuStorageBuffer>.Empty;
    private ImmutableDictionary<uint, GpuStorageBuffer> _fragmentStorageBuffers = ImmutableDictionary<uint, GpuStorageBuffer>.Empty;
    private Rectangle? _scissor;
    private byte _stencilReference;

    internal StubRenderPass(StubCommandBuffer commandBuffer, in RenderPassTargets targets, DepthBufferFormat depthBufferFormat, SampleCount sampleCount, ShortSize targetSize)
        : base(commandBuffer, depthBufferFormat, sampleCount, targetSize)
    {
        _commandBuffer = commandBuffer;
        ColorTargets = targets.ColorTargets.ToArray();
        ColorTargetSettings = targets.ColorTargetSettings.ToArray();
        ResolveTextures = targets.ResolveTextures.IsEmpty ? new Texture?[ColorTargets.Count] : targets.ResolveTextures.ToArray();
        DepthBuffer = targets.DepthBuffer;
        DepthBufferSettings = targets.DepthBufferSettings;
    }

    public IReadOnlyList<Texture> ColorTargets { get; }

    public IReadOnlyList<ColorTargetSettings> ColorTargetSettings { get; }

    /// <summary>One entry per color target: the texture it resolves into, or null.</summary>
    public IReadOnlyList<Texture?> ResolveTextures { get; }

    public Texture? DepthBuffer { get; }

    public DepthBufferSettings DepthBufferSettings { get; }

    public IReadOnlyList<StubDraw> Draws => _draws;

    public bool IsEnded { get; private set; }

    internal override void BindGraphicsPipelineCore(GraphicsPipeline graphicsPipeline) => _pipeline = graphicsPipeline;

    internal override void BindVertexBufferCore(uint slot, GpuVertexBuffer buffer) => _vertexBuffers = _vertexBuffers.SetItem(slot, buffer);

    internal override void BindIndexBufferCore(GpuIndexBuffer buffer) => _indexBuffer = buffer;

    internal override void BindVertexSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler) =>
        _vertexSamplers = StubBindings.BindSamplers(_vertexSamplers, slot, textures, sampler);

    internal override void BindFragmentSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler) =>
        _fragmentSamplers = StubBindings.BindSamplers(_fragmentSamplers, slot, textures, sampler);

    internal override void BindVertexStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers) =>
        _vertexStorageBuffers = StubBindings.Bind(_vertexStorageBuffers, slot, buffers);

    internal override void BindFragmentStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers) =>
        _fragmentStorageBuffers = StubBindings.Bind(_fragmentStorageBuffers, slot, buffers);

    internal override void SetStencilReferenceCore(byte reference) => _stencilReference = reference;

    internal override void SetScissorCore(Rectangle scissor) => _scissor = scissor;

    internal override void DrawPrimitivesCore(uint vertexCount, uint instanceCount) =>
        _draws.Add(CreateDraw(false, vertexCount, 0, 0, instanceCount));

    internal override void DrawIndexedPrimitivesCore(uint indexCount, uint instanceCount, uint firstIndex) =>
        _draws.Add(CreateDraw(true, 0, indexCount, firstIndex, instanceCount));

    internal override void EndCore() => IsEnded = true;

    // The validation requires a bound pipeline before any draw.
    private StubDraw CreateDraw(bool isIndexed, uint vertexCount, uint indexCount, uint firstIndex, uint instanceCount) => new()
    {
        Pipeline = _pipeline!,
        VertexBuffers = _vertexBuffers,
        IndexBuffer = isIndexed ? _indexBuffer : null,
        IsIndexed = isIndexed,
        VertexCount = vertexCount,
        IndexCount = indexCount,
        FirstIndex = firstIndex,
        InstanceCount = instanceCount,
        VertexSamplers = _vertexSamplers,
        FragmentSamplers = _fragmentSamplers,
        VertexStorageBuffers = _vertexStorageBuffers,
        FragmentStorageBuffers = _fragmentStorageBuffers,
        Scissor = _scissor,
        StencilReference = _stencilReference,
        VertexUniforms = _commandBuffer.VertexUniforms,
        FragmentUniforms = _commandBuffer.FragmentUniforms
    };
}

/// <summary>A texture bound to a sampler slot together with the sampler it is read through.</summary>
public readonly record struct StubSamplerBinding(Texture Texture, Sampler Sampler);
