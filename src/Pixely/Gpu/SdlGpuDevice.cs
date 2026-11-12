using System.Runtime.InteropServices;
using System.Text;
using Pixely.Shaders;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Gpu;

// The GPU device backed by SDL GPU, the one every game runs on.
internal sealed class SdlGpuDevice : GpuDevice
{
    internal Pointer<SDL_GPUDevice> Pointer { get; private set; }

    internal override string? DeviceName { get; }

    internal override string? DriverVersion { get; }

    private SdlGpuDevice(Pointer<SDL_GPUDevice> pointer, string driver, SDL_PropertiesID properties)
        : base(driver, GetStringProperty(properties, SDL_PROP_GPU_DEVICE_DRIVER_NAME_STRING))
    {
        Pointer = pointer;
        DeviceName = GetStringProperty(properties, SDL_PROP_GPU_DEVICE_NAME_STRING);
        DriverVersion = GetStringProperty(properties, SDL_PROP_GPU_DEVICE_DRIVER_VERSION_STRING);
    }

    private static string? GetStringProperty(SDL_PropertiesID properties, ReadOnlySpan<byte> name)
    {
        ReadOnlySpan<byte> value = SDL_GetStringProperty(properties, name, default);
        return value.IsEmpty ? null : Encoding.UTF8.GetString(value);
    }

    internal static SdlGpuDevice Create(Pointer<SDL_GPUDevice> pointer)
    {
        string driver;
        SDL_PropertiesID properties;
        unsafe
        {
            driver = Marshal.PtrToStringUTF8((nint)SDL_GetGPUDeviceDriver(pointer)) ??
                     throw new PixelyInitializationException("SDL_GetGPUDeviceDriver returned null");
            properties = SDL_GetGPUDeviceProperties(pointer);
        }

        return new SdlGpuDevice(pointer, driver, properties);
    }

    internal override ShaderFormats GetSupportedShaderFormatsCore()
    {
        unsafe
        {
            return new ShaderFormats((uint)SDL_GetGPUShaderFormats(Pointer));
        }
    }

    internal override bool IsTextureFormatSupportedCore(TextureFormat format, TextureType type, TextureUsage usage)
    {
        unsafe
        {
            return SDL_GPUTextureSupportsFormat(
                Pointer,
                (SDL_GPUTextureFormat)format,
                (SDL_GPUTextureType)type,
                (SDL_GPUTextureUsageFlags)usage);
        }
    }

    internal override bool IsSampleCountSupportedCore(TextureFormat format, SampleCount sampleCount)
    {
        unsafe
        {
            return SDL_GPUTextureSupportsSampleCount(Pointer, (SDL_GPUTextureFormat)format, (SDL_GPUSampleCount)sampleCount);
        }
    }

    internal override CommandBuffer AcquireCommandBufferCore()
    {
        unsafe
        {
            Pointer<SDL_GPUCommandBuffer> sdlGpuCommandBuffer = SDL_AcquireGPUCommandBuffer(Pointer);

            if (sdlGpuCommandBuffer.IsNull)
            {
                throw new PixelyInitializationException($"SDL_AcquireGPUCommandBuffer failed: {SdlError.GetError()}");
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
            Pointer<SDL_GPUSampler> samplerPointer = SDL_CreateGPUSampler(Pointer, &sdlGpuSamplerCreateInfo);
            SdlError.ThrowOnNull(samplerPointer);
            return samplerPointer;
        }
    }

    internal override Pointer<SDL_GPUTexture> CreateTextureCore(TextureType type, ShortSize size, uint layerCount, TextureFormat format, TextureUsage usage, SampleCount sampleCount)
    {
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

            Pointer<SDL_GPUTexture> texture = SDL_CreateGPUTexture(Pointer, &info);
            SdlError.ThrowOnNull(texture);
            return texture;
        }
    }

    internal override Pointer<SDL_GPUBuffer> CreateBufferCore(SDL_GPUBufferUsageFlags usage, uint sizeInBytes, uint stride)
    {
        SDL_PropertiesID props = stride == 0 ? default : CreateProperties();
        try
        {
            if (stride != 0)
            {
                SetNumberProperty(props, XDL_PROP_GPU_BUFFER_CREATE_STRIDE_NUMBER, stride);
            }

            unsafe
            {
                SDL_GPUBufferCreateInfo createInfo = new SDL_GPUBufferCreateInfo
                {
                    usage = usage,
                    size = sizeInBytes,
                    props = props
                };

                Pointer<SDL_GPUBuffer> buffer = SDL_CreateGPUBuffer(Pointer, &createInfo);
                SdlError.ThrowOnNull(buffer);
                return buffer;
            }
        }
        finally
        {
            DestroyProperties(props);
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
            Pointer<SDL_GPUTransferBuffer> transferBuffer = SDL_CreateGPUTransferBuffer(Pointer, &createInfo);
            SdlError.ThrowOnNull(transferBuffer);
            return transferBuffer;
        }
    }

    internal override Pointer<SDL_GPUShader> CreateShaderCore(in SDL_GPUShaderCreateInfo createInfo, byte structuredStorageBuffers)
    {
        SDL_PropertiesID props = structuredStorageBuffers == 0 ? default : CreateProperties();
        try
        {
            if (structuredStorageBuffers != 0)
            {
                SetNumberProperty(props, XDL_PROP_GPU_SHADER_CREATE_STRUCTURED_STORAGE_BUFFERS_NUMBER, structuredStorageBuffers);
            }

            SDL_GPUShaderCreateInfo createInfoWithProperties = createInfo;
            createInfoWithProperties.props = props;
            unsafe
            {
                SDL_GPUShader* shader = SDL_CreateGPUShader(Pointer, &createInfoWithProperties);
                if (shader == null)
                {
                    string stageName = createInfo.stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX ? "vertex" : "fragment";
                    throw new PixelyInitializationException($"SDL_CreateGPUShader failed for {stageName} stage: " + SdlError.GetError());
                }

                return shader;
            }
        }
        finally
        {
            DestroyProperties(props);
        }
    }

