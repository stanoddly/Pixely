using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pixely.Shaders;
using SDL;

namespace Pixely.Gpu;

public enum PrimitiveType
{
    TriangleList = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
    TriangleStrip = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP,
    LineList = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINELIST,
    LineStrip = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINESTRIP,
    PointList = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_POINTLIST
}

public enum SampleCount
{
    Count1 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
    Count2 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2,
    Count4 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4,
    Count8 = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_8
}

public enum CompareOperation
{
    Invalid = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_INVALID,
    Never = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NEVER,
    Less = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS,
    Equal = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_EQUAL,
    LessOrEqual = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
    Greater = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER,
    NotEqual = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NOT_EQUAL,
    GreaterOrEqual = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER_OR_EQUAL,
    Always = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS,

    // for reversed depth buffer
    ReversedLess = Greater,
    ReversedLessOrEqual = GreaterOrEqual,
    ReversedGreater = Less,
    ReversedGreaterOrEqual = LessOrEqual
}

public enum StencilOperation
{
    Invalid = SDL_GPUStencilOp.SDL_GPU_STENCILOP_INVALID,
    Keep = SDL_GPUStencilOp.SDL_GPU_STENCILOP_KEEP,
    Zero = SDL_GPUStencilOp.SDL_GPU_STENCILOP_ZERO,
    Replace = SDL_GPUStencilOp.SDL_GPU_STENCILOP_REPLACE,
    IncrementAndClamp = SDL_GPUStencilOp.SDL_GPU_STENCILOP_INCREMENT_AND_CLAMP,
    DecrementAndClamp = SDL_GPUStencilOp.SDL_GPU_STENCILOP_DECREMENT_AND_CLAMP,
    Invert = SDL_GPUStencilOp.SDL_GPU_STENCILOP_INVERT,
    IncrementAndWrap = SDL_GPUStencilOp.SDL_GPU_STENCILOP_INCREMENT_AND_WRAP,
    DecrementAndWrap = SDL_GPUStencilOp.SDL_GPU_STENCILOP_DECREMENT_AND_WRAP,
}

public readonly record struct StencilOperationState(
    StencilOperation Fail,
    StencilOperation Pass,
    StencilOperation DepthFail,
    CompareOperation Compare)
{
    public static implicit operator SDL_GPUStencilOpState(in StencilOperationState stencilOperationState)
    {
        return new SDL_GPUStencilOpState
        {
            compare_op = (SDL_GPUCompareOp)stencilOperationState.Compare,
            depth_fail_op = (SDL_GPUStencilOp)stencilOperationState.DepthFail,
            fail_op = (SDL_GPUStencilOp)stencilOperationState.Fail,
            pass_op = (SDL_GPUStencilOp)stencilOperationState.Pass
        };
    }
}

internal struct PipelineBuilderInfo
{
    public PipelineBuilderInfo()
    {
    }

    public PrimitiveType PrimitiveType { get; set; } = PrimitiveType.TriangleList;
    public List<SDL_GPUColorTargetDescription> SdlGpuColorTargetDescriptions { get; } = new();
    public List<SDL_GPUVertexAttribute> SdlGpuVertexAttributes { get; } = new();
    public List<SDL_GPUVertexBufferDescription> SdlGpuVertexBufferDescriptions { get; } = new();
    public List<VertexTypeId> VertexBufferTypeIds { get; } = new();
    public SDL_GPUMultisampleState SdlGpuMultisampleState { get; set; }
    public SDL_GPUDepthStencilState SdlGpuDepthStencilState = new();
    public SDL_GPUColorTargetBlendState SdlGpuColorTargetBlendState { get; set; }
    public RasterizerState RasterizerState { get; set; } = new() { CullMode = CullMode.Back, FrontFace = FrontFace.Clockwise };

    public DepthBufferFormat? DepthBufferFormat { get; set; }

    public GraphicsShaderProgram? ShaderProgram { get; set; }

