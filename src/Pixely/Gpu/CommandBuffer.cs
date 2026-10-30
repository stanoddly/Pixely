using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using Pixely.Content;
using Pixely.ShaderCommon;

namespace Pixely.Gpu;

/// <summary>
/// Records GPU work until it is submitted or cancelled. The device that acquired it backs it, so its validation runs the same
/// whether SDL GPU or a test's stub device records the work.
/// </summary>
public abstract class CommandBuffer: IDisposable
{
    private ShaderUniformSlotSizes _fragmentShaderUniformSlotSizes;
    private ShaderUniformSlotSizes _vertexShaderUniformSlotSizes;
    private bool _isFinished;

    public ShaderUniformSlotSizes FragmentShaderUniformSlotSizes => _fragmentShaderUniformSlotSizes;
    public ShaderUniformSlotSizes VertexShaderUniformSlotSizes => _vertexShaderUniformSlotSizes;

    internal CommandBuffer()
    {
    }

    public void Submit()
    {
        ThrowIfDisposed();
        _isFinished = true;
        SubmitCore();
    }

    /// <summary>
    /// Submits the recorded work, waits for the GPU to finish it and returns the pixels of the texture's first layer, tightly packed.
    /// Not in the browser: mapping a download buffer and waiting for its fence both suspend the wasm stack under a managed frame.
    /// </summary>
    [UnsupportedOSPlatform("browser")]
    public Image SubmitAndDownloadTexture(Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ThrowIfDisposed();
        texture.ThrowIfDisposed();
#if BROWSER
        throw new PlatformNotSupportedException("Downloading a texture is not supported in the browser.");
#else
        if (texture.SampleCount != SampleCount.Count1)
        {
            throw new ArgumentException($"A texture with {texture.SampleCount} cannot be downloaded. Resolve it into a texture with one sample and download that.", nameof(texture));
        }

        PixelFormat pixelFormat = texture.Format.ToPixelFormat();
        long layerSizeInBytes = texture.Format.CalculateSizeInBytes(texture.Size.Width, texture.Size.Height);
        if (layerSizeInBytes > int.MaxValue)
        {
            throw new NotSupportedException($"A {texture.Size.Width}x{texture.Size.Height} {texture.Format} layer is too large to download into one array.");
        }

        byte[] pixels = new byte[layerSizeInBytes];
        _isFinished = true;
        SubmitAndDownloadTextureCore(texture, pixels);
        return new RawImage(pixels, texture.Size, pixelFormat);
#endif
    }

    public GpuFence SubmitAndAcquireFence()
    {
        ThrowIfDisposed();
        _isFinished = true;
        return SubmitAndAcquireFenceCore();
    }

    public void PushFragmentUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        ThrowIfDisposed();

