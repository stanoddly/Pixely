using System.Runtime.InteropServices;
using Pixely.Shaders;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

// The GPU device backed by SDL GPU, the one every game runs on.
internal sealed class SdlGpuDevice : GpuDevice
{
    internal Pointer<SDL_GPUDevice> Pointer { get; private set; }

    internal override string? DeviceName { get; }

    internal override string? DriverVersion { get; }

    private SdlGpuDevice(Pointer<SDL_GPUDevice> pointer, string driver, SDL_PropertiesID properties)
        : base(driver, SDL3.SDL_GetStringProperty(properties, SDL3.SDL_PROP_GPU_DEVICE_DRIVER_NAME_STRING, null))
    {
        Pointer = pointer;
        DeviceName = SDL3.SDL_GetStringProperty(properties, SDL3.SDL_PROP_GPU_DEVICE_NAME_STRING, null);
        DriverVersion = SDL3.SDL_GetStringProperty(properties, SDL3.SDL_PROP_GPU_DEVICE_DRIVER_VERSION_STRING, null);
    }

    internal static SdlGpuDevice Create(Pointer<SDL_GPUDevice> pointer)
    {
        string driver;
        SDL_PropertiesID properties;
        unsafe
        {
            byte* driverName = SDL3.Unsafe_SDL_GetGPUDeviceDriver(pointer);
            driver = Marshal.PtrToStringUTF8((IntPtr)driverName) ??
                     throw new PixelyInitializationException("SDL_GetGPUDeviceDriver returned null");
            properties = SDL3.SDL_GetGPUDeviceProperties(pointer);
        }

        return new SdlGpuDevice(pointer, driver, properties);
    }

    internal override ShaderFormats GetSupportedShaderFormatsCore()
    {
        unsafe
        {
            return new ShaderFormats((uint)SDL3.SDL_GetGPUShaderFormats(Pointer));
        }
    }

    internal override bool IsTextureFormatSupportedCore(TextureFormat format, TextureType type, TextureUsage usage)
    {
        unsafe
        {
            return SDL3.SDL_GPUTextureSupportsFormat(
                Pointer,
                (SDL_GPUTextureFormat)format,
                (SDL_GPUTextureType)type,
                (SDL_GPUTextureUsageFlags)usage);
        }
    }

    // On Direct3D 12 the SDL that Pixely pins checks a depth format by its shader-view format, so it can report no multisampling for a depth
    // format the GPU supports it for, such as Depth24Stencil8. Texture creation and pipeline building then reject that count.
    // libsdl-org/SDL#16124, fixed by libsdl-org/SDL#16230 in a later SDL.
    internal override bool IsSampleCountSupportedCore(TextureFormat format, SampleCount sampleCount)
    {
        unsafe
        {
            return SDL3.SDL_GPUTextureSupportsSampleCount(Pointer, (SDL_GPUTextureFormat)format, (SDL_GPUSampleCount)sampleCount);
        }
    }

    internal override CommandBuffer AcquireCommandBufferCore()
    {
        unsafe
        {
            Pointer<SDL_GPUCommandBuffer> sdlGpuCommandBuffer = SDL3.SDL_AcquireGPUCommandBuffer(Pointer);

            if (sdlGpuCommandBuffer.IsNull)
            {
                throw new PixelyInitializationException($"SDL_AcquireGPUCommandBuffer failed: {SDL3.SDL_GetError()}");
            }

            return new SdlCommandBuffer(this, sdlGpuCommandBuffer);
        }
    }