    public void Reset()
    {
        SdlGpuColorTargetDescriptions.Clear();
        SdlGpuVertexAttributes.Clear();
        SdlGpuVertexBufferDescriptions.Clear();
        VertexBufferTypeIds.Clear();
        ShaderProgram = null;
        PrimitiveType = PrimitiveType.TriangleList;
        SdlGpuMultisampleState = new();
        SdlGpuDepthStencilState = new();
        DepthBufferFormat = null;
        SdlGpuColorTargetBlendState = default;
        // We use left hand coordinates, that's why CLOCKWISE winding order
        RasterizerState = new() { CullMode = CullMode.Back, FrontFace = FrontFace.Clockwise };
    }
}

public class GraphicsPipelineBuilder
{
    private readonly GpuDevice _gpuDevice;
    private readonly WindowRegistry _windowRegistry;
    private readonly IShaderLoader _shaderLoader;
    private PipelineBuilderInfo _info = new();

    /// <summary>
    /// Gets the shader loader for loading shaders from the content source.
    /// </summary>
    public IShaderLoader ShaderLoader => _shaderLoader;

    internal GraphicsPipelineBuilder(
        GpuDevice gpuDevice,
        WindowRegistry windowRegistry,
        IShaderLoader shaderLoader)
    {
        _gpuDevice = gpuDevice;
        _windowRegistry = windowRegistry;
        _shaderLoader = shaderLoader;
    }

    public GraphicsPipelineBuilder AddColorFormatFromDisplay(
        in BlendingState? blendingState = null,
        ColorComponentFlags? colorWriteMask = null)
    {
        return AddColorFormatFromDisplay(default, blendingState, colorWriteMask);
    }

    public GraphicsPipelineBuilder AddColorFormatFromDisplay(
        ViewScope viewScope,
        in BlendingState? blendingState = null,
        ColorComponentFlags? colorWriteMask = null)
    {
        Window window = _windowRegistry.GetWindow(viewScope);
        AddColorTarget(window.ColorTargetFormat, blendingState, colorWriteMask);
        return this;
    }

    public GraphicsPipelineBuilder AddColorTarget(TextureFormat textureFormat, in BlendingState? blendingState = null, ColorComponentFlags? colorWriteMask = null)
    {
        SDL_GPUColorTargetDescription description = new SDL_GPUColorTargetDescription
        {
            format = (SDL_GPUTextureFormat)textureFormat,
        };

        if (blendingState != null)
        {
            description.blend_state.enable_blend = true;
            description.blend_state.src_color_blendfactor = (SDL_GPUBlendFactor)blendingState.Value.SourceColorBlendFactor;
            description.blend_state.dst_color_blendfactor = (SDL_GPUBlendFactor)blendingState.Value.DestinationColorBlendFactor;
            description.blend_state.color_blend_op = (SDL_GPUBlendOp)blendingState.Value.ColorBlendOp;
            description.blend_state.src_alpha_blendfactor = (SDL_GPUBlendFactor)blendingState.Value.SourceAlphaBlendFactor;
            description.blend_state.dst_alpha_blendfactor = (SDL_GPUBlendFactor)blendingState.Value.DestinationAlphaBlendFactor;
            description.blend_state.alpha_blend_op = (SDL_GPUBlendOp)blendingState.Value.AlphaBlendOp;
        }

        if (colorWriteMask != null)
        {
            description.blend_state.enable_color_write_mask = true;
            description.blend_state.color_write_mask = (SDL_GPUColorComponentFlags)colorWriteMask.Value;
        }
        
        _info.SdlGpuColorTargetDescriptions.Add(description);

        return this;
    }

    public GraphicsPipelineBuilder AddVertexBufferConfigBasedOnBuffer<TVertexType>(GpuVertexBuffer<TVertexType> buffer,
        int? instanceStepRate = default) where TVertexType : unmanaged, IVertexType
    {
        return AddVertexBufferConfig<TVertexType>(instanceStepRate);
    }