    internal override Pointer<SDL_GPUGraphicsPipeline> CreateGraphicsPipelineCore(in SDL_GPUGraphicsPipelineCreateInfo createInfo)
    {
        unsafe
        {
            fixed (SDL_GPUGraphicsPipelineCreateInfo* createInfoPointer = &createInfo)
            {
                SDL_GPUGraphicsPipeline* pipeline = SDL_CreateGPUGraphicsPipeline(Pointer, createInfoPointer);
                if (pipeline == null)
                {
                    throw new PixelyInitializationException($"SDL_CreateGPUGraphicsPipeline failed: {SdlError.GetError()}");
                }

                return pipeline;
            }
        }
    }

    internal override Pointer<SDL_GPUComputePipeline> CreateComputePipelineCore(
        in SDL_GPUComputePipelineCreateInfo createInfo, byte structuredReadOnlyStorageBuffers, byte structuredReadWriteStorageBuffers)
    {
        bool hasStructured = structuredReadOnlyStorageBuffers != 0 || structuredReadWriteStorageBuffers != 0;
        SDL_PropertiesID props = hasStructured ? CreateProperties() : default;
        try
        {
            if (hasStructured)
            {
                SetNumberProperty(props, XDL_PROP_GPU_COMPUTEPIPELINE_CREATE_STRUCTURED_READONLY_STORAGE_BUFFERS_NUMBER, structuredReadOnlyStorageBuffers);
                SetNumberProperty(props, XDL_PROP_GPU_COMPUTEPIPELINE_CREATE_STRUCTURED_READWRITE_STORAGE_BUFFERS_NUMBER, structuredReadWriteStorageBuffers);
            }

            SDL_GPUComputePipelineCreateInfo createInfoWithProperties = createInfo;
            createInfoWithProperties.props = props;
            unsafe
            {
                SDL_GPUComputePipeline* pipeline = SDL_CreateGPUComputePipeline(Pointer, &createInfoWithProperties);
                if (pipeline == null)
                {
                    throw new PixelyInitializationException($"SDL_CreateGPUComputePipeline failed: {SdlError.GetError()}");
                }

                return pipeline;
            }
        }
        finally
        {
            DestroyProperties(props);
        }
    }

    // The properties of a create info that needs some. The caller destroys them with DestroyProperties.
    private static SDL_PropertiesID CreateProperties()
    {
        SDL_PropertiesID props = SDL_CreateProperties();
        if (props == default)
        {
            SdlError.Throw(nameof(SDL_CreateProperties));
        }

        return props;
    }

    private static void SetNumberProperty(SDL_PropertiesID props, ReadOnlySpan<byte> name, long value) =>
        SdlError.ThrowOnFalse(SDL_SetNumberProperty(props, name, value), nameof(SDL_SetNumberProperty));

    // Nothing to destroy for a create info that needed no properties.
    private static void DestroyProperties(SDL_PropertiesID props)
    {
        if (props != default)
        {
            SDL_DestroyProperties(props);
        }
    }

    internal override void ReleaseTextureCore(Pointer<SDL_GPUTexture> texture)
    {
        unsafe
        {
            SDL_ReleaseGPUTexture(Pointer, texture);
        }
    }

    internal override void ReleaseBufferCore(Pointer<SDL_GPUBuffer> buffer)
    {
        unsafe
        {
            SDL_ReleaseGPUBuffer(Pointer, buffer);
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
            SDL_ReleaseGPUTransferBuffer(Pointer, transferBuffer);
        }
    }

    internal override void ReleaseSamplerCore(Pointer<SDL_GPUSampler> sampler)
    {
        unsafe
        {
            SDL_ReleaseGPUSampler(Pointer, sampler);
        }
    }

    internal override void ReleaseShaderCore(Pointer<SDL_GPUShader> shader)
    {
        unsafe
        {
            SDL_ReleaseGPUShader(Pointer, shader);
        }
    }

    internal override void ReleaseGraphicsPipelineCore(Pointer<SDL_GPUGraphicsPipeline> pipeline)
    {
        unsafe
        {
            SDL_ReleaseGPUGraphicsPipeline(Pointer, pipeline);
        }
    }

    internal override void ReleaseComputePipelineCore(Pointer<SDL_GPUComputePipeline> pipeline)
    {
        unsafe
        {
            SDL_ReleaseGPUComputePipeline(Pointer, pipeline);
        }
    }

    internal override bool QueryFenceCore(Pointer<SDL_GPUFence> fence)
    {
        unsafe
        {
            return SDL_QueryGPUFence(Pointer, fence);
        }
    }

    internal override void ReleaseFenceCore(Pointer<SDL_GPUFence> fence)
    {
        unsafe
        {
            SDL_ReleaseGPUFence(Pointer, fence);
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
                if (!SDL_WaitForGPUFences(Pointer, waitAll, fencePointersPtr, (uint)fences.Length))
                {
                    throw new PixelyException($"SDL_WaitForGPUFences failed: {SdlError.GetError()}");
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
            SDL_DestroyGPUDevice(Pointer);
            Pointer = null;
        }
#endif
    }
}
