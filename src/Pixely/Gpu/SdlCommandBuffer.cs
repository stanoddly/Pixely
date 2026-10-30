using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

internal sealed class SdlCommandBuffer : CommandBuffer
{
    private readonly SdlGpuDevice _gpuDevice;

    internal Pointer<SDL_GPUCommandBuffer> Pointer { get; private set; }

    internal SdlCommandBuffer(SdlGpuDevice gpuDevice, Pointer<SDL_GPUCommandBuffer> pointer) : base(gpuDevice)
    {
        _gpuDevice = gpuDevice;
        Pointer = pointer;
    }

    internal override void SubmitCore()
    {
        // Once the submit reaches SDL's backend, SDL invalidates the command buffer even when it fails, so it cannot be retried
        // or cancelled. Only SDL's debug check for a pass still in progress fails earlier and leaves it valid, which SDL also
        // asserts on as a programming error.
        Pointer<SDL_GPUCommandBuffer> sdlCommandBuffer = Pointer;
        Pointer = Pointer<SDL_GPUCommandBuffer>.Null;
        unsafe
        {
            SdlError.ThrowOnFalse(SDL3.SDL_SubmitGPUCommandBuffer(sdlCommandBuffer), "SDL_SubmitGPUCommandBuffer");
        }
    }

    internal override GpuFence SubmitAndAcquireFenceCore()
    {
        unsafe
        {
            SDL_GPUFence* fence = SDL3.SDL_SubmitGPUCommandBufferAndAcquireFence(Pointer);
            Pointer = Pointer<SDL_GPUCommandBuffer>.Null;

            if (fence == null)
            {
                throw new PixelyException($"SDL_SubmitGPUCommandBufferAndAcquireFence failed: {SDL3.SDL_GetError()}");
            }

            return new GpuFence(_gpuDevice, fence);
        }
    }

#if !BROWSER
    internal override void SubmitAndDownloadTextureCore(Texture texture, byte[] pixels)
    {
        unsafe
        {
            SDL_GPUTransferBufferCreateInfo transferBufferCreateInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD,
                size = (uint)pixels.Length
            };
            SDL_GPUTransferBuffer* transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(_gpuDevice.Pointer, &transferBufferCreateInfo);
            if (transferBuffer == null)
            {
                // The command buffer is already marked finished, so nothing else would cancel it.
                string? error = SDL3.SDL_GetError();
                CancelCore();
                throw new PixelyException(error);
            }

            try
            {
                SDL_GPUTextureRegion source = new SDL_GPUTextureRegion { texture = texture.SdlGpuTexture, w = texture.Size.Width, h = texture.Size.Height, d = 1 };
                SDL_GPUTextureTransferInfo destination = new SDL_GPUTextureTransferInfo { transfer_buffer = transferBuffer };

                SDL_GPUCopyPass* copyPass = SDL3.SDL_BeginGPUCopyPass(Pointer);
                SDL3.SDL_DownloadFromGPUTexture(copyPass, &source, &destination);
                SDL3.SDL_EndGPUCopyPass(copyPass);

                using (GpuFence fence = SubmitAndAcquireFenceCore())
                {
                    _gpuDevice.WaitForFences([fence]);
                }

                byte* mapped = (byte*)SdlBoolInterop.SDL_MapGPUTransferBuffer(_gpuDevice.Pointer, transferBuffer, false);
                SdlError.ThrowOnNull(mapped);
                new ReadOnlySpan<byte>(mapped, pixels.Length).CopyTo(pixels);
                SDL3.SDL_UnmapGPUTransferBuffer(_gpuDevice.Pointer, transferBuffer);
            }
            finally
            {
                SDL3.SDL_ReleaseGPUTransferBuffer(_gpuDevice.Pointer, transferBuffer);
            }
        }
    }
