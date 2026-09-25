using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// A compute pass open on a <see cref="CommandBuffer"/>. Like <see cref="RenderPass"/>, the command buffer hands out this same
/// object for every compute pass it begins, so it must not be used after it was disposed.
/// </summary>
public class ComputePass : IDisposable
{
    private readonly CommandBuffer _commandBuffer;
    private Pointer<SDL_GPUComputePass> _nativePointer;
    private uint _readWriteStorageTextureCount;
    private uint _readWriteStorageBufferCount;
    private ComputePipeline? _boundPipeline;
    private StorageBufferElementSizes _readOnlyStorageBufferElementSizes;
    private StorageBufferElementSizes _readWriteStorageBufferElementSizes;

    internal ComputePass(CommandBuffer commandBuffer)
    {
        _commandBuffer = commandBuffer;
    }

    // A reused pass starts over: no pipeline and no read-only bindings.
    internal void Begin(Pointer<SDL_GPUComputePass> nativePointer, uint readWriteStorageTextureCount, uint readWriteStorageBufferCount, StorageBufferElementSizes readWriteStorageBufferElementSizes)
    {
        _nativePointer = nativePointer;
        _readWriteStorageTextureCount = readWriteStorageTextureCount;
        _readWriteStorageBufferCount = readWriteStorageBufferCount;
        _boundPipeline = null;
        _readOnlyStorageBufferElementSizes = default;
        _readWriteStorageBufferElementSizes = readWriteStorageBufferElementSizes;
    }

    public void BindComputePipeline(ComputePipeline pipeline)
    {
        ThrowIfDisposed();
        _boundPipeline = pipeline;
        _readOnlyStorageBufferElementSizes = default;
        unsafe
        {
            SDL3.SDL_BindGPUComputePipeline(_nativePointer, pipeline.Pointer);
        }
    }

    public void BindSamplers(ReadOnlySpan<Texture> textures, Sampler sampler, uint slot = 0)
    {
        ThrowIfDisposed();
        unsafe
        {
            SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                bindings[i] = new SDL_GPUTextureSamplerBinding
                {
                    texture = textures[i].SdlGpuTexture,
                    sampler = sampler.Pointer
                };
            }
            SDL3.SDL_BindGPUComputeSamplers(_nativePointer, slot, bindings, (uint)textures.Length);
        }
    }

    public void BindReadOnlyStorageTextures(ReadOnlySpan<Texture> textures, uint slot = 0)
    {
        ThrowIfDisposed();
        unsafe
        {
            SDL_GPUTexture** sdlTextures = stackalloc SDL_GPUTexture*[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                sdlTextures[i] = textures[i].SdlGpuTexture;
            }
            SDL3.SDL_BindGPUComputeStorageTextures(_nativePointer, slot, sdlTextures, (uint)textures.Length);
        }
    }

    public void BindReadOnlyStorageTexture(Texture texture, uint slot = 0)
    {
        ReadOnlySpan<Texture> textures = [texture];
        BindReadOnlyStorageTextures(textures, slot);
    }

    public void BindReadOnlyStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0)
    {
        ThrowIfDisposed();
        for (int i = 0; i < buffers.Length; i++)
        {
            _readOnlyStorageBufferElementSizes = SetStorageBufferSlotSize(_readOnlyStorageBufferElementSizes, slot + (uint)i, (ushort)buffers[i].ElementSize);
        }
        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];
            for (int i = 0; i < buffers.Length; i++)
            {
                sdlBuffers[i] = buffers[i].SdlBuffer;
            }
            SDL3.SDL_BindGPUComputeStorageBuffers(_nativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
    }

    public void BindReadOnlyStorageBuffer(GpuStorageBuffer buffer, uint slot = 0)
    {
        ReadOnlySpan<GpuStorageBuffer> buffers = [buffer];
        BindReadOnlyStorageBuffers(buffers, slot);
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        ThrowIfDisposed();
        ThrowIfInvalidDispatch();
        unsafe
        {
            SDL3.SDL_DispatchGPUCompute(_nativePointer, groupCountX, groupCountY, groupCountZ);
        }
    }

    public void DispatchIndirect(GpuStorageBuffer buffer, uint offset = 0)
    {
        ThrowIfDisposed();
        ThrowIfInvalidDispatch();
        unsafe
        {
            SDL3.SDL_DispatchGPUComputeIndirect(_nativePointer, buffer.SdlBuffer, offset);
        }
    }

    public void Dispose()
    {
        if (!_nativePointer.IsNull)
        {
            unsafe
            {
                SDL3.SDL_EndGPUComputePass(_nativePointer);
            }
            _nativePointer = Pointer<SDL_GPUComputePass>.Null;
            _commandBuffer.OnPassEnded(this);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_nativePointer.IsNull)
        {
            throw new ObjectDisposedException(nameof(ComputePass));
        }
    }

    private void ThrowIfInvalidDispatch()
    {
        if (_boundPipeline == null)
        {
            throw new InvalidOperationException("ComputePipeline must be bound before dispatching.");
        }

        uint declaredTextures = _boundPipeline.BindingLayout.BindingCounts.NumReadWriteStorageTextures;
        if (_readWriteStorageTextureCount != declaredTextures)
        {
            throw new InvalidOperationException(
                $"Read-write storage texture count mismatch: compute pass was created with {_readWriteStorageTextureCount} but pipeline declares {declaredTextures}.");
        }

        uint declaredBuffers = _boundPipeline.BindingLayout.BindingCounts.NumReadWriteStorageBuffers;
        if (_readWriteStorageBufferCount != declaredBuffers)
        {
            throw new InvalidOperationException(
                $"Read-write storage buffer count mismatch: compute pass was created with {_readWriteStorageBufferCount} but pipeline declares {declaredBuffers}.");
        }

        ShaderBindingLayoutValidator.ValidateStorageBufferElementSizes("Read-only",
            _boundPipeline.BindingLayout.StorageBufferElementSizes,
            _readOnlyStorageBufferElementSizes);

        ShaderBindingLayoutValidator.ValidateStorageBufferElementSizes("Read-write",
            _boundPipeline.BindingLayout.ReadWriteStorageBufferElementSizes,
            _readWriteStorageBufferElementSizes);
    }

    private static StorageBufferElementSizes SetStorageBufferSlotSize(StorageBufferElementSizes sizes, uint slot, ushort elementSize)
    {
        return slot switch
        {
            0 => sizes with { Slot0 = elementSize },
            1 => sizes with { Slot1 = elementSize },
            2 => sizes with { Slot2 = elementSize },
            3 => sizes with { Slot3 = elementSize },
            _ => sizes
        };
    }
}