    public GraphicsPipelineBuilder AddVertexBufferConfig<TVertexType>(int? instanceStepRate = default) where TVertexType : unmanaged, IVertexType
    {
        uint vertexTypeSizeBytes = (uint)Unsafe.SizeOf<TVertexType>();

        if (instanceStepRate.HasValue)
        {
            throw new NotSupportedException(
                "SDL GPU currently requires vertex buffer instance_step_rate to be 0. " +
                "Use SV_InstanceID with storage buffers for instancing, and keep firstInstance at 0 when the shader depends on SV_InstanceID.");
        }

        uint bufferSlot = (uint)_info.SdlGpuVertexBufferDescriptions.Count;
        SDL_GPUVertexBufferDescription sdlGpuVertexBufferDescription = new()
        {
            slot = bufferSlot,
            input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
            instance_step_rate = 0,
            pitch = vertexTypeSizeBytes
        };
        _info.SdlGpuVertexBufferDescriptions.Add(sdlGpuVertexBufferDescription);
        _info.VertexBufferTypeIds.Add(VertexTypeId<TVertexType>.Value);

        // Location continues from previous buffers
        uint location = (uint)_info.SdlGpuVertexAttributes.Count;
        uint offset = 0;

        foreach (VertexElementFormat vertexElementFormat in TVertexType.VertexElements)
        {
            _info.SdlGpuVertexAttributes.Add(new SDL_GPUVertexAttribute
            {
                buffer_slot = bufferSlot,
                format = (SDL_GPUVertexElementFormat)vertexElementFormat,
                location = location,
                offset = offset
            });

            // TODO: we may assert that the number of bytes is not higher than Unsafe.Size<TVertexType>()
            offset += (uint)vertexElementFormat.GetNumberOfBytes();
            location++;
        }

        return this;
    }
    
    public GraphicsPipelineBuilder SetShaderProgram(GraphicsShaderProgram shaderProgram)
    {
        _info.ShaderProgram = shaderProgram;

        return this;
    }

    public GraphicsPipelineBuilder SetShaderProgram(string path)
    {
        return SetShaderProgram(_shaderLoader.LoadGraphicsShaderProgram(path));
    }

    public GraphicsPipelineBuilder SetPrimitiveType(PrimitiveType primitiveType)
    {
        _info.PrimitiveType = primitiveType;
        return this;
    }

    public GraphicsPipelineBuilder EnableMultiSampling(SampleCount sampleCount, UInt32? mask = null)
    {
        // TODO: check the value with SDL_GPUTextureSupportsSampleCount
        _info.SdlGpuMultisampleState = _info.SdlGpuMultisampleState with
        {
            sample_count = (SDL_GPUSampleCount)sampleCount,
            enable_mask = mask.HasValue,
            sample_mask = mask ?? 0
        };

        return this;
    }

    public GraphicsPipelineBuilder EnableDepthTesting(DepthBufferFormat depthBufferFormat, bool write = true, CompareOperation compareOp = CompareOperation.Less)
    {
        return EnableDepthTesting((TextureFormat)depthBufferFormat, write, compareOp);
    }
    
    public GraphicsPipelineBuilder EnableReversedDepthTesting(DepthBufferFormat depthBufferFormat, bool write = true, CompareOperation compareOp = CompareOperation.ReversedLess)
    {
        return EnableDepthTesting((TextureFormat)depthBufferFormat, write, compareOp);
    }
    
    public GraphicsPipelineBuilder EnableReversedDepthTesting(TextureFormat depthBufferFormat, bool write = true, CompareOperation compareOp = CompareOperation.ReversedLess)
    {
        return EnableDepthTesting(depthBufferFormat, write, compareOp);
    }
    
    public GraphicsPipelineBuilder EnableDepthTesting(TextureFormat depthBufferFormat, bool write = true, CompareOperation compareOp = CompareOperation.Less)
    {
        if (depthBufferFormat == TextureFormat.None)
        {
            throw new ArgumentException($"{nameof(depthBufferFormat)} should be something else than {nameof(TextureFormat.None)} to be enabled");
        }
        
        _info.DepthBufferFormat = (DepthBufferFormat)depthBufferFormat;
        _info.SdlGpuDepthStencilState = _info.SdlGpuDepthStencilState with
        {
            enable_depth_test = true,
            enable_depth_write = write,
            compare_op = (SDL_GPUCompareOp)compareOp,
        };
        
        return this;
    }
    
