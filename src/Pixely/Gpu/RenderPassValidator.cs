using Pixely.ShaderCommon;

namespace Pixely.Gpu;

/// <summary>
/// Validates render pass state with full validation checks.
/// </summary>
internal struct RenderPassValidator
{
    private const int MaxVertexBufferSlots = 8;

    private uint _verticesCount;
    private GpuIndexBuffer? _indexBuffer;
    private GraphicsPipeline? _graphicsPipeline;

    // Track bound vertex types per slot (up to 8 slots should be plenty)
    private VertexTypeId _slot0Type;
    private VertexTypeId _slot1Type;
    private VertexTypeId _slot2Type;
    private VertexTypeId _slot3Type;
    private VertexTypeId _slot4Type;
    private VertexTypeId _slot5Type;
    private VertexTypeId _slot6Type;
    private VertexTypeId _slot7Type;

    private ShaderCommon.StorageBufferElementSizes _vertexStorageBufferElementSizes;
    private ShaderCommon.StorageBufferElementSizes _fragmentStorageBufferElementSizes;

    public void OnBindGraphicsPipeline(DepthBufferFormat renderPassFormat, GraphicsPipeline graphicsPipeline)
    {
        _graphicsPipeline = graphicsPipeline;

        DepthBufferFormat pipelineFormat = graphicsPipeline.DepthBufferFormat;

        if (renderPassFormat != pipelineFormat)
        {
            throw new InvalidOperationException(
                $"Depth/stencil format mismatch: the render pass uses {renderPassFormat} but the pipeline was created with {pipelineFormat}. " +
                $"Ensure the depth buffer format passed to EnableDepthTesting matches the format of the depth buffer texture used in the render pass.");
        }

        // Reset slot bindings when pipeline changes
        _slot0Type = VertexTypeId.Null;
        _slot1Type = VertexTypeId.Null;
        _slot2Type = VertexTypeId.Null;
        _slot3Type = VertexTypeId.Null;
        _slot4Type = VertexTypeId.Null;
        _slot5Type = VertexTypeId.Null;
        _slot6Type = VertexTypeId.Null;
        _slot7Type = VertexTypeId.Null;
        _vertexStorageBufferElementSizes = default;
        _fragmentStorageBufferElementSizes = default;
    }