        AssignSlot(ref _fragmentShaderUniformSlotSizes, slot, Unsafe.SizeOf<TType>());
        PushFragmentUniformDataCore(slot, MemoryMarshal.AsBytes(new ReadOnlySpan<TType>(in variable)));
    }

    public void PushVertexUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        ThrowIfDisposed();

        AssignSlot(ref _vertexShaderUniformSlotSizes, slot, Unsafe.SizeOf<TType>());
        PushVertexUniformDataCore(slot, MemoryMarshal.AsBytes(new ReadOnlySpan<TType>(in variable)));
    }

    /// <summary>
    /// Begins a render pass. <paramref name="resolveTextures"/> is either empty or has one entry per color target: the texture with one
    /// sample that a multisampled color target resolves into, or null for a target that does not resolve.
    /// </summary>
    public RenderPass CreateRenderPass(
        ReadOnlySpan<Texture> colorTargets,
        ReadOnlySpan<ColorTargetSettings> colorTargetSettings,
        Texture? depthBuffer,
        DepthBufferSettings depthBufferSettings,
        ReadOnlySpan<Texture?> resolveTextures = default)
    {
        ThrowIfDisposed();

        if (!resolveTextures.IsEmpty && resolveTextures.Length != colorTargets.Length)
        {
            throw new ArgumentException($"There are {resolveTextures.Length} resolve textures for {colorTargets.Length} color targets. Pass one per color target, or none.", nameof(resolveTextures));
        }

        SampleCount sampleCount = ValidateSampleCounts(colorTargets, depthBuffer);
        if (depthBuffer != null)
        {
            ValidateDepthBufferStoreOperations(depthBufferSettings);
        }

        for (int i = 0; i < colorTargets.Length; i++)
        {
            Texture? resolveTexture = resolveTextures.IsEmpty ? null : resolveTextures[i];
            ValidateResolve(i, colorTargets[i], colorTargetSettings[i].StoreOperation, resolveTexture);
        }

        DepthBufferFormat depthBufferFormat = depthBuffer != null
            ? (DepthBufferFormat)depthBuffer.Format
            : DepthBufferFormat.None;

        RenderPassTargets targets = new(
            colorTargets,
            colorTargetSettings[..colorTargets.Length],
            depthBuffer,
            depthBufferSettings,
            resolveTextures);

        return CreateRenderPassCore(targets, depthBufferFormat, sampleCount, CalculateTargetSize(colorTargets, depthBuffer));
    }

    // Every backend needs the attachments of a pass to share one sample count, which the pass's pipelines must also have.
    internal static SampleCount ValidateSampleCounts(ReadOnlySpan<Texture> colorTargets, Texture? depthBuffer)
    {
        SampleCount sampleCount = colorTargets.IsEmpty ? depthBuffer?.SampleCount ?? SampleCount.Count1 : colorTargets[0].SampleCount;

        for (int i = 1; i < colorTargets.Length; i++)
        {
            if (colorTargets[i].SampleCount != sampleCount)
            {
                throw new InvalidOperationException(
                    $"All attachments of a render pass need the same sample count, but color target 0 has {sampleCount} and color target {i} has {colorTargets[i].SampleCount}.");
            }
        }

        if (depthBuffer != null && depthBuffer.SampleCount != sampleCount)
        {
            throw new InvalidOperationException(
                $"All attachments of a render pass need the same sample count, but the color targets have {sampleCount} and the depth buffer has {depthBuffer.SampleCount}.");
        }

        return sampleCount;
    }

    // SDL has no resolve for depth-stencil targets.
    internal static void ValidateDepthBufferStoreOperations(DepthBufferSettings settings)
    {
        if (settings.DepthBufferStoreOperation is StoreOperation.Resolve or StoreOperation.ResolveAndStore
            || settings.StencilStoreOperation is StoreOperation.Resolve or StoreOperation.ResolveAndStore)
        {
            throw new InvalidOperationException(
                $"A depth-stencil buffer cannot be resolved, but its store operations are {settings.DepthBufferStoreOperation} for depth and {settings.StencilStoreOperation} for stencil.");
        }
    }

    internal static void ValidateResolve(int index, Texture colorTarget, StoreOperation storeOperation, Texture? resolveTexture)
    {
        bool resolves = storeOperation is StoreOperation.Resolve or StoreOperation.ResolveAndStore;

        if (resolves && resolveTexture == null)
        {
            throw new InvalidOperationException($"Color target {index} has the store operation {storeOperation} but no resolve texture.");
        }

        if (resolveTexture == null)
        {
            return;
        }

        if (!resolves)
        {
            throw new InvalidOperationException(
                $"Color target {index} has a resolve texture but the store operation {storeOperation}. Use {nameof(StoreOperation.Resolve)} or {nameof(StoreOperation.ResolveAndStore)}.");
        }

        if (colorTarget.SampleCount == SampleCount.Count1)
        {
            throw new InvalidOperationException($"Color target {index} has one sample, so there is nothing to resolve. Only a multisampled color target can be resolved.");
        }

        if ((resolveTexture.Usage & TextureUsage.ColorTarget) == 0)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} has the usage '{resolveTexture.Usage}', but a resolve texture needs {nameof(TextureUsage.ColorTarget)}.");
        }

        if (resolveTexture.SampleCount != SampleCount.Count1)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} has {resolveTexture.SampleCount}, but a resolve texture needs one sample.");
        }

        if (resolveTexture.Format != colorTarget.Format)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} has the format {resolveTexture.Format}, but the color target has {colorTarget.Format}.");
        }

        if (resolveTexture.Size != colorTarget.Size)
        {
            throw new InvalidOperationException($"The resolve texture of color target {index} is {resolveTexture.Size.Width}x{resolveTexture.Size.Height}, but the color target is {colorTarget.Size.Width}x{colorTarget.Size.Height}.");
        }
    }

    // A pass can only safely address the area every attachment shares, so the scissor bounds
    // are the smallest attachment, depth included.
    private static ShortSize CalculateTargetSize(ReadOnlySpan<Texture> colorTargets, Texture? depthBuffer)
    {
        ushort width = ushort.MaxValue;
        ushort height = ushort.MaxValue;

        foreach (Texture colorTarget in colorTargets)
        {
            width = Math.Min(width, colorTarget.Size.Width);
            height = Math.Min(height, colorTarget.Size.Height);
        }

        if (depthBuffer != null)
        {
            width = Math.Min(width, depthBuffer.Size.Width);
            height = Math.Min(height, depthBuffer.Size.Height);
        }

        return new ShortSize(width, height);
    }

    public void PushComputeUniformData<TType>(uint slot, TType variable) where TType : unmanaged
    {
        ThrowIfDisposed();
        ThrowIfInvalidSlot(slot);
        PushComputeUniformDataCore(slot, MemoryMarshal.AsBytes(new ReadOnlySpan<TType>(in variable)));
    }

    public ComputePass CreateComputePass(
        ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers)
    {
        ThrowIfDisposed();
        for (int i = 0; i < readWriteStorageBuffers.Length; i++)
        {
            readWriteStorageBuffers[i].Buffer.ValidateUsage("Compute read-write storage buffer", (uint)i, StorageBufferUsage.ComputeStorageWrite);
        }

        return CreateComputePassCore(readWriteStorageTextures, readWriteStorageBuffers, BuildStorageBufferElementSizes(readWriteStorageBuffers));
    }

    public ComputePass CreateComputePass()
    {
        return CreateComputePass(
            ReadOnlySpan<StorageTextureReadWriteBinding>.Empty,
            ReadOnlySpan<StorageBufferReadWriteBinding>.Empty);
    }

    private static StorageBufferElementSizes BuildStorageBufferElementSizes(ReadOnlySpan<StorageBufferReadWriteBinding> buffers)
    {
        StorageBufferElementSizes sizes = default;
        for (int i = 0; i < buffers.Length && i < 4; i++)
        {
            ushort elementSize = (ushort)buffers[i].Buffer.ElementSize;
            sizes = i switch
            {
                0 => sizes with { Slot0 = elementSize },
                1 => sizes with { Slot1 = elementSize },
                2 => sizes with { Slot2 = elementSize },
                3 => sizes with { Slot3 = elementSize },
                _ => sizes
            };
        }
        return sizes;
    }

    private void AssignSlot(ref ShaderUniformSlotSizes slotSizes, uint slot, int size)
    {
        ThrowIfInvalidSlot(slot);

        byte byteSize = (byte)size;

        if (slot == 0)
        {
            slotSizes.Slot0 = byteSize;
        }

        if (slot == 1)
        {
            slotSizes.Slot1 = byteSize;
        }

        if (slot == 2)
        {
            slotSizes.Slot2 = byteSize;
        }

        if (slot == 3)
        {
            slotSizes.Slot3 = byteSize;
        }
    }

    public void BlitTextures(Texture source, Texture destination)
    {
        ThrowIfDisposed();
        BlitTexturesCore(source, destination);
    }

    public void Cancel()
    {
        if (!_isFinished)
        {
            _isFinished = true;
            CancelCore();
        }
    }

    public void Dispose()
    {
        Cancel();
    }

    // SDL GPU has four uniform slots per stage.
    private static void ThrowIfInvalidSlot(uint slot)
    {
        if (slot > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "Slot must be between 0 and 3.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isFinished)
        {
            throw new ObjectDisposedException(nameof(CommandBuffer));
        }
    }

    // The backend. The command buffer is open whenever one of these runs, except that the submits and the cancel run once it
    // has been marked finished.
    internal abstract void SubmitCore();
    internal abstract GpuFence SubmitAndAcquireFenceCore();
#if !BROWSER
    // Records the download, submits, waits and fills pixels with the texture's first layer. A failure before the submit
    // cancels the command buffer.
    internal abstract void SubmitAndDownloadTextureCore(Texture texture, byte[] pixels);
#endif
    internal abstract void PushVertexUniformDataCore(uint slot, ReadOnlySpan<byte> data);
    internal abstract void PushFragmentUniformDataCore(uint slot, ReadOnlySpan<byte> data);
    internal abstract void PushComputeUniformDataCore(uint slot, ReadOnlySpan<byte> data);
    internal abstract RenderPass CreateRenderPassCore(in RenderPassTargets targets, DepthBufferFormat depthBufferFormat, SampleCount sampleCount, ShortSize targetSize);
    internal abstract ComputePass CreateComputePassCore(
        ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers,
        StorageBufferElementSizes readWriteStorageBufferElementSizes);
    // The copy pass GpuMemorySystem records its uploads into. The transfer buffer outlives the pass, see UploadTransferBuffer.
    internal abstract CopyPass BeginCopyPassCore(UploadTransferBuffer transferBuffer);
    internal abstract void BlitTexturesCore(Texture source, Texture destination);
    internal abstract void CancelCore();
}

// The validated attachments of a render pass, one color target setting per color target and resolve textures either empty
// or one per color target.
internal readonly ref struct RenderPassTargets
{
    public ReadOnlySpan<Texture> ColorTargets { get; }
    public ReadOnlySpan<ColorTargetSettings> ColorTargetSettings { get; }
    public Texture? DepthBuffer { get; }
    public DepthBufferSettings DepthBufferSettings { get; }
    public ReadOnlySpan<Texture?> ResolveTextures { get; }

    public RenderPassTargets(
        ReadOnlySpan<Texture> colorTargets,
        ReadOnlySpan<ColorTargetSettings> colorTargetSettings,
        Texture? depthBuffer,
        DepthBufferSettings depthBufferSettings,
        ReadOnlySpan<Texture?> resolveTextures)
    {
        ColorTargets = colorTargets;
        ColorTargetSettings = colorTargetSettings;
        DepthBuffer = depthBuffer;
        DepthBufferSettings = depthBufferSettings;
        ResolveTextures = resolveTextures;
    }
}