    public GraphicsPipelineBuilder Custom(TextureFormat depthBufferFormat)
    {
        if (depthBufferFormat == TextureFormat.None)
        {
            throw new ArgumentException($"{nameof(depthBufferFormat)} should be something else than {nameof(TextureFormat.None)} to be enabled");
        }
        
        _info.DepthBufferFormat = (DepthBufferFormat)depthBufferFormat;
        _info.SdlGpuDepthStencilState = _info.SdlGpuDepthStencilState with
        {
            enable_depth_test = false,
            enable_depth_write = true,
            compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NEVER,
        };
        
        return this;
    }

    public GraphicsPipelineBuilder EnableStencilTesting(in StencilOperationState frontFacing, CompareOperation compareOperation, byte compareMask=0xFF, byte writeMask=0xFF)
    {
        return EnableStencilTesting(frontFacing, frontFacing, compareOperation, compareMask, writeMask);
    }
    
    public GraphicsPipelineBuilder EnableStencilTesting(in StencilOperationState frontFacing, in StencilOperationState backFacing, CompareOperation compareOperation, byte compareMask=0xFF, byte writeMask=0xFF)
    {
        _info.SdlGpuDepthStencilState = _info.SdlGpuDepthStencilState with
        {
            enable_stencil_test = true,
            compare_op = (SDL_GPUCompareOp)compareOperation,
            front_stencil_state = frontFacing,
            back_stencil_state = backFacing,
            compare_mask = compareMask,
            write_mask = writeMask
        };
        return this;
    }

    public GraphicsPipelineBuilder SetRasterizerState(RasterizerState rasterizerState)
    {
        _info.RasterizerState = rasterizerState;
        return this;
    }

    public GraphicsPipelineBuilder SetCullMode(CullMode cullMode)
    {
        _info.RasterizerState.CullMode = cullMode;
        return this;
    }

    public GraphicsPipelineBuilder SetFrontFace(FrontFace frontFace)
    {
        _info.RasterizerState.FrontFace = frontFace;
        return this;
    }

    public GraphicsPipelineBuilder SetFillMode(FillMode fillMode)
    {
        _info.RasterizerState.FillMode = fillMode;
        return this;
    }

    public GraphicsPipelineBuilder SetDepthBiasConstantFactor(float depthBiasConstantFactor)
    {
        _info.RasterizerState.DepthBiasConstantFactor = depthBiasConstantFactor;
        return this;
    }

    public GraphicsPipelineBuilder SetDepthBiasClamp(float depthBiasClamp)
    {
        _info.RasterizerState.DepthBiasClamp = depthBiasClamp;
        return this;
    }

    public GraphicsPipelineBuilder SetDepthBiasSlopeFactor(float depthBiasSlopeFactor)
    {
        _info.RasterizerState.DepthBiasSlopeFactor = depthBiasSlopeFactor;
        return this;
    }

    public GraphicsPipelineBuilder SetEnableDepthBias(bool enableDepthBias)
    {
        _info.RasterizerState.EnableDepthBias = enableDepthBias;
        return this;
    }

    public GraphicsPipelineBuilder SetEnableDepthClip(bool enableDepthClip)
    {
        _info.RasterizerState.EnableDepthClip = enableDepthClip;
        return this;
    }