#endif

    internal override void PushVertexUniformDataCore(uint slot, ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* dataPointer = data)
            {
                SDL3.SDL_PushGPUVertexUniformData(Pointer, slot, (IntPtr)dataPointer, (uint)data.Length);
            }
        }
    }

    internal override void PushFragmentUniformDataCore(uint slot, ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* dataPointer = data)
            {
                SDL3.SDL_PushGPUFragmentUniformData(Pointer, slot, (IntPtr)dataPointer, (uint)data.Length);
            }
        }
    }

    internal override void PushComputeUniformDataCore(uint slot, ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* dataPointer = data)
            {
                SDL3.SDL_PushGPUComputeUniformData(Pointer, slot, (IntPtr)dataPointer, (uint)data.Length);
            }
        }
    }

    internal override RenderPass CreateRenderPassCore(in RenderPassTargets targets, DepthBufferFormat depthBufferFormat, SampleCount sampleCount, ShortSize targetSize)
    {
        ReadOnlySpan<Texture> colorTargets = targets.ColorTargets;
        Span<SDL_GPUColorTargetInfo> colorTargetInfos = stackalloc SDL_GPUColorTargetInfo[colorTargets.Length];

        for (int i = 0; i < colorTargets.Length; i++)
        {
            ColorTargetSettings colorTargetSetting = targets.ColorTargetSettings[i];
            Texture? resolveTexture = targets.ResolveTextures.IsEmpty ? null : targets.ResolveTextures[i];

            colorTargetInfos[i] = new SDL_GPUColorTargetInfo
            {
                texture = colorTargets[i].SdlGpuTexture,
                clear_color = colorTargetSetting.ClearColorValue,
                load_op = (SDL_GPULoadOp)colorTargetSetting.LoadOperation,
                store_op = (SDL_GPUStoreOp)colorTargetSetting.StoreOperation,
                resolve_texture = resolveTexture != null ? resolveTexture.SdlGpuTexture : Pointer<SDL_GPUTexture>.Null
            };
        }

        unsafe
        {
            SDL_GPURenderPass* gpuRenderPass;
            fixed (SDL_GPUColorTargetInfo* colorTargetInfosPtr = colorTargetInfos)
            {
                if (targets.DepthBuffer == null)
                {
                    gpuRenderPass = SDL3.SDL_BeginGPURenderPass(
                        Pointer,
                        colorTargetInfosPtr,
                        (uint)colorTargetInfos.Length,
                        null);
                }
                else
                {
                    DepthBufferSettings depthBufferSettings = targets.DepthBufferSettings;
                    SDL_GPUDepthStencilTargetInfo depthStencilTargetInfo = new SDL_GPUDepthStencilTargetInfo
                    {
                        texture = targets.DepthBuffer.SdlGpuTexture,
                        clear_depth = depthBufferSettings.ClearDepthValue,
                        load_op = (SDL_GPULoadOp)depthBufferSettings.DepthBufferLoadOperation,
                        store_op = (SDL_GPUStoreOp)depthBufferSettings.DepthBufferStoreOperation,
                        stencil_load_op = (SDL_GPULoadOp)depthBufferSettings.StencilLoadOperation,
                        stencil_store_op = (SDL_GPUStoreOp)depthBufferSettings.StencilStoreOperation,
                        clear_stencil = depthBufferSettings.ClearStencilValue
                    };

                    gpuRenderPass = SDL3.SDL_BeginGPURenderPass(
                        Pointer,
                        colorTargetInfosPtr,
                        (uint)colorTargetInfos.Length,
                        &depthStencilTargetInfo);
                }
            }

            // SDL returns no pass for a misuse, such as a pass begun while another is still recording.
            SdlError.ThrowOnNull<SDL_GPURenderPass>(gpuRenderPass, "SDL_BeginGPURenderPass");
            return new SdlRenderPass(this, gpuRenderPass, depthBufferFormat, sampleCount, targetSize);
        }
    }

    internal override ComputePass CreateComputePassCore(
        ReadOnlySpan<StorageTextureReadWriteBinding> readWriteStorageTextures,
        ReadOnlySpan<StorageBufferReadWriteBinding> readWriteStorageBuffers,
        StorageBufferElementSizes readWriteStorageBufferElementSizes)
    {
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

            SDL_GPUComputePass* computePass = SDL3.SDL_BeginGPUComputePass(
                Pointer,
                textureBindings,
                (uint)readWriteStorageTextures.Length,
                bufferBindings,
                (uint)readWriteStorageBuffers.Length);

            // SDL returns no pass for a misuse, such as a pass begun while another is still recording.
            SdlError.ThrowOnNull<SDL_GPUComputePass>(computePass, "SDL_BeginGPUComputePass");
            return new SdlComputePass(computePass, (uint)readWriteStorageTextures.Length, (uint)readWriteStorageBuffers.Length, readWriteStorageBufferElementSizes);
        }
    }

    internal override CopyPass BeginCopyPassCore(UploadTransferBuffer transferBuffer)
    {
        unsafe
        {
            Pointer<SDL_GPUCopyPass> sdlCopyPass = SDL3.SDL_BeginGPUCopyPass(Pointer);
            SdlError.ThrowOnNull(sdlCopyPass);
            return new SdlCopyPass(_gpuDevice, sdlCopyPass, transferBuffer);
        }
    }

    internal override void BlitTexturesCore(Texture source, Texture destination)
    {
        unsafe
        {
            SDL_GPUBlitRegion sourceRegion = new SDL_GPUBlitRegion
            {
                texture = source.SdlGpuTexture,
                x = 0,
                y = 0,
                w = source.Size.Width,
                h = source.Size.Height
            };

            SDL_GPUBlitRegion destinationRegion = new SDL_GPUBlitRegion
            {
                texture = destination.SdlGpuTexture,
                x = 0,
                y = 0,
                w = destination.Size.Width,
                h = destination.Size.Height
            };

            SDL_GPUBlitInfo blitInfo = new SDL_GPUBlitInfo
            {
                source = sourceRegion,
                destination = destinationRegion,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                flip_mode = SDL_FlipMode.SDL_FLIP_NONE,
                filter = SDL_GPUFilter.SDL_GPU_FILTER_NEAREST,
            };

            SDL3.SDL_BlitGPUTexture(Pointer, &blitInfo);
        }
    }

    internal override void CancelCore()
    {
        unsafe
        {
            SDL3.SDL_CancelGPUCommandBuffer(Pointer);
        }

        Pointer = Pointer<SDL_GPUCommandBuffer>.Null;
    }
}
