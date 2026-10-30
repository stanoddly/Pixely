using System.Collections.Immutable;
using Pixely.Gpu;
using Pixely.ShaderCommon;

namespace Pixely.Testing;

/// <summary>
/// A compute pass of a <see cref="StubCommandBuffer"/>: the read-write bindings it began with and the dispatches recorded into
/// it, each with the state it was dispatched with. Only calls that passed Pixely's validation are recorded.
/// </summary>
public sealed class StubComputePass : ComputePass
{
    private readonly StubCommandBuffer _commandBuffer;
    private readonly List<StubDispatch> _dispatches = new();

    private ComputePipeline? _pipeline;
    private ImmutableDictionary<uint, StubSamplerBinding> _samplers = ImmutableDictionary<uint, StubSamplerBinding>.Empty;
    private ImmutableDictionary<uint, Texture> _readOnlyStorageTextures = ImmutableDictionary<uint, Texture>.Empty;
    private ImmutableDictionary<uint, GpuStorageBuffer> _readOnlyStorageBuffers = ImmutableDictionary<uint, GpuStorageBuffer>.Empty;

    internal StubComputePass(
        StubCommandBuffer commandBuffer,
        ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers,
        StorageBufferElementSizes readWriteStorageBufferElementSizes)
        : base((uint)readWriteStorageTextures.Length, (uint)readWriteStorageBuffers.Length, readWriteStorageBufferElementSizes)
    {
        _commandBuffer = commandBuffer;
        ReadWriteStorageTextures = readWriteStorageTextures.ToArray();
        ReadWriteStorageBuffers = readWriteStorageBuffers.ToArray();
    }

    public IReadOnlyList<StorageTextureReadWriteBinding> ReadWriteStorageTextures { get; }

    public IReadOnlyList<StorageBufferReadWriteBinding> ReadWriteStorageBuffers { get; }

    public IReadOnlyList<StubDispatch> Dispatches => _dispatches;

    public bool IsEnded { get; private set; }

    internal override void BindComputePipelineCore(ComputePipeline pipeline) => _pipeline = pipeline;

    internal override void BindSamplersCore(uint slot, ReadOnlySpan<Texture> textures, Sampler sampler) =>
        _samplers = StubBindings.BindSamplers(_samplers, slot, textures, sampler);

    internal override void BindReadOnlyStorageTexturesCore(uint slot, ReadOnlySpan<Texture> textures) =>
        _readOnlyStorageTextures = StubBindings.Bind(_readOnlyStorageTextures, slot, textures);

    internal override void BindReadOnlyStorageBuffersCore(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers) =>
        _readOnlyStorageBuffers = StubBindings.Bind(_readOnlyStorageBuffers, slot, buffers);

    internal override void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ) =>
        _dispatches.Add(CreateDispatch(groupCountX, groupCountY, groupCountZ, null, 0));

    internal override void DispatchIndirectCore(GpuStorageBuffer buffer, uint offset) =>
        _dispatches.Add(CreateDispatch(0, 0, 0, buffer, offset));

    internal override void EndCore() => IsEnded = true;

    // The validation requires a bound pipeline before any dispatch.
    private StubDispatch CreateDispatch(uint groupCountX, uint groupCountY, uint groupCountZ, GpuStorageBuffer? indirectBuffer, uint indirectOffset) => new()
    {
        Pipeline = _pipeline!,
        GroupCountX = groupCountX,
        GroupCountY = groupCountY,
        GroupCountZ = groupCountZ,
        IndirectBuffer = indirectBuffer,
        IndirectOffset = indirectOffset,
        Samplers = _samplers,
        ReadOnlyStorageTextures = _readOnlyStorageTextures,
        ReadOnlyStorageBuffers = _readOnlyStorageBuffers,
        Uniforms = _commandBuffer.ComputeUniforms
    };
}

/// <summary>One dispatch of a <see cref="StubComputePass"/> with the state current when it was recorded.</summary>
public sealed class StubDispatch
{
    internal StubDispatch()
    {
    }

    public ComputePipeline Pipeline { get; internal init; } = null!;

    /// <summary>The group counts of a direct dispatch, 0 for an indirect one, whose counts the GPU reads from <see cref="IndirectBuffer"/>.</summary>
    public uint GroupCountX { get; internal init; }

    public uint GroupCountY { get; internal init; }

    public uint GroupCountZ { get; internal init; }

    /// <summary>The buffer an indirect dispatch reads its group counts from, null for a direct dispatch.</summary>
    public GpuStorageBuffer? IndirectBuffer { get; internal init; }

    public uint IndirectOffset { get; internal init; }

    public IReadOnlyDictionary<uint, StubSamplerBinding> Samplers { get; internal init; } = ImmutableDictionary<uint, StubSamplerBinding>.Empty;

    public IReadOnlyDictionary<uint, Texture> ReadOnlyStorageTextures { get; internal init; } = ImmutableDictionary<uint, Texture>.Empty;

    public IReadOnlyDictionary<uint, GpuStorageBuffer> ReadOnlyStorageBuffers { get; internal init; } = ImmutableDictionary<uint, GpuStorageBuffer>.Empty;

    internal StubUniforms Uniforms { get; init; } = StubUniforms.Empty;

    public bool HasUniform(uint slot) => Uniforms.Contains(slot);

    /// <summary>The value last pushed to the compute uniform slot before the dispatch, which must be a <typeparamref name="T"/> by size.</summary>
    public T ReadUniform<T>(uint slot) where T : unmanaged => Uniforms.Read<T>(slot, "Compute");
}