    public void OnBindVertexBuffer<TVertexType>(uint slot, GpuVertexBuffer<TVertexType> buffer)
        where TVertexType : unmanaged, IVertexType
    {
        if (slot >= MaxVertexBufferSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), $"Slot must be less than {MaxVertexBufferSlots}.");
        }

        if (slot == 0)
        {
            _verticesCount = (uint)buffer.Size;
        }

        VertexTypeId typeId = VertexTypeId<TVertexType>.Value;
        SetSlotType(slot, typeId);
    }

    public void OnBindIndexBuffer(GpuIndexBuffer buffer)
    {
        _indexBuffer = buffer;
    }

    private void SetSlotType(uint slot, VertexTypeId typeId)
    {
        switch (slot)
        {
            case 0: _slot0Type = typeId; break;
            case 1: _slot1Type = typeId; break;
            case 2: _slot2Type = typeId; break;
            case 3: _slot3Type = typeId; break;
            case 4: _slot4Type = typeId; break;
            case 5: _slot5Type = typeId; break;
            case 6: _slot6Type = typeId; break;
            case 7: _slot7Type = typeId; break;
        }
    }

    private readonly VertexTypeId GetSlotType(uint slot)
    {
        return slot switch
        {
            0 => _slot0Type,
            1 => _slot1Type,
            2 => _slot2Type,
            3 => _slot3Type,
            4 => _slot4Type,
            5 => _slot5Type,
            6 => _slot6Type,
            7 => _slot7Type,
            _ => VertexTypeId.Null
        };
    }

    public void OnBindVertexStorageBuffers(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers)
    {
        for (int i = 0; i < buffers.Length; i++)
        {
            _vertexStorageBufferElementSizes = SetStorageBufferSlotSize(_vertexStorageBufferElementSizes, slot + (uint)i, (ushort)buffers[i].ElementSize);
        }
    }

    public void OnBindFragmentStorageBuffers(uint slot, ReadOnlySpan<GpuStorageBuffer> buffers)
    {
        for (int i = 0; i < buffers.Length; i++)
        {
            _fragmentStorageBufferElementSizes = SetStorageBufferSlotSize(_fragmentStorageBufferElementSizes, slot + (uint)i, (ushort)buffers[i].ElementSize);
        }
    }

    // A rectangle outside the target is clipped to it by SetScissor, but a negative size cannot be
    // clipped into anything meaningful: it would silently become an empty scissor that draws nothing.
    internal static void ValidateScissorSize(Rectangle scissor)
    {
        if (scissor.Width < 0 || scissor.Height < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scissor),
                $"Scissor size must not be negative, but was {scissor.Width}x{scissor.Height}.");
        }
    }

    public readonly void OnDrawPrimitive(in CommandBufferState commandBuffer, uint firstInstance)
    {
        ValidateDrawState(commandBuffer);
        // SDL's first_vertex restriction is safe here because DrawPrimitive currently
        // hardcodes first_vertex to 0 in RenderPass.DrawPrimitiveInstanced.
        ValidateSystemValueInputs(firstInstance);
    }

    public readonly void OnDrawIndexedPrimitive(
        in CommandBufferState commandBuffer,
        uint indexCount,
        uint firstIndex,
        int vertexOffset,
        uint firstInstance)
    {
        ValidateDrawState(commandBuffer);
        ValidateSystemValueInputs(firstInstance);
        ValidateVertexOffset(vertexOffset);

        if (_indexBuffer == null)
        {
            throw new InvalidOperationException("IndexBuffer must be bound before indexed drawing.");
        }

        if (_indexBuffer.Size == 0)
        {
            throw new InvalidOperationException("Bound IndexBuffer is empty.");
        }

        if (firstIndex >= _indexBuffer.Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstIndex),
                $"First index {firstIndex} is outside the bound IndexBuffer size {_indexBuffer.Size}.");
        }

        if (indexCount > _indexBuffer.Size - firstIndex)
        {
            throw new ArgumentOutOfRangeException(
                nameof(indexCount),
                $"Index count {indexCount} starting at {firstIndex} exceeds the bound IndexBuffer size {_indexBuffer.Size}.");
        }
    }

    private readonly void ValidateDrawState(in CommandBufferState commandBuffer)
    {
        if (_graphicsPipeline == null)
        {
            throw new InvalidOperationException(
                $"{nameof(GraphicsPipeline)} must be bound.");
        }

        // Validate all configured slots have matching buffer types
        for (int i = 0; i < _graphicsPipeline.VertexBufferSlotCount; i++)
        {
            VertexTypeId expectedType = _graphicsPipeline.VertexBufferTypeIds[i];
            VertexTypeId boundType = GetSlotType((uint)i);

            if (boundType == VertexTypeId.Null)
            {
                throw new InvalidOperationException(
                    $"Vertex buffer slot {i} is not bound. Pipeline expects {_graphicsPipeline.VertexBufferSlotCount} buffer(s).");
            }

            if (expectedType != boundType)
            {
                throw new InvalidOperationException(
                    $"Vertex buffer type mismatch at slot {i}. Pipeline expects a different vertex type.");
            }
        }

        if (_verticesCount == 0)
        {
            throw new InvalidOperationException("Bound VertexBuffer at slot 0 is empty.");
        }

        ShaderBindingLayoutValidator.ValidateBindingCounts(
            _graphicsPipeline.ShaderProgram.FragmentShader.BindingLayout.BindingCounts,
            commandBuffer.RenderPass.FragmentShaderBindingCounts);

        ShaderBindingLayoutValidator.ValidateUniformSlotSizes(
            _graphicsPipeline.ShaderProgram.FragmentShader.BindingLayout.UniformSlotSizes,
            commandBuffer.FragmentShaderUniformSlotSizes);

        ShaderBindingLayoutValidator.ValidateUniformSlotSizes(
            _graphicsPipeline.ShaderProgram.VertexShader.BindingLayout.UniformSlotSizes,
            commandBuffer.VertexShaderUniformSlotSizes);

        ShaderBindingLayoutValidator.ValidateStorageBufferElementSizes("Vertex",
            _graphicsPipeline.ShaderProgram.VertexShader.BindingLayout.StorageBufferElementSizes,
            _vertexStorageBufferElementSizes);

        ShaderBindingLayoutValidator.ValidateStorageBufferElementSizes("Fragment",
            _graphicsPipeline.ShaderProgram.FragmentShader.BindingLayout.StorageBufferElementSizes,
            _fragmentStorageBufferElementSizes);
    }

    private static ShaderCommon.StorageBufferElementSizes SetStorageBufferSlotSize(ShaderCommon.StorageBufferElementSizes sizes, uint slot, ushort elementSize)
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

    private readonly void ValidateSystemValueInputs(uint firstInstance)
    {
        if (_graphicsPipeline == null)
        {
            return;
        }

        if (_graphicsPipeline.ShaderProgram.VertexShader.SystemValueInputs.UsesInstanceId && firstInstance != 0)
        {
            // SDL GPU: "first_vertex and first_instance parameters are NOT compatible
            // with built-in vertex/instance ID variables in shaders".
            // https://wiki.libsdl.org/SDL3/SDL_DrawGPUIndexedPrimitives
            throw new InvalidOperationException(
                "firstInstance must be 0 when the bound vertex shader uses SV_InstanceID. " +
                "SDL GPU does not define built-in instance IDs consistently for non-zero firstInstance values.");
        }
    }

    private readonly void ValidateVertexOffset(int vertexOffset)
    {
        if (_graphicsPipeline == null)
        {
            return;
        }

        if (_graphicsPipeline.ShaderProgram.VertexShader.SystemValueInputs.UsesVertexId && vertexOffset != 0)
        {
            // SDL GPU: "first_vertex and first_instance parameters are NOT compatible
            // with built-in vertex/instance ID variables in shaders".
            // https://wiki.libsdl.org/SDL3/SDL_DrawGPUIndexedPrimitives
            throw new InvalidOperationException(
                "vertexOffset must be 0 when the bound vertex shader uses SV_VertexID. " +
                "SDL GPU does not define built-in vertex IDs consistently for non-zero vertex offset values.");
        }
    }
}