    public GraphicsPipeline Build()
    {
        Span<SDL_GPUColorTargetDescription> sdlGpuColorTargetDescriptions =
            CollectionsMarshal.AsSpan(_info.SdlGpuColorTargetDescriptions);
        Span<SDL_GPUVertexBufferDescription> sdlGpuVertexBufferDescription =
            CollectionsMarshal.AsSpan(_info.SdlGpuVertexBufferDescriptions);
        Span<SDL_GPUVertexAttribute> sdlGpuVertexAttributes = CollectionsMarshal.AsSpan(_info.SdlGpuVertexAttributes);

        if (_info.VertexBufferTypeIds.Count == 0)
        {
            throw new InvalidOperationException("No vertex buffer configurations added. Call AddVertexBufferConfig at least once.");
        }

        if (sdlGpuVertexBufferDescription.Length == 0)
        {
            throw new InvalidOperationException("No vertex buffer descriptions configured.");
        }

        if (sdlGpuVertexAttributes.Length == 0)
        {
            throw new InvalidOperationException("No vertex attributes configured.");
        }

        GraphicsShaderProgram shaderProgram = _info.ShaderProgram ?? throw new InvalidOperationException(
            $"No graphics shader program configured. Call {nameof(SetShaderProgram)} before {nameof(Build)}.");

        if (shaderProgram.VertexShader.Pointer.IsNull)
        {
            throw new InvalidOperationException("Vertex shader has null pointer.");
        }

        if (shaderProgram.FragmentShader.Pointer.IsNull)
        {
            throw new InvalidOperationException("Fragment shader has null pointer.");
        }
        
        unsafe
        {
            fixed (SDL_GPUColorTargetDescription* sdlGpuColorTargetDescriptionsPointer = sdlGpuColorTargetDescriptions)
            fixed (SDL_GPUVertexBufferDescription* sdlGpuVertexBufferDescriptionPointer = sdlGpuVertexBufferDescription)
            fixed (SDL_GPUVertexAttribute* sdlGpuVertexAttributePointer = sdlGpuVertexAttributes)
            {
                SDL_GPUGraphicsPipelineCreateInfo sdlGpuGraphicsPipelineCreateInfo = new()
                {
                    target_info = new SDL_GPUGraphicsPipelineTargetInfo
                    {
                        num_color_targets = (uint)sdlGpuColorTargetDescriptions.Length,
                        color_target_descriptions = sdlGpuColorTargetDescriptionsPointer,
                        has_depth_stencil_target = _info.DepthBufferFormat.HasValue,
                        depth_stencil_format = _info.DepthBufferFormat.HasValue ? (SDL_GPUTextureFormat)_info.DepthBufferFormat : default,
                    },
                    vertex_input_state = new SDL_GPUVertexInputState
                    {
                        num_vertex_buffers = (uint)sdlGpuVertexBufferDescription.Length,
                        vertex_buffer_descriptions = sdlGpuVertexBufferDescriptionPointer,
                        num_vertex_attributes = (uint)sdlGpuVertexAttributes.Length,
                        vertex_attributes = sdlGpuVertexAttributePointer
                    },
                    primitive_type = (SDL_GPUPrimitiveType)_info.PrimitiveType,
                    vertex_shader = shaderProgram.VertexShader.Pointer,
                    fragment_shader = shaderProgram.FragmentShader.Pointer,
                    multisample_state = _info.SdlGpuMultisampleState,
                    depth_stencil_state = _info.SdlGpuDepthStencilState,
                    rasterizer_state = new SDL_GPURasterizerState
                    {
                        fill_mode = (SDL_GPUFillMode)_info.RasterizerState.FillMode,
                        cull_mode = (SDL_GPUCullMode)_info.RasterizerState.CullMode,
                        front_face = (SDL_GPUFrontFace)_info.RasterizerState.FrontFace,
                        depth_bias_constant_factor = _info.RasterizerState.DepthBiasConstantFactor,
                        depth_bias_clamp = _info.RasterizerState.DepthBiasClamp,
                        depth_bias_slope_factor = _info.RasterizerState.DepthBiasSlopeFactor,
                        enable_depth_bias = _info.RasterizerState.EnableDepthBias,
                        enable_depth_clip = _info.RasterizerState.EnableDepthClip
                    }
                };

                SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(
                    _gpuDevice.SdlGpuDevice,
                    &sdlGpuGraphicsPipelineCreateInfo);
                if (pipeline == null)
                {
                    throw new PixelyInitializationException(
                        $"SDL_CreateGPUGraphicsPipeline failed: {SDL3.SDL_GetError()}");
                }

                GraphicsPipeline graphicsPipeline = new GraphicsPipeline(
                    _gpuDevice,
                    pipeline,
                    [.. _info.VertexBufferTypeIds],
                    shaderProgram,
                    _info.DepthBufferFormat ?? DepthBufferFormat.None);
                _info.Reset();
                
                _gpuDevice.RegisterGraphicsPipeline(graphicsPipeline);
                return graphicsPipeline;
            }
        }
    }
}
