using Pixely.Gpu;
using Pixely.ShaderCommon;
using SDL;

namespace Pixely.Testing;

/// <summary>
/// A command buffer of a <see cref="StubGpuDevice"/>: it keeps the passes and blits recorded into it, in order. Uploads into it
/// reach the device's contents when it is submitted, and are dropped when it is cancelled.
/// </summary>
public sealed class StubCommandBuffer : CommandBuffer
{
    private readonly StubGpuDevice _gpuDevice;
    private readonly List<StubRenderPass> _renderPasses = new();
    private readonly List<StubComputePass> _computePasses = new();
    private readonly List<StubBlit> _blits = new();
    private readonly List<StubUpload> _uploads = new();

    internal StubCommandBuffer(StubGpuDevice gpuDevice)
    {
        _gpuDevice = gpuDevice;
    }

    public IReadOnlyList<StubRenderPass> RenderPasses => _renderPasses;

    public IReadOnlyList<StubComputePass> ComputePasses => _computePasses;

    public IReadOnlyList<StubBlit> Blits => _blits;

    public bool IsSubmitted { get; private set; }

    public bool IsCancelled { get; private set; }

    // The uniforms pushed so far, per stage and slot; a draw or dispatch keeps the ones current when it is recorded.
    internal StubUniforms VertexUniforms { get; private set; } = StubUniforms.Empty;

    internal StubUniforms FragmentUniforms { get; private set; } = StubUniforms.Empty;

    internal StubUniforms ComputeUniforms { get; private set; } = StubUniforms.Empty;

    internal IReadOnlyList<StubUpload> Uploads => _uploads;

    internal void AddUpload(StubUpload upload) => _uploads.Add(upload);

    internal override void SubmitCore()
    {
        IsSubmitted = true;
        _gpuDevice.OnSubmitted(this);
    }

    internal override GpuFence SubmitAndAcquireFenceCore()
    {
        SubmitCore();
        return new GpuFence(_gpuDevice, _gpuDevice.NextHandle<SDL_GPUFence>());
    }

    internal override void SubmitAndDownloadTextureCore(Texture texture, byte[] pixels)
    {
        SubmitCore();
        _gpuDevice.ReadTexture(texture).CopyTo(pixels.AsSpan());
    }

    internal override void PushVertexUniformDataCore(uint slot, ReadOnlySpan<byte> data) => VertexUniforms = VertexUniforms.With(slot, data);

    internal override void PushFragmentUniformDataCore(uint slot, ReadOnlySpan<byte> data) => FragmentUniforms = FragmentUniforms.With(slot, data);

    internal override void PushComputeUniformDataCore(uint slot, ReadOnlySpan<byte> data) => ComputeUniforms = ComputeUniforms.With(slot, data);

    internal override RenderPass CreateRenderPassCore(in RenderPassTargets targets, DepthBufferFormat depthBufferFormat, SampleCount sampleCount, ShortSize targetSize)
    {
        StubRenderPass renderPass = new(this, targets, depthBufferFormat, sampleCount, targetSize);
        _renderPasses.Add(renderPass);
        return renderPass;
    }

    internal override ComputePass CreateComputePassCore(
        ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers,
        StorageBufferElementSizes readWriteStorageBufferElementSizes)
    {
        StubComputePass computePass = new(this, readWriteStorageTextures, readWriteStorageBuffers, readWriteStorageBufferElementSizes);
        _computePasses.Add(computePass);
        return computePass;
    }

    internal override CopyPass BeginCopyPassCore(UploadTransferBuffer transferBuffer) => new StubCopyPass(_gpuDevice, this);

    internal override void BlitTexturesCore(Texture source, Texture destination) => _blits.Add(new StubBlit(source, destination));

    internal override void CancelCore()
    {
        IsCancelled = true;
        _uploads.Clear();
    }
}

/// <summary>A blit from one texture to another, whole source onto whole destination.</summary>
public readonly record struct StubBlit(Texture Source, Texture Destination);

// An upload recorded into a stub copy pass: the whole contents of a buffer from its start, or one layer of a texture.
internal readonly record struct StubUpload(bool IsTexture, nint Handle, uint Layer, byte[] Data);
