using System.Runtime.CompilerServices;
using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

/// <summary>
/// A compute pass open on a <see cref="CommandBuffer"/>. Like <see cref="RenderPass"/>, it holds no state of its own, so copies
/// act on the same pass, a handle used after its pass was disposed throws, and disposing it again does nothing.
/// </summary>
public readonly ref struct ComputePass : IDisposable
{
    private readonly ref CommandBufferState _commandBuffer;
    private readonly uint _passNumber;

    private ComputePass(ref CommandBufferState commandBuffer, uint passNumber)
    {
        _commandBuffer = ref commandBuffer;
        _passNumber = passNumber;
    }

    internal static ComputePass Begin(
        ref CommandBufferState commandBuffer,
        scoped ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        scoped ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers)
    {
        commandBuffer.ThrowIfDisposed();
        commandBuffer.ThrowIfPassOpen();

        Pointer<SDL_GPUComputePass> nativePointer;

        unsafe
        {
            SDL_GPUStorageTextureReadWriteBinding* textureBindings = stackalloc SDL_GPUStorageTextureReadWriteBinding[readWriteStorageTextures.Length];
            for (int i = 0; i < readWriteStorageTextures.Length; i++)
            {
                textureBindings[i] = new SDL_GPUStorageTextureReadWriteBinding
                {
                    texture = readWriteStorageTextures[i].Texture.SdlGpuTexture,
                    mip_level = readWriteStorageTextures[i].MipLevel,
                    layer = readWriteStorageTextures[i].Layer,
                    cycle = readWriteStorageTextures[i].Cycle
                };
            }

            SDL_GPUStorageBufferReadWriteBinding* bufferBindings = stackalloc SDL_GPUStorageBufferReadWriteBinding[readWriteStorageBuffers.Length];
            for (int i = 0; i < readWriteStorageBuffers.Length; i++)
            {
                bufferBindings[i] = new SDL_GPUStorageBufferReadWriteBinding
                {
                    buffer = readWriteStorageBuffers[i].Buffer.SdlBuffer,
                    cycle = readWriteStorageBuffers[i].Cycle
                };
            }

            nativePointer = SDL3.SDL_BeginGPUComputePass(
                commandBuffer.SdlGpuCommandBuffer,
                textureBindings,
                (uint)readWriteStorageTextures.Length,
                bufferBindings,
                (uint)readWriteStorageBuffers.Length);
        }

        SdlError.ThrowOnNull(nativePointer);

        uint passNumber = commandBuffer.OpenNextPass(OpenPassKind.Compute);
        commandBuffer.ComputePass = new ComputePassState
        {
            NativePointer = nativePointer,
            ReadWriteStorageTextureCount = (uint)readWriteStorageTextures.Length,
            ReadWriteStorageBufferCount = (uint)readWriteStorageBuffers.Length,
            ReadWriteStorageBufferElementSizes = BuildStorageBufferElementSizes(readWriteStorageBuffers)
        };

        return new ComputePass(ref commandBuffer, passNumber);
    }

    public void BindComputePipeline(ComputePipeline pipeline)
    {
        ref ComputePassState state = ref OpenState();
        state.BoundPipeline = pipeline;
        state.ReadOnlyStorageBufferElementSizes = default;
        unsafe
        {
            SDL3.SDL_BindGPUComputePipeline(state.NativePointer, pipeline.Pointer);
        }
    }

    public void BindSamplers(ReadOnlySpan<Texture> textures, Sampler sampler, uint slot = 0)
    {
        ref ComputePassState state = ref OpenState();
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
            SDL3.SDL_BindGPUComputeSamplers(state.NativePointer, slot, bindings, (uint)textures.Length);
        }
    }

    public void BindReadOnlyStorageTextures(ReadOnlySpan<Texture> textures, uint slot = 0)
    {
        ref ComputePassState state = ref OpenState();
        unsafe
        {
            SDL_GPUTexture** sdlTextures = stackalloc SDL_GPUTexture*[textures.Length];
            for (int i = 0; i < textures.Length; i++)
            {
                sdlTextures[i] = textures[i].SdlGpuTexture;
            }
            SDL3.SDL_BindGPUComputeStorageTextures(state.NativePointer, slot, sdlTextures, (uint)textures.Length);
        }
    }

    public void BindReadOnlyStorageTexture(Texture texture, uint slot = 0)
    {
        ReadOnlySpan<Texture> textures = [texture];
        BindReadOnlyStorageTextures(textures, slot);
    }

    public void BindReadOnlyStorageBuffers(ReadOnlySpan<GpuStorageBuffer> buffers, uint slot = 0)
    {
        ref ComputePassState state = ref OpenState();
        for (int i = 0; i < buffers.Length; i++)
        {
            state.ReadOnlyStorageBufferElementSizes = SetStorageBufferSlotSize(state.ReadOnlyStorageBufferElementSizes, slot + (uint)i, (ushort)buffers[i].ElementSize);
        }
        unsafe
        {
            SDL_GPUBuffer** sdlBuffers = stackalloc SDL_GPUBuffer*[buffers.Length];
            for (int i = 0; i < buffers.Length; i++)
            {
                sdlBuffers[i] = buffers[i].SdlBuffer;
            }
            SDL3.SDL_BindGPUComputeStorageBuffers(state.NativePointer, slot, sdlBuffers, (uint)buffers.Length);
        }
    }

    public void BindReadOnlyStorageBuffer(GpuStorageBuffer buffer, uint slot = 0)
    {
        ReadOnlySpan<GpuStorageBuffer> buffers = [buffer];
        BindReadOnlyStorageBuffers(buffers, slot);
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        ref ComputePassState state = ref OpenState();
        ThrowIfInvalidDispatch(state);
        unsafe
        {
            SDL3.SDL_DispatchGPUCompute(state.NativePointer, groupCountX, groupCountY, groupCountZ);
        }
    }

    public void DispatchIndirect(GpuStorageBuffer buffer, uint offset = 0)
    {
        ref ComputePassState state = ref OpenState();
        ThrowIfInvalidDispatch(state);
        unsafe
        {
            SDL3.SDL_DispatchGPUComputeIndirect(state.NativePointer, buffer.SdlBuffer, offset);
        }
    }

    public bool IsDefault()
    {
        return Unsafe.IsNullRef(ref _commandBuffer);
    }

    public void Dispose()
    {
        if (Unsafe.IsNullRef(ref _commandBuffer) || !_commandBuffer.IsPassOpen(OpenPassKind.Compute, _passNumber))
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_EndGPUComputePass(_commandBuffer.ComputePass.NativePointer);
        }

        _commandBuffer.ClosePass();
    }

    private ref ComputePassState OpenState()
    {
        if (Unsafe.IsNullRef(ref _commandBuffer) || !_commandBuffer.IsPassOpen(OpenPassKind.Compute, _passNumber))
        {
            throw new ObjectDisposedException(nameof(ComputePass));
        }

        return ref _commandBuffer.ComputePass;
    }

    private static void ThrowIfInvalidDispatch(in ComputePassState state)
    {
        if (state.BoundPipeline == null)
        {
            throw new InvalidOperationException("ComputePipeline must be bound before dispatching.");
        }

        uint declaredTextures = state.BoundPipeline.BindingLayout.BindingCounts.NumReadWriteStorageTextures;
        if (state.ReadWriteStorageTextureCount != declaredTextures)
        {
            throw new InvalidOperationException(
                $"Read-write storage texture count mismatch: compute pass was created with {state.ReadWriteStorageTextureCount} but pipeline declares {declaredTextures}.");
        }

        uint declaredBuffers = state.BoundPipeline.BindingLayout.BindingCounts.NumReadWriteStorageBuffers;
        if (state.ReadWriteStorageBufferCount != declaredBuffers)
        {
            throw new InvalidOperationException(
                $"Read-write storage buffer count mismatch: compute pass was created with {state.ReadWriteStorageBufferCount} but pipeline declares {declaredBuffers}.");
        }

        ShaderBindingLayoutValidator.ValidateStorageBufferElementSizes("Read-only",
            state.BoundPipeline.BindingLayout.StorageBufferElementSizes,
            state.ReadOnlyStorageBufferElementSizes);

        ShaderBindingLayoutValidator.ValidateStorageBufferElementSizes("Read-write",
            state.BoundPipeline.BindingLayout.ReadWriteStorageBufferElementSizes,
            state.ReadWriteStorageBufferElementSizes);
    }

    private static StorageBufferElementSizes BuildStorageBufferElementSizes(ReadOnlySpan<StorageBufferReadWriteBinding> buffers)
    {
        StorageBufferElementSizes sizes = default;
        for (int i = 0; i < buffers.Length && i < 4; i++)
        {
            sizes = SetStorageBufferSlotSize(sizes, (uint)i, (ushort)buffers[i].Buffer.ElementSize);
        }
        return sizes;
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
