using System.Collections.Immutable;
using Pixely.ShaderCommon;
using Pixely.Utilities;
using SDL;

namespace Pixely.Gpu;

public class GraphicsPipeline: IDisposable
{
    private readonly GpuDevice _gpuDevice;
    internal Pointer<SDL_GPUGraphicsPipeline> Pointer { get; set; }

    /// <summary>
    /// Gets the vertex type IDs for each buffer slot configured in this pipeline.
    /// Index corresponds to buffer slot number.
    /// </summary>
    public ImmutableArray<VertexTypeId> VertexBufferTypeIds { get; }

    /// <summary>
    /// Gets the number of vertex buffer slots configured in this pipeline.
    /// </summary>
    public int VertexBufferSlotCount => VertexBufferTypeIds.Length;

    public GraphicsShaderProgram ShaderProgram { get; }
    public DepthBufferFormat DepthBufferFormat { get; }
    public SampleCount SampleCount { get; }

    internal GraphicsPipeline(
        GpuDevice gpuDevice,
        Pointer<SDL_GPUGraphicsPipeline> pointer,
        ImmutableArray<VertexTypeId> vertexBufferTypeIds,
        GraphicsShaderProgram shaderProgram,
        DepthBufferFormat depthBufferFormat,
        SampleCount sampleCount)
    {
        _gpuDevice = gpuDevice;
        Pointer = pointer;
        VertexBufferTypeIds = vertexBufferTypeIds;
        ShaderProgram = shaderProgram;
        DepthBufferFormat = depthBufferFormat;
        SampleCount = sampleCount;
    }


    public void Dispose()
    {
        _gpuDevice.ReleaseGraphicsPipeline(this);
    }
}