    internal override Pointer<SDL_GPUSampler> CreateSamplerCore(SamplerConfig config)
    {
        SDL_GPUSamplerCreateInfo sdlGpuSamplerCreateInfo = new SDL_GPUSamplerCreateInfo()
        {
            min_filter = (SDL_GPUFilter)config.MinFilter,
            mag_filter = (SDL_GPUFilter)config.MagFilter,
            mipmap_mode = (SDL_GPUSamplerMipmapMode)config.MipmapMode,
            address_mode_u = (SDL_GPUSamplerAddressMode)config.AddressModeU,
            address_mode_v = (SDL_GPUSamplerAddressMode)config.AddressModeV,
            address_mode_w = (SDL_GPUSamplerAddressMode)config.AddressModeW,
            mip_lod_bias = config.MipLodBias,
            min_lod = config.MinLod,
            max_lod = config.MaxLod,
            max_anisotropy = config.MaxAnisotropy,
            enable_anisotropy = config.EnableAnisotropy,
            compare_op = (SDL_GPUCompareOp)config.CompareOp,
            enable_compare = config.EnableCompare
        };

        unsafe
        {
            Pointer<SDL_GPUSampler> samplerPointer = SDL3.SDL_CreateGPUSampler(Pointer, &sdlGpuSamplerCreateInfo);
            SdlError.ThrowOnNull(samplerPointer);
            return samplerPointer;
        }
    }

    internal override Pointer<SDL_GPUTexture> CreateTextureCore(TextureType type, ShortSize size, uint layerCount, TextureFormat format, TextureUsage usage, SampleCount sampleCount)
    {
        SdlError.Clear();

        unsafe
        {
            SDL_GPUTextureCreateInfo info = new SDL_GPUTextureCreateInfo
            {
                type = (SDL_GPUTextureType)type,
                usage = (SDL_GPUTextureUsageFlags)usage,
                format = (SDL_GPUTextureFormat)format,
                width = size.Width,
                height = size.Height,
                layer_count_or_depth = layerCount,
                num_levels = 1,
                sample_count = (SDL_GPUSampleCount)sampleCount
            };

            Pointer<SDL_GPUTexture> texture = SDL3.SDL_CreateGPUTexture(Pointer, &info);
            SdlError.ThrowOnNull(texture);
            return texture;
        }
    }

    internal override Pointer<SDL_GPUBuffer> CreateBufferCore(SDL_GPUBufferUsageFlags usage, uint sizeInBytes)
    {
        unsafe
        {
            SDL_GPUBufferCreateInfo createInfo = new SDL_GPUBufferCreateInfo
            {
                usage = usage,
                size = sizeInBytes
            };

            Pointer<SDL_GPUBuffer> buffer = SDL3.SDL_CreateGPUBuffer(Pointer, &createInfo);
            SdlError.ThrowOnNull(buffer);
            return buffer;
        }
    }

    internal override Pointer<SDL_GPUTransferBuffer> CreateUploadTransferBufferCore(uint sizeInBytes)
    {
        unsafe
        {
            SDL_GPUTransferBufferCreateInfo createInfo = new SDL_GPUTransferBufferCreateInfo
            {
                usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                size = sizeInBytes
            };
            Pointer<SDL_GPUTransferBuffer> transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(Pointer, &createInfo);
            SdlError.ThrowOnNull(transferBuffer);
            return transferBuffer;
        }
    }

    internal override Pointer<SDL_GPUShader> CreateShaderCore(in SDL_GPUShaderCreateInfo createInfo)
    {
        unsafe
        {
            fixed (SDL_GPUShaderCreateInfo* createInfoPointer = &createInfo)
            {
                SDL_GPUShader* shader = SDL3.SDL_CreateGPUShader(Pointer, createInfoPointer);
                if (shader == null)
                {
                    string stageName = createInfo.stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "vertex" : "fragment";
                    throw new PixelyInitializationException($"SDL_CreateGPUShader failed for {stageName} stage: " + SDL3.SDL_GetError());
                }

                return shader;
            }
        }
    }

    internal override Pointer<SDL_GPUGraphicsPipeline> CreateGraphicsPipelineCore(in SDL_GPUGraphicsPipelineCreateInfo createInfo)
    {
        unsafe
        {
            fixed (SDL_GPUGraphicsPipelineCreateInfo* createInfoPointer = &createInfo)
            {
                SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(Pointer, createInfoPointer);
                if (pipeline == null)
                {
                    throw new PixelyInitializationException($"SDL_CreateGPUGraphicsPipeline failed: {SDL3.SDL_GetError()}");
                }

                return pipeline;
            }
        }
    }

    internal override Pointer<SDL_GPUComputePipeline> CreateComputePipelineCore(in SDL_GPUComputePipelineCreateInfo createInfo)
    {
        unsafe
        {
            fixed (SDL_GPUComputePipelineCreateInfo* createInfoPointer = &createInfo)
            {
                SDL_GPUComputePipeline* pipeline = SDL3.SDL_CreateGPUComputePipeline(Pointer, createInfoPointer);
                if (pipeline == null)
                {
                    throw new PixelyInitializationException($"SDL_CreateGPUComputePipeline failed: {SDL3.SDL_GetError()}");
                }

                return pipeline;
            }
        }
    }

    internal override void ReleaseTextureCore(Pointer<SDL_GPUTexture> texture)
    {
        unsafe
        {
            SDL3.SDL_ReleaseGPUTexture(Pointer, texture);
        }
    }

    internal override void ReleaseBufferCore(Pointer<SDL_GPUBuffer> buffer)
    {
        unsafe
        {
            SDL3.SDL_ReleaseGPUBuffer(Pointer, buffer);
        }
    }

    internal override void ReleaseTransferBufferCore(Pointer<SDL_GPUTransferBuffer> transferBuffer)
    {
        if (transferBuffer.IsNull)
        {
            return;
        }

        unsafe
        {
            SDL3.SDL_ReleaseGPUTransferBuffer(Pointer, transferBuffer);
        }
    }

    internal override void ReleaseSamplerCore(Pointer<SDL_GPUSampler> sampler)
    {
        unsafe
        {
            SDL3.SDL_ReleaseGPUSampler(Pointer, sampler);
        }
    }

    internal override void ReleaseShaderCore(Pointer<SDL_GPUShader> shader)
    {
        unsafe
        {
            SDL3.SDL_ReleaseGPUShader(Pointer, shader);
        }
    }

    internal override void ReleaseGraphicsPipelineCore(Pointer<SDL_GPUGraphicsPipeline> pipeline)
    {
        unsafe
        {
            SDL3.SDL_ReleaseGPUGraphicsPipeline(Pointer, pipeline);
        }
    }

    internal override void ReleaseComputePipelineCore(Pointer<SDL_GPUComputePipeline> pipeline)
    {
        unsafe
        {
            SDL3.SDL_ReleaseGPUComputePipeline(Pointer, pipeline);
        }
    }

    internal override bool QueryFenceCore(Pointer<SDL_GPUFence> fence)
    {
        unsafe
        {
            return SDL3.SDL_QueryGPUFence(Pointer, fence);
        }
    }

    internal override void ReleaseFenceCore(Pointer<SDL_GPUFence> fence)
    {
        unsafe
        {
            SDL3.SDL_ReleaseGPUFence(Pointer, fence);
        }
    }

#if !BROWSER
    internal override void WaitForFencesCore(ReadOnlySpan<GpuFence> fences, bool waitAll)
    {
        Span<Pointer<SDL_GPUFence>> fencePointers = stackalloc Pointer<SDL_GPUFence>[fences.Length];
        for (int i = 0; i < fences.Length; i++)
        {
            fencePointers[i] = fences[i].Pointer;
        }

        unsafe
        {
            fixed (Pointer<SDL_GPUFence>* fencePointersPtr = fencePointers)
            {
                if (!SdlBoolInterop.SDL_WaitForGPUFences(Pointer, waitAll, (SDL_GPUFence**)fencePointersPtr, (uint)fences.Length))
                {
                    throw new PixelyException($"SDL_WaitForGPUFences failed: {SDL3.SDL_GetError()}");
                }
            }
        }
    }
#endif

    internal override void DestroyCore()
    {
#if !BROWSER
        // In the browser the device lives as long as the page, which frees it. A clean destroy has to wait for the last submissions,
        // and a submission completes only after the page's event loop turns, which a synchronous Dispose cannot wait for.
        unsafe
        {
            SDL3.SDL_DestroyGPUDevice(Pointer);
            Pointer = null;
        }
#endif
    }
}
